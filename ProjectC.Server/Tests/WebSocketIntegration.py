"""Real RFC6455 multi-match loopback tests. Python standard library only."""
import base64
import hashlib
import json
import os
import select
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
        self.match = ''
        self.game = 0
        self.player = 0
        self.token = ''
        self.lobby = None
        self.connected = True
        self.audit = []
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
        data = dict(Type=kind, ProtocolVersion=2, RequestId=str(self.counter), **fields)
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
        self.audit.append(result)
        kind = result['Type']
        if kind in ('MatchCreated','MatchJoined'):
            self.match = result['MatchId']; self.player = result['PlayerId']; self.token = result['SessionToken']
        if kind in ('GameEvents','StateSnapshot','LobbyState','MatchStarted','SessionUpdated'):
            assert result['MatchId'] == self.match, ('foreign match event',result,self.match)
            if result['GameNumber'] > self.game:
                self.game = result['GameNumber']; self.seq = 0
            assert result['GameNumber'] == self.game
        if kind in ('GameEvents','StateSnapshot'):
            assert result['SequenceNumber'] >= self.seq, result
            self.seq = result['SequenceNumber']
        if kind == 'SessionUpdated': self.token = result['SessionToken']
        if kind == 'MatchEnded': self.token = ''
        if kind == 'GameEvents': self.events.append(result)
        if kind == 'LobbyState': self.lobby = result['Payload']
        if kind == 'StateSnapshot': self.state = result['Payload']
        if kind in ('MatchLeft','MatchClosed'):
            self.match = ''; self.token = ''; self.player = 0; self.seq = 0; self.game = 0
        return result
    def until(self, kind):
        for _ in range(64):
            data = self.read()
            if kind == 'CommandAccepted' and data['Type'] in ('CommandRejected','ServerError'):
                raise AssertionError(('unexpected rejection', data.get('Reason')))
            if data['Type'] == kind: return data
        raise AssertionError('message not received: '+kind)
    def close(self):
        self.connected = False
        self.socket.close()


def public(state):
    result=json.loads(json.dumps(state))
    for player in result['Players']:
        player.pop('Hand')
        for zone in ('Field','Graveyard'):
            for card in player[zone]:
                card.pop('CanSummon'); card.pop('CanAttack'); card.pop('Activations')
    return result

def quiet(peer):
    if select.select([peer.socket],[],[],.01)[0]:
        raise AssertionError(('unexpected other-match traffic',peer.read()))

