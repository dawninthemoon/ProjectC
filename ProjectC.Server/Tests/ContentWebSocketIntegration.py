"""Real WebSocket tests for experimental content and private action choices."""
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from WebSocketIntegration import Peer

def submit(peer, command):
    return peer.send('SubmitCommand', MatchId=peer.match, GameNumber=peer.game,
                     SequenceNumber=peer.seq, Command=command)

def run(server_path):
    with tempfile.TemporaryDirectory(prefix='projectc-content-') as data:
        shutil.copyfile(os.path.join(os.path.dirname(server_path),'Data','HyakkiYagyo','Cards.json'),os.path.join(data,'Cards.json'))
        decks={"Config":{"InitialLife":20,"InitialHandSize":2,"DrawPerTurn":0,"FieldLimit":5,
               "HandLimit":10,"MinDeckSize":10,"MaxDeckSize":10,"MaxCardCopies":10,
               "InitialYokaiPower":2,"YokaiPowerPerTurn":1,"MaxYokaiPower":10,"SummoningSickness":False},
               "Decks":[{"Id":"Choice","Name":"Choice fixture","Cards":[1102]*10},
                        {"Id":"Scroll","Name":"Scroll fixture","Cards":[2003]*10}]}
        with open(os.path.join(data,'Decks.json'),'w',encoding='utf-8') as file:json.dump(decks,file)
        with socket.socket() as probe:
            probe.bind(('127.0.0.1',0));port=probe.getsockname()[1]
        server=subprocess.Popen([server_path,'--port',str(port),'--data',data],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        try:
            assert 'listening' in server.stdout.readline()
            a,b,c,d=[Peer(port) for _ in range(4)]
            for host,guest,deck in ((a,b,'Choice'),(c,d,'Scroll')):
                host.send('CreateMatch',DeckId=deck);host.until('MatchCreated');host.until('StateSnapshot')
                guest.send('JoinMatch',MatchId=host.match,DeckId=deck);guest.until('MatchJoined');guest.until('StateSnapshot')
                host.send('SetReady',MatchId=host.match,GameNumber=0,Ready=True);host.until('LobbyAccepted')
                guest.send('SetReady',MatchId=guest.match,GameNumber=0,Ready=True);guest.until('LobbyAccepted')
                host.until('StateSnapshot');guest.until('StateSnapshot')
                assert host.seq==guest.seq==1
            assert a.match!=c.match
            unrelated_a=json.dumps(a.state,sort_keys=True)
            spell=c.state['Players'][0]['Hand'][0]['Id']
            submit(c,{"Type":"PlayerAction","CardId":spell,"EffectId":1});c.until('CommandAccepted')
            c.until('StateSnapshot');d.until('StateSnapshot')
            assert c.state['Players'][0]['YokaiPower']==1
            assert c.state['Players'][0]['HiddenExileCount']==5
            assert 'HiddenExile' not in d.state['Players'][0]
            for peer in (c,d):
                for event in peer.events[-1]['Payload']:
                    if event['ToZone']==5: assert event['SourceCardId']==event['TargetCardId']==event['DefinitionId']==0
            assert json.dumps(a.state,sort_keys=True)==unrelated_a
            source=a.state['Players'][0]['Hand'][0]['Id'];other=a.state['Players'][0]['Hand'][1]['Id']
            request=submit(a,{"Type":"PlayerAction","CardId":source,"EffectId":1});a.until('CommandPending')
            a.until('StateSnapshot');choice=a.until('ActionChoiceRequired')['Payload'];b.until('StateSnapshot')
            assert a.seq==b.seq==1 and a.state['Players'][0]['HandCount']==2
            assert b.state['Players'][0]['Hand']==[] and b.state['ResolvingPlayerId']==a.player
            assert choice['CanCancel'] is False and len(choice['Candidates'])==2
            assert all(option['CardId']!=source for option in choice['Candidates'])
            submit(b,{"Type":"ResolveChoice","ChoiceId":choice['ChoiceId'],"Choices":[choice['Candidates'][0]['Token']]})
            assert b.until('CommandRejected')['Reason']=='InvalidChoiceSession'
            b.until('StateSnapshot')
            submit(a,{"Type":"CancelAction","ChoiceId":choice['ChoiceId']})
            assert a.until('CommandRejected')['Reason']=='ChoiceMustResolve'
            a.until('StateSnapshot');a.until('ActionChoiceRequired')
            token=a.token;a.close();time.sleep(.1);b.until('LobbyState')
            a=Peer(port);a.send('JoinMatch',MatchId=b.match,SessionToken=token)
            a.until('MatchJoined');a.until('StateSnapshot');assert a.until('ActionChoiceRequired')['Payload']['ChoiceId']==choice['ChoiceId']
            selected=next(option['Token'] for option in choice['Candidates'] if option['CardId']!=other)
            answer=submit(a,{"Type":"ResolveChoice","ChoiceId":choice['ChoiceId'],"Choices":[selected]})
            accepted=a.until('CommandAccepted');assert accepted['RequestId']==answer['RequestId'] and accepted['SequenceNumber']==2
            a.until('CommandAccepted');a.until('StateSnapshot');b.until('StateSnapshot')
            assert a.state['Players'][0]['Graveyard'][0]['DefinitionId']==1102
            assert len(a.state['Players'][0]['Graveyard'])==2 and a.state['Players'][0]['HandCount']==1
            assert b.state['Players'][0]['Hand']==[] and b.state['ResolvingPlayerId']==0
            assert c.seq==d.seq==2 and a.seq==b.seq==2
            for host in (a,c):
                host.send('CloseMatch',MatchId=host.match,GameNumber=host.game);host.until('MatchClosed')
            for peer in (a,b,c,d):peer.close()
        finally:
            server.terminate()
            try:server.wait(timeout=5)
            except subprocess.TimeoutExpired:server.kill();server.wait();raise
            logs=server.stdout.read()
            assert logs.count('replayVerified=true')>=2,logs
    print('Real WebSocket content: two matches, hidden exile, atomic private choice, reconnect, replay passed')

if __name__=='__main__':run(sys.argv[1])
