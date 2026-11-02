"""Personal decks over real RFC6455 connections, using production content/rules."""
import json
import os
import socket
import subprocess
import sys
import time
from WebSocketIntegration import Peer


def run(server_path):
    data = os.path.join(os.path.dirname(server_path), 'Data', 'HyakkiYagyo')
    with open(os.path.join(data, 'Cards.json'), encoding='utf-8') as f:
        version = json.load(f)['CatalogVersion']
    with open(os.path.join(data, 'Decks.json'), encoding='utf-8') as f:
        cards = json.load(f)['Decks'][0]['Cards']
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        port = probe.getsockname()[1]
    server = subprocess.Popen([server_path, '--port', str(port), '--data', data],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    peers = []
    try:
        assert 'listening' in server.stdout.readline()
        a, b = Peer(port), Peer(port)
        peers.extend([a, b])
        a.send('ListDecks')
        catalog = a.until('DeckCatalog')
        assert catalog['CatalogVersion'] == version and catalog['PersonalDeckSupported']
        a.send('CreateMatch'); a.until('MatchCreated'); a.until('StateSnapshot')
        b.send('JoinMatch', MatchId=a.match); b.until('MatchJoined'); b.until('StateSnapshot')
        base = dict(MatchId=a.match, GameNumber=0, CatalogVersion=version,
                    DeckName='온라인 개인 덱', DefinitionIds=cards)
        for payload, reason in [
            (dict(base, DefinitionIds=[999999] + cards[1:]), 'DeckUnknownCard'),
            (dict(base, DefinitionIds=cards[:-1]), 'DeckCardCount'),
            (dict(base, DefinitionIds=[1001] * 30), 'DeckCopyLimit'),
            (dict(base, DefinitionIds=[1007] + cards[1:]), 'DeckIneligibleCard'),
            (dict(base, CatalogVersion='old'), 'CatalogVersionMismatch'),
            (dict(base, Effects=[]), 'InvalidDeckPayload'),
        ]:
            a.send('SubmitDeck', **payload)
            error = a.until('DeckRejected')
            assert error['Reason'] == reason and error['Message']
        for text in ['{', '{"Type":"SubmitDeck","Type":"SetReady"}', '[' * 40 + '0' + ']' * 40]:
            a.raw(text)
            assert a.until('ServerError')['Reason'] == 'InvalidRequest'
        request = a.send('SubmitDeck', **base)
        accepted = a.until('DeckAccepted')
        a.raw(json.dumps(request)); assert a.until('DeckAccepted') == accepted
        request['DefinitionIds'] = [2004] + cards[1:]
        a.raw(json.dumps(request)); assert a.until('DeckRejected')['Reason'] == 'RequestIdConflict'
        a.send('SetReady', MatchId=a.match, GameNumber=0, Ready=True); a.until('LobbyAccepted')
        b.send('SubmitDeck', **dict(base, DeckName='다른 개인 덱', DefinitionIds=[2004] + cards[1:]))
        b.until('DeckAccepted')
        b.send('SetReady', MatchId=b.match, GameNumber=0, Ready=True); b.until('LobbyAccepted')
        a.until('StateSnapshot'); b.until('StateSnapshot')
        assert a.state['Started'] and b.state['Started'] and a.seq == b.seq == 1
        assert a.state['Players'][1]['Hand'] == [] and b.state['Players'][0]['Hand'] == []
        assert not any('DefinitionIds' in m for m in a.audit + b.audit)
        assert not any(m.get('DeckName') == '다른 개인 덱' for m in a.audit)
        before = a.state
        a.send('SubmitDeck', **dict(base, GameNumber=1, DefinitionIds=[2004] + cards[1:]))
        assert a.until('DeckRejected')['Reason'] == 'DeckLocked'
        token, match = a.token, a.match
        a.close(); time.sleep(.1)
        restored = Peer(port); peers.append(restored)
        restored.send('JoinMatch', MatchId=match, SessionToken=token)
        restored.until('MatchJoined'); restored.until('StateSnapshot')
        assert restored.state == before
        restored.send('SubmitCommand', MatchId=match, GameNumber=1, SequenceNumber=1, Command={'Type': 'EndTurn'})
        restored.until('CommandAccepted'); restored.until('StateSnapshot')
        assert restored.seq == 2
        restored.send('CloseMatch', MatchId=match, GameNumber=1); restored.until('MatchClosed')
    finally:
        for peer in peers: peer.close()
        server.terminate()
        output, _ = server.communicate(timeout=5)
    assert 'replayVerified=true' in output, output
    print('Personal deck WebSocket validation, privacy, reconnect, immutable match and replay passed')


if __name__ == '__main__':
    run(os.path.abspath(sys.argv[1]))
