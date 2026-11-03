"""Loopback-only HTTP contract/failure tests with real Battle Server WebSockets.

No production credentials, accounts or databases are used.
"""
import http.server
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import time
import uuid
from WebSocketIntegration import Peer


def free_port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


def finish_normal(a, b):
    def command(client, name, **fields):
        before = client.seq
        client.send('SubmitCommand', MatchId=client.match, GameNumber=client.game,
                    SequenceNumber=client.seq, Command=dict(Type=name, **fields))
        for _ in range(128):
            message = client.read()
            assert message['Type'] not in ('ServerError', 'CommandRejected'), message.get('Reason')
            if message['Type'] == 'ActionChoiceRequired':
                choice = message['Payload']
                client.send('SubmitCommand', MatchId=client.match, GameNumber=client.game,
                            SequenceNumber=client.seq, Command=dict(Type='ResolveChoice', ChoiceId=choice['ChoiceId'],
                            Choices=[candidate['Token'] for candidate in choice['Candidates'][:choice['MinCount']]]))
            if message['Type'] == 'StateSnapshot' and client.seq > before:
                break
        else:
            raise AssertionError('command did not commit')
        other = b if client == a else a
        while other.seq <= before:
            other.until('StateSnapshot')

    # One side summons and attacks; the opponent passes. Every change uses
    # public commands, leaving the real GameCore to determine the winner.
    for _ in range(30):
        if a.state['Finished']:
            return
        active = a if a.state['ActivePlayerId'] == a.player else b
        if active == a:
            own = next(p for p in a.state['Players'] if p['PlayerId'] == a.player)
            playable = next((card for card in own['Hand'] if card['CanSummon']), None)
            if playable:
                command(a, 'PlayCard', CardId=playable['Id'])
            command(a, 'AdvancePhase')
            while not a.state['Finished']:
                own = next(p for p in a.state['Players'] if p['PlayerId'] == a.player)
                attacker = next((card for card in own['Field'] if card['CanDirectAttack']), None)
                if not attacker:
                    break
                command(a, 'DirectAttack', CardId=attacker['Id'])
        if not a.state['Finished']:
            command(active, 'EndTurn')
    assert a.state['Finished'], 'normal combat did not finish'


