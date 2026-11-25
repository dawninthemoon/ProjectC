"""Opt-in Yakou prototype: real two-client RFC6455 transport integration."""
import os
import json
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

from WebSocketIntegration import Peer


def expect(peer, kind):
    return peer.until(kind)


def command(peer, match, request_id, payload):
    peer.raw(__import__("json").dumps({
        "ProtocolVersion": 2, "RequestId": request_id,
        "Type": "YakouCommand", "MatchId": match, "Command": payload
    }))
    return expect(peer, "YakouCommandResult")


def run(port):
    a, b, stranger = [Peer(port) for _ in range(3)]
    tokens = []
    try:
        a.send("ListDecks")
        assert expect(a, "DeckCatalog")["Payload"]  # existing game not removed
        a.send("YakouCreate")
        created = expect(a, "YakouCreated")
        match = created["MatchId"]
        token_a = created["Payload"]["ResumeToken"]
        tokens.append(token_a)
        state_a = expect(a, "YakouSnapshot")["Payload"]["State"]
        assert state_a["Players"][0]["Hand"] and not state_a["Players"][1]["Hand"]
        stranger.send("YakouCommand", MatchId=match, Command={"Type": "StartTurn"})
        assert expect(stranger, "YakouRejected")["Payload"]["Reason"] == "NotJoined"
        b.send("YakouJoin", MatchId=match)
        joined = expect(b, "YakouJoined")
        token_b = joined["Payload"]["ResumeToken"]
        tokens.append(token_b)
        assert joined["Payload"]["Seat"] == 2
        sa = expect(a, "YakouSnapshot")["Payload"]["State"]
        sb = expect(b, "YakouSnapshot")["Payload"]["State"]
        assert len(sa["Players"][0]["Hand"]) == 5
        assert sa["Players"][1]["Hand"] == []
        assert sb["Players"][0]["Hand"] == []
        assert len(sb["Players"][1]["Hand"]) == 5

        first_id = sa["Players"][0]["Hand"][0]["Id"]
        wrong = command(b, match, "wrong", {"Type": "Summon", "CardId": first_id,
                                              "Method": "Normal", "Materials": [], "Slot": 0})
        assert wrong["Payload"]["Accepted"] is False and wrong["Payload"]["Sequence"] == 0
        started = command(a, match, "turn", {"Type": "StartTurn"})
        assert started["Payload"]["Accepted"] is True and started["Payload"]["Sequence"] == 1
        assert expect(a, "YakouSnapshot")["Payload"]["Sequence"] == 1
        assert expect(b, "YakouSnapshot")["Payload"]["Sequence"] == 1
        summoned = command(a, match, "summon", {"Type": "Summon", "CardId": first_id,
                                                 "Method": "Normal", "Materials": [], "Slot": 0})
        assert summoned["Payload"]["Accepted"] is True and summoned["Payload"]["Sequence"] == 2
        after_a = expect(a, "YakouSnapshot")["Payload"]["State"]
        after_b = expect(b, "YakouSnapshot")["Payload"]["State"]
        assert after_a["Players"][0]["FieldSlots"][0]["Id"] == first_id
        assert after_a["Players"][0]["Graveyard"]
        assert after_b["Players"][0]["Hand"] == []
        assert after_a["Players"][1]["Hand"] == []
        assert after_b["Players"][0]["HandCount"] == after_a["Players"][0]["HandCount"]

        # Retrying after original success is an idempotent no-op, not a
        # second broadcast or a second summon.
        again = command(a, match, "summon", {"Type": "Summon", "CardId": first_id,
                                              "Method": "Normal", "Materials": [], "Slot": 0})
        assert again["Payload"] == summoned["Payload"]
        conflict = command(a, match, "summon", {"Type": "BeginRaid"})
        assert conflict["Payload"]["Reason"] == "DuplicateRequestId"
        a.close()
        # A stolen/malformed resume token must not bind another seat.
        stranger.send("YakouResume", MatchId=match, ResumeToken="bad-token")
        assert expect(stranger, "YakouRejected")["Payload"]["Reason"] == "InvalidResumeToken"
        resume_request = {"Type": "YakouResume", "ProtocolVersion": 2,
                          "RequestId": os.urandom(16).hex(), "MatchId": match, "ResumeToken": token_a}
        stranger.raw(__import__("json").dumps(resume_request))
        resumed = expect(stranger, "YakouResumed")
        token_new = resumed["Payload"]["ResumeToken"]
        tokens.append(token_new)
        assert token_new != token_a
        state = expect(stranger, "YakouSnapshot")["Payload"]["State"]
        assert state["Players"][1]["Hand"] == []
        assert state["Players"][0]["FieldSlots"][0]["Id"] == first_id
        assert expect(b, "YakouSnapshot")["Payload"]["Sequence"] == 2
        # Rotated resume token must no longer work.
        c = Peer(port)
        try:
            c.send("YakouResume", MatchId=match, ResumeToken=token_a)
            assert expect(c, "YakouRejected")["Payload"]["Reason"] == "InvalidResumeToken"
        finally:
            c.close()
        # Simulate a lost resume response: the exact previous request may be
        # retried and returns the same rotated token, not a second rotation.
        c = Peer(port)
        try:
            c.raw(__import__("json").dumps(resume_request))
            retry = expect(c, "YakouResumed")
            assert retry["Payload"]["ResumeToken"] == token_new
            expect(c, "YakouSnapshot")
            expect(stranger, "YakouReplaced")
            c.send("YakouSnapshot", MatchId=match)
            expect(c, "YakouSnapshot")
            # Once the rotated credential is acknowledged, even the exact
            # previous request cannot rebind the seat.
            d = Peer(port)
            try:
                d.raw(__import__("json").dumps(resume_request))
                assert expect(d, "YakouRejected")["Payload"]["Reason"] == "InvalidResumeToken"
            finally:
                d.close()
            stranger.close()
            stranger = c
        except Exception:
            c.close()
            raise
        duplicate = command(stranger, match, "summon", {"Type": "Summon", "CardId": first_id,
                                                         "Method": "Normal", "Materials": [], "Slot": 0})
        assert duplicate["Payload"] == summoned["Payload"]
        snapshot = command(stranger, match, "raid", {"Type": "BeginRaid"})
        assert snapshot["Payload"]["Accepted"] is True and snapshot["Payload"]["Sequence"] == 3
        expect(stranger, "YakouSnapshot")
        expect(b, "YakouSnapshot")
        b.send("YakouLeave", MatchId=match)
        assert expect(b, "YakouClosed")["Payload"]["Reason"] == "PlayerLeft"
        assert expect(stranger, "YakouClosed")["Payload"]["Reason"] == "PlayerLeft"
        stranger.send("YakouSnapshot", MatchId=match)
        assert expect(stranger, "YakouRejected")["Payload"]["Reason"] == "NotJoined"
        print("Real Yakou websocket: 2 seats, private snapshots, atomic commands, retries, reconnect, token rotation and lifecycle passed")
        return tokens
    finally:
        for peer in (a, b, stranger):
            peer.close()


