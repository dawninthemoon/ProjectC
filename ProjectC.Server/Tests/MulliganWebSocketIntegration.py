"""Two concurrent RFC6455 peers, mulligan recovery, privacy and server deadline."""
import json
import os
import socket
import subprocess
import sys
import time
from WebSocketIntegration import Peer


def start_pair(port):
    a, b = Peer(port), Peer(port)
    a.send('CreateMatch'); a.until('MatchCreated'); a.until('StateSnapshot')
    b.send('JoinMatch', MatchId=a.match); b.until('MatchJoined'); b.until('StateSnapshot')
    for peer in (a, b):
        peer.send('SetReady', MatchId=peer.match, GameNumber=0, Ready=True)
        peer.until('LobbyAccepted')
    for peer in (a, b): peer.until('StateSnapshot')
    assert a.state['MulliganActive'] and not a.state['TurnStarted'] and a.seq == b.seq == 1
    return a, b


def mulligan(peer, ids):
    return peer.send('SubmitCommand', MatchId=peer.match, GameNumber=1,
                     SequenceNumber=1, Command=dict(Type='SubmitMulligan', CardIds=ids))


def run(server_path):
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0)); port = probe.getsockname()[1]
    data = os.path.join(os.path.dirname(server_path), 'Data', 'HyakkiYagyo')
    server = subprocess.Popen([server_path, '--port', str(port), '--data', data,
                               '--mulligan', 'on', '--mulligan-timeout', '4'],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    peers = []
    try:
        assert 'listening' in server.stdout.readline()
        a, b = start_pair(port); peers.extend([a, b])
        hand = a.state['Players'][0]['Hand']; assert len(hand) == 5
        own, enemy = hand[0]['Id'], b.state['Players'][1]['Hand'][0]['Id']
        for cards in ([own, own], [enemy], [999999], [own] * 6):
            mulligan(a, cards)
            assert 'InvalidMulliganSelection' in a.until('CommandRejected')['Reason']
        a.send('SubmitCommand', MatchId=a.match, GameNumber=1, SequenceNumber=1, Command=dict(Type='EndTurn'))
        assert a.until('CommandRejected')['Reason'] == 'MulliganPending'
        # Disconnect before submitting: initial private hand must survive exactly.
        token, match = a.token, a.match; a.close(); time.sleep(.1)
        a = Peer(port); peers.append(a)
        a.send('JoinMatch', MatchId=match, SessionToken=token); a.until('MatchJoined'); a.until('StateSnapshot')
        assert a.state['Players'][0]['Hand'] == hand
        request = mulligan(a, [own]); a.until('CommandAccepted'); a.until('StateSnapshot')
        assert a.state['MulliganActive'] and not a.state['TurnStarted']
        assert own not in [c['Id'] for c in a.state['Players'][0]['Hand']]
        saved = a.state['Players']; token = a.token; a.close(); time.sleep(.1)
        a = Peer(port); peers.append(a)
        a.send('JoinMatch', MatchId=match, SessionToken=token); a.until('MatchJoined'); a.until('StateSnapshot')
        assert a.state['Players'] == saved and a.state['Players'][0]['MulliganSelectedIds'] == [own]
        mulligan(b, [])  # stale snapshot from same preparation epoch is intentional
        b.until('CommandAccepted'); b.until('StateSnapshot'); a.until('StateSnapshot')
        assert not a.state['MulliganActive'] and a.state['TurnStarted']
        assert a.state['Players'][0]['HandCount'] == 6 and a.state['Players'][0]['YokaiPower'] == 2
        for message in b.audit:
            if message['Type'] == 'StateSnapshot' and message['Payload']['Players']:
                opponent = message['Payload']['Players'][0]
                assert opponent['Hand'] == [] and opponent['MulliganSelectedIds'] == []
            assert 'MulliganRandom' not in json.dumps(message) and 'Seed' not in message
        a.send('CloseMatch', MatchId=match, GameNumber=1); a.until('MatchClosed')
        # Both sends precede either receive; exactly two decisions and one first turn.
        c, d = start_pair(port); peers.extend([c, d])
        mulligan(c, []); mulligan(d, [card['Id'] for card in d.state['Players'][1]['Hand']])
        for peer in (c, d):
            peer.until('CommandAccepted')
            while peer.state['MulliganActive']: peer.until('StateSnapshot')
            assert peer.seq == 3 and peer.state['TurnNumber'] == 1
        c.send('CloseMatch', MatchId=c.match, GameNumber=1); c.until('MatchClosed')
        # Disconnected player still gets the configured safe default at deadline.
        e, f = start_pair(port); peers.extend([e, f]); f.close()
        while e.state['MulliganActive']: e.until('StateSnapshot')
        assert all(p['MulliganComplete'] for p in e.state['Players']) and e.state['TurnStarted']
        e.send('CloseMatch', MatchId=e.match, GameNumber=1); e.until('MatchClosed')
    finally:
        for peer in peers: peer.close()
        server.terminate(); output, _ = server.communicate(timeout=5)
    assert output.count('replayVerified=true') == 3, output
    print('Mulligan WebSocket concurrent decisions, reconnect, privacy, timeout and 3 replays passed')


if __name__ == '__main__':
    run(os.path.abspath(sys.argv[1]))
