"""Opt-in Yakou prototype: real two-client RFC6455 transport integration."""
import os
import socket
import subprocess
import sys

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


if __name__ == "__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    proc = subprocess.Popen(
        [sys.argv[1], "--port", str(port), "--yakou-prototype", "on",
         "--max-matches", "2"],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    tokens = []
    try:
        line = proc.stdout.readline()
        assert "listening" in line, line
        tokens = run(port)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
            raise AssertionError("Yakou server shutdown hung")
        logs = proc.stdout.read()
        assert all(token not in logs for token in tokens), "Secret token in logs"
    if os.name != "nt":
        assert proc.returncode == 0, (proc.returncode, logs)