def run_deck_selection(port):
    source = Path(__file__).resolve().parents[1] / "Data" / "YakouPrototype" / "Decks.json"
    decks = {deck["Id"]: deck["Cards"] for deck in json.loads(source.read_text(encoding="utf-8"))["Decks"]}
    grave, raid, guard = "YakouAlphaGrave", "YakouAlphaRaid", "YakouAlphaGuard"
    combinations = [(grave, raid), (grave, guard), (raid, guard),
                    (grave, grave), (raid, raid), (guard, guard)]
    for first, second in combinations:
        a, b = Peer(port), Peer(port)
        try:
            a.send("YakouCreate", DeckId="unknown-deck")
            assert expect(a, "YakouRejected")["Payload"]["Reason"] == "InvalidDeckId"
            a.send("YakouCreate", DeckId=first)
            created = expect(a, "YakouCreated")
            match = created["MatchId"]
            assert created["Payload"]["DeckId"] == first
            initial = expect(a, "YakouSnapshot")["Payload"]
            assert initial["DeckId"] == first
            b.send("YakouJoin", MatchId=match, DeckId="unknown-deck")
            assert expect(b, "YakouRejected")["Payload"]["Reason"] == "InvalidDeckId"
            b.send("YakouJoin", MatchId=match, DeckId=second)
            joined = expect(b, "YakouJoined")
            assert joined["Payload"]["DeckId"] == second
            left = expect(a, "YakouSnapshot")["Payload"]
            right = expect(b, "YakouSnapshot")["Payload"]
            assert left["DeckId"] == first and right["DeckId"] == second
            assert [card["DefinitionId"] for card in left["State"]["Players"][0]["Hand"]] == decks[first][:5]
            assert [card["DefinitionId"] for card in right["State"]["Players"][1]["Hand"]] == decks[second][:5]
            assert left["State"]["Players"][1]["Hand"] == []
            assert right["State"]["Players"][0]["Hand"] == []
            assert "DeckId" not in left["State"]["Players"][1]
            assert "DeckId" not in right["State"]["Players"][0]
            state = left["State"]
            summoned = False
            guard_summoned = False
            guard_blocked = False
            guard_combat_checked = False
            grave_mill_checked = False
            raid_bonus_checked = False
            for step in range(256):
                if state["Phase"] == "Finished":
                    break
                phase = state["Phase"]
                active = state["ActivePlayerId"]
                if phase == "TurnStart":
                    seat, payload = active, {"Type": "StartTurn"}
                elif phase == "Main" and active == 1 and not summoned:
                    chosen_definition = 3004 if first == guard else decks[first][0]
                    card = next(card for card in state["Players"][0]["Hand"]
                                if card["DefinitionId"] == chosen_definition)
                    seat = 1
                    payload = {"Type": "Summon", "CardId": card["Id"],
                               "Method": "Normal", "Materials": [], "Slot": 0}
                    summoned = True
                elif (phase == "Main" and active == 2 and first == grave and
                      second == guard and not guard_summoned):
                    card = next(card for card in right["State"]["Players"][1]["Hand"]
                                if card["DefinitionId"] == 3004)
                    seat = 2
                    payload = {"Type": "Summon", "CardId": card["Id"],
                               "Method": "Normal", "Materials": [], "Slot": 0}
                    guard_summoned = True
                elif phase == "Main":
                    seat, payload = active, {"Type": "BeginRaid"}
                elif phase == "AssignBlockers":
                    blockers = []
                    if (first == grave and second == guard and active == 1 and
                          not guard_blocked and state["Attacks"] and
                          state["Players"][1]["FieldSlots"][0]):
                        blockers = [{"Attacker": state["Attacks"][0]["AttackerId"],
                                     "Blocker": state["Players"][1]["FieldSlots"][0]["Id"]}]
                        guard_blocked = True
                    seat, payload = 3 - active, {"Type": "AssignBlockers", "Blockers": blockers}
                elif phase == "RaidResponse":
                    priority = state["PriorityPlayerId"]
                    seat = priority if priority else 3 - active
                    payload = {"Type": "PassRaidResponse" if priority else "ResolveRaid"}
                else:
                    raise AssertionError(f"Unexpected phase during Alpha duel: {phase}")
                defender_life_before = state["Players"][1]["Life"]
                unblocked_raid = (payload["Type"] == "ResolveRaid" and
                                  first == raid and active == 1 and
                                  any(attack["BlockerId"] == 0 for attack in state["Attacks"]))
                acting = a if seat == 1 else b
                result = command(acting, match, f"deck-game-{step}", payload)
                assert result["Payload"]["Accepted"] is True
                left = expect(a, "YakouSnapshot")["Payload"]
                right = expect(b, "YakouSnapshot")["Payload"]
                assert left["Sequence"] == right["Sequence"]
                assert left["State"]["Players"][1]["Hand"] == []
                assert right["State"]["Players"][0]["Hand"] == []
                state = left["State"]
                if unblocked_raid and not raid_bonus_checked:
                    assert defender_life_before - state["Players"][1]["Life"] == 4
                    raid_bonus_checked = True  # 3003: printed attack 3 + OnRaid 1.
                if payload["Type"] == "Summon" and first == grave and seat == 1:
                    # 2001's authored OnSummoned effect mills two cards.
                    assert len(state["Players"][0]["Graveyard"]) == 2
                    grave_mill_checked = True
                if (payload["Type"] == "ResolveRaid" and guard_blocked and
                      not guard_combat_checked and active == 1):
                    blocker = state["Players"][1]["FieldSlots"][0]
                    assert blocker and blocker["DefinitionId"] == 3004
                    assert blocker["Hp"] == 5  # 3004 reduces the 1-point raid hit.
                    guard_combat_checked = True
            assert summoned and state["Phase"] == "Finished" and state["WinnerPlayerId"] in (1, 2)
            if first == grave:
                assert grave_mill_checked
            if first == grave and second == guard:
                assert guard_summoned and guard_combat_checked
            if first == raid:
                assert raid_bonus_checked
            b.send("YakouLeave", MatchId=match)
            expect(a, "YakouClosed")
            expect(b, "YakouClosed")
        finally:
            a.close()
            b.close()
    print("Real Yakou websocket: six Alpha 01 deck pairings, private snapshots and summoned matches to victory passed")


if __name__ == "__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        proc = subprocess.Popen(
            [sys.argv[1], "--port", str(port), "--yakou-prototype", "on",
             "--max-matches", "2"],
            stdout=log, stderr=subprocess.STDOUT, text=True)
        tokens = []
        try:
            deadline = time.monotonic() + 10
            line = ""
            while time.monotonic() < deadline:
                log.seek(0)
                line = log.readline()
                if "listening" in line or proc.poll() is not None:
                    break
                time.sleep(.05)
            assert "listening" in line, line
            tokens = run(port)
            run_deck_selection(port)
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
                raise AssertionError("Yakou server shutdown hung")
            log.seek(0)
            logs = log.read()
            assert all(token not in logs for token in tokens), "Secret token in logs"
        if os.name != "nt":
            assert proc.returncode == 0, (proc.returncode, logs)
