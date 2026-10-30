"""Graceful shutdown with four connected clients and two active games. Requires Docker CLI."""
import json
import subprocess
import sys
from WebSocketIntegration import Peer

def run(port,container):
    peers=[Peer(port) for _ in range(4)]
    tokens=[]
    try:
        for first,second in ((peers[0],peers[1]),(peers[2],peers[3])):
            first.send('CreateMatch');first.until('MatchCreated');first.until('StateSnapshot')
            second.send('JoinMatch',MatchId=first.match);second.until('MatchJoined');second.until('StateSnapshot')
            for peer in (first,second):
                peer.send('SetReady',MatchId=peer.match,GameNumber=0,Ready=True);peer.until('LobbyAccepted')
            for peer in (first,second):peer.until('StateSnapshot');tokens.append(peer.token)
        state=json.loads(subprocess.check_output(['docker','inspect',container],text=True))[0]['State']
        assert state['Running']
        subprocess.run(['docker','stop','-t','10',container],check=True,timeout=20,stdout=subprocess.DEVNULL)
        state=json.loads(subprocess.check_output(['docker','inspect',container],text=True))[0]['State']
        assert not state['Running'] and state['ExitCode']==0,state
        logs=subprocess.check_output(['docker','logs',container],stderr=subprocess.STDOUT,text=True)
        assert 'Server shutdown activeMatches=0' in logs and 'Clients=0 activeMatches=0' in logs
        assert logs.count('reason=ServerShutdown')>=2 and logs.count('replayVerified=true')>=2
        assert all(token not in logs for token in tokens)
        print('Docker graceful shutdown: four active clients, two playing matches, archived replays, zero active sessions and exit code 0 passed')
    finally:
        for peer in peers:peer.close()
if __name__=='__main__':run(int(sys.argv[1]),sys.argv[2])
