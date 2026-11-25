"""Alpha02: actual server, shuffled sample decks, two authenticated WebSocket seats."""
import collections
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

from WebSocketIntegration import Peer
from YakouWebSocketIntegration import command, expect

CATALOG = json.loads((Path(__file__).resolve().parents[1] / "Data" /
                      "Alpha02" / "AuthoringSnapshot.json").read_text(encoding="utf-8"))
DECKS = {d["Id"]: d["Cards"] for d in CATALOG["Decks"]["Decks"]}
VERSION = CATALOG["Cards"]["CatalogVersion"]
ONMYO, BLOOD = "Alpha02Onmyoryo", "Alpha02BloodOni"
RESOURCE_BY_DECK = {ONMYO: "Spirit", BLOOD: "Blood"}


def duel(port, first, second, reconnect=False):
    peers = [Peer(port), Peer(port)]
    resume_tokens = []
    seen = set()
    try:
        a, b = peers
        a.send("YakouCreate", DeckId=first)
        created = expect(a, "YakouCreated")
        room = created["MatchId"]
        token = created["Payload"]["ResumeToken"]
        resume_tokens.append(token)
        opening = expect(a, "YakouSnapshot")["Payload"]["State"]["Players"][0]["Hand"]
        b.send("YakouJoin", MatchId=room, DeckId=second)
        joined = expect(b, "YakouJoined")
        resume_tokens.append(joined["Payload"]["ResumeToken"])
        states = [expect(p, "YakouSnapshot")["Payload"] for p in peers]
        assert states[0]["State"]["Players"][0]["Hand"] == opening, "joining changed seat one's shuffled hand"

        def verify():
            left, right = (item["State"] for item in states)
            assert states[0]["Sequence"] == states[1]["Sequence"]
            for index, view in enumerate((left, right)):
                assert view["CatalogVersion"] == VERSION
                assert not view["Players"][1-index]["Hand"], "opponent hand leaked"
                assert "SummonChoices" not in view["Players"][1-index]
                assert "MainSpellOptions" not in view["Players"][1-index]
                assert "RandomSeed" not in view and "RandomDraws" not in view
                assert view["Players"][index]["Resources"].keys() == {RESOURCE_BY_DECK[(first, second)[index]]}
                for p in view["Players"]:
                    for balance in p["Resources"].values():
                        assert 0 <= balance["Current"] <= balance["Max"]
            for key in ("Phase", "TurnNumber", "ActivePlayerId", "PriorityPlayerId", "WinnerPlayerId", "PendingEffectCount"):
                assert left[key] == right[key], (key, left[key], right[key])
            for i in range(2):
                for key in ("Life", "Resources", "FieldSlots", "DeckCount", "HandCount"):
                    assert left["Players"][i][key] == right["Players"][i][key]

        verify()
        for index, deck in enumerate((first, second)):
            hand = states[index]["State"]["Players"][index]["Hand"]
            assert len(hand) == 5
            assert not (collections.Counter(c["DefinitionId"] for c in hand) -
                        collections.Counter(DECKS[deck])), "invalid shuffled opening cards"

        # Opponent cannot start seat one's turn; failed command must not advance sequence.
        rejected = command(b, room, "wrong-seat", {"Type":"StartTurn"})
        assert rejected["Payload"]["Accepted"] is False
        assert rejected["Payload"]["Sequence"] == states[0]["Sequence"]
        seq = states[0]["Sequence"]

        for step in range(450):
            state = states[0]["State"]
            if state["Phase"] == "Finished":
                break
            if reconnect and step == 5:
                a.close()
                peers[0] = Peer(port)
                a = peers[0]
                a.raw(json.dumps({"Type":"YakouResume", "ProtocolVersion":2,
                    "RequestId":os.urandom(16).hex(), "MatchId":room,"ResumeToken":token}))
                resumed = expect(a,"YakouResumed")
                resume_tokens.append(resumed["Payload"]["ResumeToken"])
                assert resumed["Payload"]["ResumeToken"] != token
                states = [expect(p,"YakouSnapshot")["Payload"] for p in peers]
                assert states[0]["Sequence"] == seq
                verify()
                state = states[0]["State"]

            active, phase = state["ActivePlayerId"], state["Phase"]
            # Pending selection is private to its controller. Always resolve it
            # before starting another command; optional choices may be declined.
            pending = [(i+1,item["State"].get("PendingSelection"))
                       for i,item in enumerate(states) if item["State"].get("PendingSelection")]
            if pending and not state["PriorityPlayerId"]:
                assert len(pending)==1
                seat, choice = pending[0]
                assert choice["Status"]=="Ready", choice
                candidates = choice["Candidates"]
                assert choice["MinCount"] == 0 or candidates, choice
                target = candidates[0] if choice["MinCount"] > 0 else 0
                payload = {"Type":"ChooseEffect","TargetId":target}
                if choice.get("AvailableSlots") and target:
                    payload["Slot"] = choice["AvailableSlots"][0]
                if choice["Operation"] == "SearchDeck":
                    seen.add("SearchDeck")
            elif phase == "TurnStart":
                seat,payload=active,{"Type":"StartTurn"}
            elif phase == "Main":
                seat=active
                own=states[seat-1]["State"]["Players"][seat-1]
                hand={c["Id"]:c["DefinitionId"] for c in own["Hand"]}
                # Favor resource engines and then legal summons. This is a
                # smoke-test policy, NOT a balance/AI skill benchmark.
                normal=[c for c in own["SummonChoices"] if c["Method"]=="Normal"]
                normal.sort(key=lambda c: hand[c["CardId"]] not in (5101,5201,5207,5109))
                chosen=normal[0] if normal else None
                safe=[]
                for option in own.get("MainSpellOptions",[]):
                    definition=hand[option["CardId"]]
                    # Explicitly exercised here: free generators, self-wound,
                    # direct damage, healing and delay. Other cards have C++ fixtures.
                    if definition in (5111,5211,5212,5112,5115,5116,5213):
                        if definition in (5212,5115,5213) and not any(own["FieldSlots"]):
                            continue
                        enemy=state["Players"][2-seat]
                        if definition==5116 and not any(enemy["FieldSlots"]):
                            continue
                        safe.append(option)
                safe.sort(key=lambda o: hand[o["CardId"]] not in (5111,5211,5212))
                if chosen is None and safe:
                    option=safe[0]
                    definition=hand[option["CardId"]]
                    payload={"Type":"BeginSpellResponse","CardId":option["CardId"],
                             "Costs":option["CostCandidates"][:option["CostCount"]]}
                    if option.get("TargetCandidates"):
                        payload["TargetId"]=option["TargetCandidates"][0]
                    elif definition in (5212,5115,5213):
                        target=next(c for c in own["FieldSlots"] if c)
                        payload["TargetId"]=target["Id"]
                    elif definition==5116:
                        payload["TargetId"]=next(c["Id"] for c in enemy["FieldSlots"] if c)
                    seen.add(definition)
                else:
                    # Avoid endlessly devouring the field in the smoke policy.
                    options=[o for o in own["SummonChoices"] if o["Method"] in ("Resource","Discard")
                             and any(own["FieldSlots"][slot] is None for slot in o["AvailableSlots"])]
                    if chosen is None and options:
                        options.sort(key=lambda o:o["Method"]!="Resource")
                        chosen=options[0]
                    if chosen:
                        payload={"Type":"Summon","CardId":chosen["CardId"],"Method":chosen["Method"],
                                 "Materials":chosen["Candidates"][:chosen["RequiredCount"]],
                                 "Slot":chosen["AvailableSlots"][0]}
                        seen.add(hand[chosen["CardId"]])
                        if chosen.get("ResourceCosts"):
                            seen.add("ResourceSummon")
                    else:
                        payload={"Type":"BeginRaid"}
            elif phase=="SpellResponse":
                seat=state["PriorityPlayerId"]
                payload={"Type":"PassSpellResponse"}
            elif phase=="AssignBlockers":
                seat=3-active
                # At most one block for each match, then allow decisive damage.
                blockers=[]
                if "Block" not in seen:
                    blocker=next((c for c in state["Players"][seat-1]["FieldSlots"] if c),None)
                    if blocker and state["Attacks"]:
                        blockers=[{"Attacker":state["Attacks"][0]["AttackerId"],"Blocker":blocker["Id"]}]
                        seen.add("Block")
                payload={"Type":"AssignBlockers","Blockers":blockers}
            elif phase=="RaidResponse":
                priority=state["PriorityPlayerId"]
                seat=priority or 3-active
                payload={"Type":"PassRaidResponse" if priority else "ResolveRaid"}
            else:
                raise AssertionError(("Unexpected phase",phase))
            result=command(peers[seat-1],room,f"alpha02-{step}",payload)["Payload"]
            assert result["Accepted"], (first,second,step,seat,payload,result.get("Reason"))
            seq+=1
            assert result["Sequence"]==seq
            states=[expect(p,"YakouSnapshot")["Payload"] for p in peers]
            verify()

        final=states[0]["State"]
        assert final["Phase"]=="Finished" and final["WinnerPlayerId"] in (1,2), final["Phase"]
        assert any(isinstance(x,int) and 5101 <= x <= 5218 for x in seen)
        print(f"{first} vs {second}: winner={final['WinnerPlayerId']}, turns={final['TurnNumber']}, commands={seq}, exercised={sorted(map(str,seen))}")
        peers[1].send("YakouLeave",MatchId=room)
        for peer in peers:
            expect(peer,"YakouClosed")
        return resume_tokens
    finally:
        for peer in peers:
            peer.close()


if __name__=="__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1",0))
        port=probe.getsockname()[1]
    with tempfile.TemporaryFile(mode="w+t",encoding="utf-8") as log:
        proc=subprocess.Popen([sys.argv[1],"--port",str(port),"--yakou-prototype","on",
                               "--alpha02","on","--max-matches","2"],
                              stdout=log,stderr=subprocess.STDOUT,text=True)
        tokens=[]
        try:
            deadline=time.monotonic()+10
            while True:
                if proc.poll() is not None or time.monotonic()>=deadline:
                    raise AssertionError("Alpha02 server failed to start")
                try:
                    with socket.create_connection(("127.0.0.1",port),timeout=.2):
                        break
                except OSError:
                    time.sleep(.05)
            tokens.extend(duel(port,ONMYO,BLOOD,True))
            tokens.extend(duel(port,BLOOD,ONMYO))
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
                raise AssertionError("Alpha02 shutdown hung")
            log.seek(0)
            logs=log.read()
            assert all(t not in logs for t in tokens),"Resume token leaked to logs"
        if os.name!="nt":
            assert proc.returncode==0,(proc.returncode,logs)