def run(executable):
    data = Path(executable).parent / 'Data' / 'HyakkiYagyo'
    catalog = json.loads((data / 'Cards.json').read_text(encoding='utf-8'))['CatalogVersion']
    decks = json.loads((data / 'Decks.json').read_text(encoding='utf-8'))['Decks']
    players = {str(i): str(uuid.uuid4()) for i in range(1, 7)}
    deck_ids = {token: str(uuid.uuid4()) for token in players}
    owned = {deck_ids[token]: (player, decks[(int(token) - 1) % len(decks)]['Cards']) for token, player in players.items()}
    key = uuid.uuid4().hex + uuid.uuid4().hex
    submissions, committed = [], {}
    failures = {'results': 1}

    class Api(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            assert self.headers.get('X-Battle-Api-Key') == key
            body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
            status, response = 200, {}
            if self.path.endswith('/verify'):
                assert set(body) == {'accessToken'}
                token = body['accessToken']
                if token == 'slow':
                    time.sleep(1)
                    token = '5'
                if token == 'unavailable':
                    status = 503
                elif token not in players:
                    status = 401
                else:
                    response = {'playerId': players[token], 'displayName': 'Test', 'createdAt': '2026-01-01T00:00:00Z'}
            elif self.path.endswith('/validate'):
                assert body == {'catalogVersion': catalog}
                parts = self.path.split('/')
                player, deck_id = parts[-4], parts[-2]
                deck = owned.get(deck_id)
                if not deck or deck[0] != player:
                    status = 404
                else:
                    response = dict(playerId=player, deckId=deck_id, snapshotId=str(uuid.uuid4()),
                                    catalogVersion=catalog, cardIds=deck[1])
            elif self.path.endswith('/matches'):
                assert set(body) == {'matchId', 'participants', 'winnerPlayerId', 'endReason', 'endedAt'}
                uuid.UUID(body['matchId'])
                assert len({p['playerId'] for p in body['participants']}) == 2
                for participant in body['participants']:
                    assert set(participant) == {'playerId', 'deckId', 'snapshotId'}
                    uuid.UUID(participant['snapshotId'])
                submissions.append(body)
                if failures['results']:
                    failures['results'] -= 1
                    status = 503
                elif body['matchId'] in committed:
                    previous, response = committed[body['matchId']]
                    assert previous == body, 'retry changed immutable result'
                    response = dict(response, duplicate=True)
                else:
                    response = dict(matchId=body['matchId'], duplicate=False, rankChanges=[
                        dict(playerId=p['playerId'], mmrBefore=1000, mmrAfter=1016 if p['playerId'] == body['winnerPlayerId'] else 984,
                             tier='Bronze', division=5, rp=20, rpDelta=20 if p['playerId'] == body['winnerPlayerId'] else 0,
                             promotionStatus='None') for p in reversed(body['participants'])])
                    committed[body['matchId']] = body, response
            else:
                status = 404
            output = json.dumps(response).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(output)))
            self.end_headers()
            self.wfile.write(output)

    api = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Api)
    threading.Thread(target=api.serve_forever, daemon=True).start()
    peers = []
    with tempfile.TemporaryDirectory(prefix='projectc-website-') as root:
        env = dict(os.environ, WEBSITE_API_URL=f'http://127.0.0.1:{api.server_port}', BATTLE_API_KEY=key,
                   BATTLE_RESULT_OUTBOX=root)
        port = free_port()
        log = open(Path(root) / 'server.log', 'w+', encoding='utf-8')
        server = subprocess.Popen([executable, '--port', str(port), '--mulligan', 'off', '--data', str(data)],
                                  env=env, stdout=log, stderr=subprocess.STDOUT)

        def peer():
            result = Peer(port)
            result.socket.settimeout(15)
            peers.append(result)
            return result

        def queue(client, token):
            client.send('QueueMatch', AccessToken=token, DeckId=deck_ids.get(token, deck_ids['1']))

        try:
            for _ in range(100):
                try:
                    a = peer()
                    break
                except ConnectionRefusedError:
                    time.sleep(.1)
            else:
                raise AssertionError('server did not start')
            a.send('Authenticate', AccessToken='invalid')
            assert a.until('ServerError')['Reason'] == 'AuthenticationFailed'
            a.send('Authenticate', AccessToken='1')
            a.until('Authenticated')
            queue(a, 'invalid')
            assert a.until('ServerError')['Reason'] == 'AuthenticationFailed'
            queue(a, 'unavailable')
            assert a.until('ServerError')['Reason'] == 'WebsiteUnavailable'
            a.send('QueueMatch', AccessToken='1', DeckId=deck_ids['2'])
            assert a.until('ServerError')['Reason'] == 'DeckNotFound'
            queue(a, '1')
            a.until('MatchmakingQueued')
            queue(a, '1')
            assert a.until('ServerError')['Reason'] == 'AlreadyQueued'
            duplicate = peer()
            queue(duplicate, '1')
            assert duplicate.until('ServerError')['Reason'] == 'AccountBusy'
            a.send('CancelQueue')
            a.until('MatchmakingCancelled')
            slow = peer()
            queue(slow, 'slow')
            slow.send('CancelQueue')
            slow.until('MatchmakingCancelled')
            departing = peer()
            queue(departing, '6')
            departing.close()
            queue(a, '1')
            a.until('MatchmakingQueued')
            b = peer()
            queue(b, '2')
            b.until('MatchmakingQueued')
            for client in (a, b):
                client.until('MatchmakingFound')
                client.until('StateSnapshot')
                assert client.state['Started'] and client.game == 1
                opponent = next(p for p in client.state['Players'] if p['PlayerId'] != client.player)
                assert opponent['Hand'] == []
            assert a.match == b.match
            match_id = a.match
            uuid.UUID(match_id)
            own_a = next(p for p in a.state['Players'] if p['PlayerId'] == a.player)
            own_b = next(p for p in b.state['Players'] if p['PlayerId'] == b.player)
            assert {c['DefinitionId'] for c in own_a['Hand']} <= set(owned[deck_ids['1']][1])
            assert {c['DefinitionId'] for c in own_b['Hand']} <= set(owned[deck_ids['2']][1])
            a.send('SubmitDeck', MatchId=a.match)
            assert a.until('ServerError')['Reason'] == 'RankedMatchLocked'
            queue(duplicate, '1')
            assert duplicate.until('ServerError')['Reason'] == 'AccountBusy'
            token, player = a.token, a.player
            a.close()
            time.sleep(.2)
            a = peer()
            a.send('JoinMatch', MatchId=match_id, SessionToken=token)
            assert a.until('MatchJoined')['PlayerId'] == player
            a.until('StateSnapshot')
            finish_normal(a, b)
            assert a.state['Finished'] and b.state['Finished']
            a.until('MatchEnded')
            b.until('MatchEnded')
            for client, expected in ((a, players['1']), (b, players['2'])):
                change = client.until('RankUpdated')['Payload']
                assert change['playerId'] == expected
            assert submissions[0] == submissions[1] and len(committed) == 1
            assert submissions[0]['endReason'] == 'Normal'
            # Reinsert an already committed body: models response loss or replay after restart.
            job = Path(root) / (match_id + '.json')
            job.write_text(json.dumps(dict(body=submissions[0], attempts=0, nextAttempt=0)), encoding='utf-8')
            deadline = time.time() + 5
            while job.exists() and time.time() < deadline:
                time.sleep(.05)
            assert not job.exists() and len(committed) == 1 and len(submissions) == 3
            # Retry budget survives restarts: an exhausted job stays on disk for review.
            failed_body = dict(submissions[0], matchId=str(uuid.uuid4()))
            failed_job = Path(root) / (failed_body['matchId'] + '.json')
            failures['results'] = 100
            failed_job.write_text(json.dumps(dict(body=failed_body, attempts=5, nextAttempt=0)), encoding='utf-8')
            deadline = time.time() + 5
            while time.time() < deadline:
                try:
                    if json.loads(failed_job.read_text(encoding='utf-8')).get('lastStatus') == 503:
                        break
                except (json.JSONDecodeError, PermissionError):
                    pass
                time.sleep(.05)
            assert json.loads(failed_job.read_text(encoding='utf-8'))['attempts'] == 6
            count = len(submissions)
            time.sleep(1.2)
            assert len(submissions) == count and failed_job.exists()
            for client in peers:
                for message in client.audit:
                    serialized = json.dumps(message)
                    assert key not in serialized and 'accessToken' not in serialized and 'snapshotId' not in serialized
            print('Website integration: auth/errors, ownership, duplicate queue/account, cancel/disconnect, automatic start, private decks, reconnect, normal result, retry/idempotency and private rank changes passed')
        finally:
            for client in peers:
                if client.connected:
                    client.close()
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait()
            log.seek(0)
            output = log.read()
            assert key not in output
            log.close()
            api.shutdown()
            api.server_close()


if __name__ == '__main__':
    run(str(Path(sys.argv[1]).resolve()))
