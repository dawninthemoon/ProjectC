"""Real RFC6455 loopback test. Uses only Python standard library, including CI."""
import base64
import hashlib
import json
import os
import socket
import struct
import subprocess
import sys
import time

class Peer:
    def __init__(self, port):
        self.socket = socket.create_connection(('127.0.0.1', port), timeout=5)
        self.socket.settimeout(5)
        key = base64.b64encode(os.urandom(16)).decode()
        self.socket.sendall((f'GET / HTTP/1.1\r\nHost: localhost:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
        header = b''
        while not header.endswith(b'\r\n\r\n'):
            header += self.exact(1)
        expected = base64.b64encode(hashlib.sha1((key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest())
        assert b'101 Switching Protocols' in header and expected in header
        self.seq = 0
        self.counter = 0
        self.state = None
        self.events = []
    def exact(self, length):
        data = b''
        while len(data) < length:
            chunk = self.socket.recv(length-len(data))
            if not chunk:
                raise EOFError('connection closed')
            data += chunk
        return data
    def raw(self, text):
        data = text.encode()
        length = len(data)
        header = bytes([0x81, 0x80 | length]) if length < 126 else bytes([0x81, 0xFE])+struct.pack('!H', length)
        mask = os.urandom(4)
        self.socket.sendall(header + mask + bytes(v ^ mask[i % 4] for i, v in enumerate(data)))
    def send(self, kind, **fields):
        self.counter += 1
        data = dict(Type=kind, ProtocolVersion=1, RequestId=str(self.counter), **fields)
        self.raw(json.dumps(data))
        return data
    def read(self):
        first, second = self.exact(2)
        size = second & 127
        if size == 126: size = struct.unpack('!H', self.exact(2))[0]
        elif size == 127: size = struct.unpack('!Q', self.exact(8))[0]
        payload = self.exact(size)
        if first & 15 == 8: raise EOFError('websocket close')
        assert first == 0x81, first
        result = json.loads(payload)
        if result['Type'] in ('GameEvents', 'StateSnapshot', 'MatchStarted'):
            assert result['SequenceNumber'] >= self.seq
            self.seq = result['SequenceNumber']
        if result['Type'] == 'GameEvents': self.events.append(result)
        if result['Type'] == 'StateSnapshot': self.state = result['Payload']
        return result
    def until(self, kind):
        for _ in range(64):
            data = self.read()
            if data['Type'] == kind: return data
        raise AssertionError('message not received: '+kind)
    def close(self):
        self.socket.close()

def public(state):
    result = json.loads(json.dumps(state))
    for p in result['Players']: p.pop('Hand')
    return result

def run(port):
    a, b, c = Peer(port), Peer(port), Peer(port)
    a.send('JoinMatch'); joined = a.until('MatchJoined'); token = joined['SessionToken']; assert joined['PlayerId'] == 1
    a.until('StateSnapshot')
    b.send('JoinMatch'); joined_b = b.until('MatchJoined'); assert joined_b['PlayerId'] == 2
    token_b = joined_b['SessionToken']
    a.until('StateSnapshot'); b.until('StateSnapshot')
    assert a.seq == b.seq == 1
    assert a.state['Players'][1]['Hand'] == b.state['Players'][0]['Hand'] == []
    assert a.state['Players'][0]['Hand'][0]['DefinitionId'] == 10
    assert b.state['Players'][1]['Hand'][0]['DefinitionId'] == 20
    for peer in (a,b):
        for player in peer.state['Players']: assert 'Deck' not in player
    c.send('JoinMatch'); assert c.until('ServerError')['Reason'] == 'MatchFull'
    c.send('JoinMatch', SessionToken=token); assert c.until('ServerError')['Reason'] == 'SessionConnected'
    for raw in ('{', '{}', '[]', 'null', '{"Type":"JoinMatch","ProtocolVersion":true,"RequestId":"x"}'):
        c.raw(raw); assert c.until('ServerError')['Reason'] == 'InvalidRequest'
    def reject(peer, reason, **fields):
        peer.send('SubmitCommand', MatchId=fields.pop('MatchId','local-1'), SequenceNumber=fields.pop('SequenceNumber',peer.seq), **fields)
        assert peer.until('CommandRejected')['Reason'] == reason
    reject(a, 'PlayerMismatch', PlayerId=2, Command={'Type':'PlayCard','CardId':1})
    reject(a, 'WrongMatch', MatchId='wrong', Command={'Type':'StartTurn'})
    reject(a, 'StaleSequence', SequenceNumber=0, Command={'Type':'StartTurn'})
    for name in ('ChangeLife','Transform','Draw','ActivateEffect'):
        reject(a, 'CommandNotAllowed', Command={'Type':name})
    # Player identity is socket-bound; ownership checks still reject enemy card IDs.
    a.send('SubmitCommand', MatchId='local-1', SequenceNumber=a.seq, Command={'Type':'PlayCard','CardId':3})
    assert a.until('CommandRejected')['Reason'].startswith('GameRule:')
    a.send('SubmitCommand', MatchId='local-1', SequenceNumber=a.seq, Command={'Type':'PlayCard','CardId':1.5})
    assert a.until('ServerError')['Reason'] == 'InvalidRequest'
    # Missing, mistyped, out-of-range and duplicate fields cannot mutate game state.
    for malformed in (
        '{"Type":"SubmitCommand","ProtocolVersion":1,"RequestId":"m","MatchId":"local-1","Command":{"Type":"PlayCard","CardId":1}}',
        '{"Type":"StateResyncRequest","ProtocolVersion":1,"RequestId":"m","MatchId":"local-1","PlayerId":-1}',
        '{"Type":"StateResyncRequest","ProtocolVersion":1,"RequestId":"m","MatchId":"local-1","PlayerId":18446744073709551616}',
        '{"Type":"JoinMatch","Type":"StateResyncRequest","ProtocolVersion":1,"RequestId":"m","MatchId":"local-1"}',
        '['*40 + '0' + ']'*40,
    ):
        a.raw(malformed); assert a.until('ServerError')['Reason'] == 'InvalidRequest'
    def command(peer, name, **fields):
        before = peer.seq
        request = peer.send('SubmitCommand', MatchId='local-1', SequenceNumber=before, Command=dict(Type=name,**fields))
        response = peer.read()
        assert response['Type'] == 'CommandAccepted', response
        assert response['RequestId'] == request['RequestId'] and response['SequenceNumber'] == before+1
        a.until('StateSnapshot'); b.until('StateSnapshot')
        assert a.seq == b.seq == before+1
        assert public(a.state) == public(b.state)
        assert a.events[-1]['SequenceNumber'] == a.seq == b.events[-1]['SequenceNumber']
        return request
    # B misses the summon while disconnected; reconnect must reconstruct it from a snapshot.
    b.close(); time.sleep(.1)
    request = a.send('SubmitCommand',MatchId='local-1',SequenceNumber=a.seq,Command={'Type':'PlayCard','CardId':1})
    assert a.until('CommandAccepted')['SequenceNumber'] == 2
    a.until('StateSnapshot')
    b = Peer(port); b.send('JoinMatch',MatchId='local-1',SessionToken=token_b)
    assert b.until('MatchJoined')['PlayerId'] == 2
    b.until('StateSnapshot'); b.counter = 200
    assert b.seq == a.seq == 2 and public(a.state) == public(b.state)
    assert b.state['Players'][0]['Hand'] == [] and b.state['Players'][1]['Hand'][0]['Id'] == 3
    a.raw(json.dumps(request)); assert a.until('CommandAccepted')['SequenceNumber'] == 2
    a.until('StateSnapshot'); assert a.seq == 2
    request['Command']['CardId'] = 2
    a.raw(json.dumps(request)); assert a.until('CommandRejected')['Reason'] == 'RequestIdConflict'
    assert a.state['Players'][0]['Field'][0]['Id'] == 1
    # Two pipelined commands based on one state: exactly one may advance it.
    previous = a.seq
    a.send('SubmitCommand',MatchId='local-1',SequenceNumber=previous,Command={'Type':'AdvancePhase'})
    a.send('SubmitCommand',MatchId='local-1',SequenceNumber=previous,Command={'Type':'AdvancePhase'})
    assert a.until('CommandAccepted')['SequenceNumber'] == previous+1
    a.until('StateSnapshot'); b.until('StateSnapshot')
    assert a.until('CommandRejected')['Reason'] == 'StaleSequence'
    assert a.seq == b.seq == previous+1 and a.state['Phase'] == 1
    # Recover using full private projection; no missed-event replay is needed.
    prior = a.state
    a.close(); time.sleep(.1)
    a = Peer(port); a.send('JoinMatch',MatchId='local-1',SessionToken=token)
    assert a.until('MatchJoined')['PlayerId'] == 1
    a.until('StateSnapshot'); assert a.state == prior
    # Use new unique IDs; cached request IDs persist across reconnect.
    a.counter = 100
    request['Command']['CardId'] = 1
    a.raw(json.dumps(request)); assert a.until('CommandAccepted')['SequenceNumber'] == 2
    a.until('StateSnapshot'); assert a.state == prior
    a.send('StateResyncRequest',MatchId='local-1'); a.until('StateSnapshot'); assert a.state == prior
    def end(peer):
        command(peer,'AdvancePhase'); command(peer,'AdvancePhase'); command(peer,'EndTurn')
    command(a,'AdvancePhase'); command(a,'EndTurn'); command(b,'StartTurn'); command(b,'PlayCard',CardId=3)
    end(b); command(a,'StartTurn'); command(a,'AdvancePhase'); command(a,'Attack',CardId=1,TargetCardId=3)
    assert a.state['Players'][0]['Field'][0]['Hp'] == 1
    assert len(a.state['Players'][1]['Graveyard']) == 1
    command(a,'AdvancePhase'); command(a,'EndTurn'); command(b,'StartTurn'); end(b)
    command(a,'StartTurn'); command(a,'AdvancePhase'); command(a,'DirectAttack',CardId=1)
    assert a.state['Finished'] and a.state['WinnerPlayerId'] == 1
    assert a.until('MatchEnded')['WinnerPlayerId'] == b.until('MatchEnded')['WinnerPlayerId'] == 1
    c.send('JoinMatch',SessionToken='0'*64); assert c.until('ServerError')['Reason'] == 'InvalidSession'
    c.raw('x'*16385)
    try: c.read(); raise AssertionError('oversize message accepted')
    except (EOFError, ConnectionError): pass
    a.close(); b.close(); c.close()
    print('Real WebSocket: matching, validation, authority, deduplication, sequence, privacy, reconnect, battle and victory passed')

if __name__ == '__main__':
    with socket.socket() as probe:
        probe.bind(('127.0.0.1',0)); port = probe.getsockname()[1]
    server = subprocess.Popen([sys.argv[1], '--port', str(port)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        line = server.stdout.readline()
        assert 'listening' in line, line
        run(port)
    finally:
        server.terminate()
        try: server.wait(timeout=5)
        except subprocess.TimeoutExpired: server.kill(); server.wait()

