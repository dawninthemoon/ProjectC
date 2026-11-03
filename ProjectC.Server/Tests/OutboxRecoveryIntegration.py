"""Loopback-only process/ephemeral-filesystem replacement and API outage tests.

Fresh directories model a replacement Render container; a retained directory
models only a process restart (or a correctly mounted persistent disk).
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
from WebsiteBattleIntegration import free_port


def wait_until(predicate, timeout=12):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(.05)
    raise AssertionError('Outbox condition did not become true')


def run(executable):
    key = uuid.uuid4().hex + uuid.uuid4().hex
    ledger, calls = {}, []
    mode = 'offline'
    lock = threading.Lock()

    class Api(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            assert self.path == '/api/internal/battle/matches'
            assert self.headers.get('X-Battle-Api-Key') == key
            body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
            with lock:
                calls.append(body)
                status = 503
                response = {}
                if mode != 'offline':
                    match = body['matchId']
                    duplicate = match in ledger
                    if duplicate:
                        assert ledger[match] == body, 'Result changed across restart'
                    else:
                        ledger[match] = body
                    response = dict(matchId=match, duplicate=duplicate, rankChanges=[])
                    status = 200 if mode == 'success' else 503
            output = json.dumps(response).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(output)))
            self.end_headers()
            self.wfile.write(output)

    api = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Api)
    threading.Thread(target=api.serve_forever, daemon=True).start()
    data = Path(executable).parent / 'Data' / 'HyakkiYagyo'
    running = []
    logs = []

    def start(root, storage='ephemeral', outbox='queue'):
        port = free_port()
        env = dict(os.environ, WEBSITE_API_URL=f'http://127.0.0.1:{api.server_port}',
                   BATTLE_API_KEY=key, BATTLE_RESULT_STORAGE=storage)
        if outbox is None:
            env.pop('BATTLE_RESULT_OUTBOX', None)
        else:
            env['BATTLE_RESULT_OUTBOX'] = outbox
        log = tempfile.TemporaryFile(mode='w+', encoding='utf-8')
        logs.append(log)
        process = subprocess.Popen([executable, '--port', str(port), '--data', str(data)],
                                   cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        running.append(process)
        if storage not in ('ephemeral', 'persistent'):
            assert process.wait(timeout=5) != 0
            return process
        connected = []

        def ready():
            assert process.poll() is None, 'Server failed to start'
            try:
                connected.append(Peer(port))
                return True
            except (ConnectionError, socket.timeout):
                return False

        wait_until(ready)
        peer = connected[0]
        peer.send('ListDecks')
        assert peer.until('DeckCatalog')['PersonalDeckSupported']
        peer.close()
        return process

    def stop(process):
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)

    def seed(root):
        body = dict(matchId=str(uuid.uuid4()), participants=[dict(playerId=str(uuid.uuid4()),
                    deckId=str(uuid.uuid4()), snapshotId=str(uuid.uuid4())) for _ in range(2)],
                    winnerPlayerId=None, endReason='Aborted', endedAt='2026-01-01T00:00:00Z')
        path = root / 'queue' / (body['matchId'] + '.json')
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(dict(body=body, attempts=0, nextAttempt=0)), encoding='utf-8')
        return body, path

    def failed(path):
        try:
            return json.loads(path.read_text(encoding='utf-8')).get('lastStatus') == 503
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return False

    try:
        with tempfile.TemporaryDirectory(prefix='projectc-outbox-restart-') as temporary:
            root = Path(temporary)
            # Default configuration also works without a persistent mount.
            stop(start(root, outbox=None))
            assert (root / 'BattleOutbox').is_dir()
            start(root, storage='invalid')

            # Same filesystem: failed result survives a process restart with the exact body.
            body, path = seed(root)
            process = start(root)
            wait_until(lambda: failed(path))
            assert body['matchId'] not in ledger
            stop(process)
            mode = 'success'
            process = start(root, storage='persistent')
            wait_until(lambda: not path.exists())
            assert ledger[body['matchId']] == body
            assert len([call for call in calls if call['matchId'] == body['matchId']]) >= 2
            stop(process)

        for committed_before_restart in (False, True):
            with tempfile.TemporaryDirectory(prefix='projectc-outbox-old-') as old_temporary, \
                 tempfile.TemporaryDirectory(prefix='projectc-outbox-new-') as new_temporary:
                old_root, new_root = Path(old_temporary), Path(new_temporary)
                mode = 'commit_then_503' if committed_before_restart else 'offline'
                body, path = seed(old_root)
                process = start(old_root)
                wait_until(lambda: failed(path))
                stop(process)
                assert (body['matchId'] in ledger) == committed_before_restart
                before = len(calls)
                mode = 'success'
                replacement = start(new_root)
                time.sleep(1.5)
                # A replacement container cannot rediscover a job whose local disk vanished.
                assert len(calls) == before
                assert list((new_root / 'queue').glob('*.json')) == []
                assert (body['matchId'] in ledger) == committed_before_restart
                stop(replacement)

                # If the original immutable body is available, duplicate delivery is harmless.
                if committed_before_restart:
                    replay = new_root / 'queue' / path.name
                    replay.write_text(json.dumps(dict(body=body, attempts=0, nextAttempt=0)), encoding='utf-8')
                    restored = start(new_root)
                    wait_until(lambda: not replay.exists())
                    assert ledger[body['matchId']] == body
                    assert len([match for match in ledger if match == body['matchId']]) == 1
                    stop(restored)

        print('Outbox recovery: writable ephemeral/default paths, invalid storage, retained-disk restart, API outage, replacement-container loss, committed result survival and immutable duplicate retry passed')
    finally:
        for process in running:
            if process.poll() is None:
                stop(process)
        for log in logs:
            log.seek(0)
            assert key not in log.read()
            log.close()
        api.shutdown()
        api.server_close()


if __name__ == '__main__':
    run(str(Path(sys.argv[1]).resolve()))