def run(port,check_limits=False,check_expiry=False):
    a,b,c,d,e=[Peer(port) for _ in range(5)]
    tokens=[]
    a.send('ListDecks'); assert len(a.until('DeckCatalog')['Payload']) >= 2
    a.send('CreateMatch',DeckId='Strikers'); a.until('MatchCreated'); id_a=a.match; pre_token=a.token; a.until('StateSnapshot')
    b.send('JoinMatch',MatchId=id_a,DeckId='Wardens'); b.until('MatchJoined'); b.until('StateSnapshot')
    c.send('CreateMatch',DeckId='Wardens'); c.until('MatchCreated'); id_b=c.match; c.until('StateSnapshot')
    d.send('JoinMatch',MatchId=id_b,DeckId='Strikers'); d.until('MatchJoined'); d.until('StateSnapshot')
    assert id_a != id_b and len({p.player for p in (a,b,c,d)}) == 4
    def start(x,y):
        for p in (x,y):
            p.send('SetReady',MatchId=p.match,GameNumber=0,Ready=True)
            p.until('LobbyAccepted')
        for p in (x,y): p.until('StateSnapshot'); assert p.state['Started'] and p.state['TurnStarted']
        assert x.seq == y.seq == 1 and public(x.state)==public(y.state)
        assert x.state['Players'][1]['Hand'] == y.state['Players'][0]['Hand'] == []
        for p in (x,y):
            tokens.append(p.token)
            for player in p.state['Players']: assert 'Deck' not in player
    start(a,b); start(c,d)
    if check_limits:
        e.send('CreateMatch');assert e.until('ServerError')['Reason']=='MatchLimit'

    group_a=[a,b]; group_b=[c,d]
    def command(peer,name,**fields):
        group=group_a if peer.match==id_a else group_b
        old=peer.seq
        request=peer.send('SubmitCommand',MatchId=peer.match,GameNumber=peer.game,SequenceNumber=old,Command=dict(Type=name,**fields))
        accepted=peer.until('CommandAccepted')
        assert accepted['RequestId']==request['RequestId'] and accepted['SequenceNumber']==old+1
        for p in group:
            if p.connected: p.until('StateSnapshot'); assert p.seq==old+1 and p.state['TurnStarted']
        if all(p.connected for p in group): assert public(group[0].state)==public(group[1].state)
        return request
    def reject(peer,reason,**fields):
        peer.send('SubmitCommand',MatchId=fields.pop('MatchId',peer.match),GameNumber=fields.pop('GameNumber',peer.game),
                  SequenceNumber=fields.pop('SequenceNumber',peer.seq),**fields)
        assert peer.until('CommandRejected')['Reason']==reason
    # Session, MatchId and command ownership boundaries.
    e.send('JoinMatch',MatchId=id_a); assert e.until('ServerError')['Reason']=='MatchNotWaiting'
    a.send('JoinMatch',MatchId=id_a); assert a.until('ServerError')['Reason']=='AlreadyJoined'
    e.send('JoinMatch',MatchId='missing'); assert e.until('ServerError')['Reason']=='MatchNotFound'
    e.send('JoinMatch',MatchId=id_b,SessionToken=a.token); assert e.until('ServerError')['Reason']=='InvalidSession'
    e.send('JoinMatch',MatchId=id_a,SessionToken=pre_token); assert e.until('ServerError')['Reason']=='InvalidSession'
    e.send('JoinMatch',MatchId=id_a,SessionToken=a.token); assert e.until('ServerError')['Reason']=='SessionConnected'
    reject(a,'NotInMatch',MatchId=id_b,Command={'Type':'EndTurn'})
    reject(a,'PlayerMismatch',PlayerId=b.player,Command={'Type':'EndTurn'})
    reject(a,'StaleSequence',SequenceNumber=0,Command={'Type':'EndTurn'})
    for internal in ('Transform','TransformCommand','ChangeLife','ChangeLifeCommand','Draw','StartTurn'):
        reject(a,'CommandNotAllowed',Command={'Type':internal})
    for raw in ('{','{}','[]','null','{"Type":"x","Type":"y"}', '['*40+'0'+']'*40,
                '{"Type":"ListDecks","ProtocolVersion":true,"RequestId":"x"}'):
        e.raw(raw); assert e.until('ServerError')['Reason']=='InvalidRequest'
    # Match B has independent triggered card effects; A must receive no B events.
    c_card=c.state['Players'][0]['Hand'][0]['Id']
    before_a=json.dumps(a.state,sort_keys=True)
    command(c,'PlayCard',CardId=c_card)
    assert c.state['Players'][0]['Field'][0]['Attack']==3
    assert any(event['AttackAfter']==3 for event in c.events[-1]['Payload'])
    assert json.dumps(a.state,sort_keys=True)==before_a
    quiet(a); quiet(b)
    # A cannot address B's globally unique card IDs even with a valid A envelope.
    a.send('SubmitCommand',MatchId=id_a,GameNumber=a.game,SequenceNumber=a.seq,Command={'Type':'PlayCard','CardId':c_card})
    assert a.until('CommandRejected')['Reason'].startswith('GameRule:CardNotFound')
    # D misses an entire automatic turn transition, then recovers from a private snapshot.
    d_token=d.token; d_player=d.player; d.close(); time.sleep(.1)
    c.until('LobbyState')
    command(c,'EndTurn'); assert c.state['ActivePlayerId']==d_player
    d=Peer(port); group_b[1]=d
    d.send('JoinMatch',MatchId=id_b,SessionToken=d_token); assert d.until('MatchJoined')['PlayerId']==d_player
    d.until('StateSnapshot'); d.counter=200
    assert public(c.state)==public(d.state) and d.state['Players'][0]['Hand']==[]
    command(d,'EndTurn')
    # Match A completes combat and direct attack without changing B.
    before_b=json.dumps(c.state,sort_keys=True); sequence_b=c.seq
    a_card=a.state['Players'][0]['Hand'][0]['Id']; b_card=b.state['Players'][1]['Hand'][0]['Id']
    request=command(a,'PlayCard',CardId=a_card)
    assert b.events[-1]['Payload'][0]['TargetCardId']==0 and b.events[-1]['Payload'][0]['DefinitionId']==0
    a.raw(json.dumps(request)); assert a.until('CommandAccepted')['SequenceNumber']==2; a.until('StateSnapshot')
    conflict=json.loads(json.dumps(request)); conflict['Command']['CardId']=999
    a.raw(json.dumps(conflict)); assert a.until('CommandRejected')['Reason']=='RequestIdConflict'
    # Pipelined same-sequence commands serialize: the second must be stale.
    old=a.seq
    a.send('SubmitCommand',MatchId=id_a,GameNumber=1,SequenceNumber=old,Command={'Type':'AdvancePhase'})
    a.send('SubmitCommand',MatchId=id_a,GameNumber=1,SequenceNumber=old,Command={'Type':'AdvancePhase'})
    a.until('CommandAccepted'); a.until('StateSnapshot'); b.until('StateSnapshot')
    assert a.until('CommandRejected')['Reason']=='StaleSequence' and a.state['Phase']==1
    command(a,'EndTurn'); command(b,'PlayCard',CardId=b_card); command(b,'EndTurn')
    command(a,'AdvancePhase'); command(a,'Attack',CardId=a_card,TargetCardId=b_card)
    assert a.state['Players'][0]['Field'][0]['Hp']==2 and len(a.state['Players'][1]['Graveyard'])==1
    command(a,'EndTurn'); command(b,'EndTurn'); command(a,'AdvancePhase')
    finished_token=a.token
    command(a,'DirectAttack',CardId=a_card)
    assert a.state['Finished'] and a.state['WinnerPlayerId']==a.player
    assert a.until('MatchEnded')['WinnerPlayerId']==b.until('MatchEnded')['WinnerPlayerId']==a.player
    a.until('LobbyState');b.until('LobbyState')
    assert c.seq==sequence_b and json.dumps(c.state,sort_keys=True)==before_b
    quiet(c);quiet(d)
    e.send('JoinMatch',MatchId=id_a,SessionToken=finished_token);assert e.until('ServerError')['Reason']=='InvalidSession'
    reject(a,'MatchNotPlaying',Command={'Type':'EndTurn'})
    # Both players consent to rematch. New game, IDs, flags, sequence, tokens and request cache.
    for p,deck in ((a,'Wardens'),(b,'Strikers')):
        p.send('Rematch',MatchId=id_a,GameNumber=1,DeckId=deck);p.until('LobbyAccepted')
    for p in (a,b):p.until('StateSnapshot');assert p.game==2 and p.seq==1 and not p.state['Finished']
    for player in a.state['Players']:
        assert player['Life']==3 and not player['Field'] and not player['Graveyard']
    new_card=a.state['Players'][0]['Hand'][0]['Id']; assert new_card not in (a_card,b_card,c_card)
    assert a.state['Players'][0]['Hand'][0]['Attack']==2  # no previous +1 trigger/stat leakage
    reject(a,'StaleGame',GameNumber=1,Command={'Type':'PlayCard','CardId':a_card})
    e.send('JoinMatch',MatchId=id_a,SessionToken=finished_token);assert e.until('ServerError')['Reason']=='InvalidSession'
    # Old RequestId may be used in a new game only when the epoch and current card are explicit.
    request.update(GameNumber=2,SequenceNumber=1,Command={'Type':'PlayCard','CardId':new_card})
    a.raw(json.dumps(request));a.until('CommandAccepted');a.until('StateSnapshot');b.until('StateSnapshot')
    assert a.state['Players'][0]['Field'][0]['Attack']==3
    # Explicit close detaches both clients; they can create and join a new match on the same server.
    a.send('CloseMatch',MatchId=id_a,GameNumber=2);a.until('MatchClosed');b.until('MatchClosed')
    b.send('StateResyncRequest',MatchId=id_a);assert b.until('ServerError')['Reason']=='MatchNotFound'
    a.send('CreateMatch');a.until('MatchCreated');new_match=a.match;a.until('StateSnapshot');assert new_match not in (id_a,id_b)
    b.send('JoinMatch',MatchId=new_match);b.until('MatchJoined');b.until('StateSnapshot')
    b.send('LeaveMatch',MatchId=new_match,GameNumber=0);b.until('MatchLeft');a.until('LobbyState')
    a.send('CloseMatch',MatchId=new_match,GameNumber=0);a.until('MatchClosed')
    c.send('LeaveMatch',MatchId=id_b,GameNumber=1);c.until('MatchClosed');d.until('MatchClosed')
    if check_expiry:
        e.send('CreateMatch');e.until('MatchCreated');expired=e.match;e.until('StateSnapshot')
        assert e.until('MatchClosed')['Reason']=='Expired'
        e.send('StateResyncRequest',MatchId=expired);assert e.until('ServerError')['Reason']=='MatchNotFound'
    e.raw('x'*16385)
    try:e.read();raise AssertionError('oversize message accepted')
    except (EOFError,ConnectionError):pass
    for p in (a,b,c,d,e):p.close()
    print('Real WebSocket: four players, two isolated matches, decks/effects, auto turns, reconnect, victory, rematch, lifecycle and validation passed')
    return tokens

if __name__=='__main__':
    with socket.socket() as probe:
        probe.bind(('127.0.0.1',0));port=probe.getsockname()[1]
    server=subprocess.Popen([sys.argv[1],'--port',str(port),'--max-matches','2','--waiting-timeout','2','--data',os.path.join(os.path.dirname(__file__),'LegacyData')],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    tokens=[]
    try:
        line=server.stdout.readline();assert 'listening' in line,line
        tokens=run(port,check_limits=True,check_expiry=True)
    finally:
        server.terminate()
        try:server.wait(timeout=5)
        except subprocess.TimeoutExpired:server.kill();server.wait();raise AssertionError('server shutdown timed out')
        logs=server.stdout.read()
        assert all(token not in logs for token in tokens)
        assert 'Test Striker' not in logs and 'Test Warden' not in logs
    assert logs.count('replayVerified=true')>=3,logs
    if os.name!='nt':assert server.returncode==0,(server.returncode,logs)
