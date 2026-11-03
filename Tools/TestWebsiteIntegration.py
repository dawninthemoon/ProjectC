"""Full C++ -> NestJS -> disposable PostgreSQL 17 verification.

Usage: python Tools/TestWebsiteIntegration.py <battle-server-executable> <Website/backend>
Requires Docker, Node and the backend's installed dependencies. No .env is read
by this runner and every database URL passed to Nest/Prisma targets its own
fresh loopback-only container. The container is removed afterwards.
"""
import collections
import http.server
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'ProjectC.Server' / 'Tests'))
from WebSocketIntegration import Peer
from WebsiteBattleIntegration import finish_normal, free_port


def run(executable, backend):
    container = 'projectc-battle-test-' + uuid.uuid4().hex
    password, jwt_key, battle_key = [uuid.uuid4().hex + uuid.uuid4().hex for _ in range(3)]
    env = dict(os.environ, POSTGRES_PASSWORD=password)
    processes = []
    proxy = None

    def execute(arguments, workdir=None, environment=env):
        result = subprocess.run(arguments, cwd=workdir, env=environment, capture_output=True, text=True, encoding='utf-8', errors='replace')
        if result.returncode:
            output = result.stdout + result.stderr
            for secret in (password, jwt_key, battle_key):
                output = output.replace(secret, '[redacted]')
            raise RuntimeError(output[-12000:])
        return result.stdout.strip()

    def api(path, method='GET', body=None, token=None, internal=False):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        if internal:
            headers['X-Battle-Api-Key'] = battle_key
        request = urllib.request.Request(api_url + path, headers=headers, method=method,
                                         data=None if body is None else json.dumps(body).encode())
        with urllib.request.urlopen(request, timeout=30) as response:
            content = response.read()
            return json.loads(content) if content else None

    try:
        execute(['docker', 'run', '-d', '--rm', '--name', container, '--env', 'POSTGRES_PASSWORD',
                 '--env', 'POSTGRES_DB=projectc_test', '-p', '127.0.0.1::5432', 'postgres:17'])
        mapping = execute(['docker', 'port', container, '5432']).split(':')[-1]
        for _ in range(100):
            ready = subprocess.run(['docker', 'exec', container, 'pg_isready', '-U', 'postgres'], capture_output=True)
            if ready.returncode == 0:
                break
            time.sleep(.2)
        else:
            raise RuntimeError('Disposable PostgreSQL did not become ready')
        database = f'postgresql://postgres:{password}@127.0.0.1:{mapping}/projectc_test'
        api_port = free_port()
        api_url = f'http://127.0.0.1:{api_port}'
        env.update(DATABASE_URL=database, DIRECT_URL=database, PROJECTC_TEST_DATABASE=database,
                   JWT_SIGNING_KEY=jwt_key, BATTLE_API_KEY=battle_key, PORT=str(api_port),
                   CATALOG_PATH=str(backend / 'catalog' / 'cards.json'), GUEST_PERMIT_LIMIT='1000',
                   NODE_ENV='test')
        npm = shutil.which('npm.cmd' if os.name == 'nt' else 'npm')
        print('Running Website unit and PostgreSQL integration suites in an isolated database', flush=True)
        execute([npm, 'test'], backend)
        print('Website unit tests passed', flush=True)
        execute([npm, 'run', 'test:e2e'], backend)
        print('Website PostgreSQL integration tests passed', flush=True)
        execute([npm, 'run', 'build'], backend)
        with tempfile.TemporaryDirectory(prefix='projectc-db-battle-') as root:
            log = open(Path(root) / 'services.log', 'w+', encoding='utf-8')
            node = subprocess.Popen(['node', 'dist/main.js'], cwd=backend, env=env, stdout=log, stderr=subprocess.STDOUT)
            processes.append(node)
            for _ in range(100):
                try:
                    api('/health')
                    break
                except (urllib.error.URLError, ConnectionError):
                    time.sleep(.1)
            else:
                raise RuntimeError('NestJS did not start')
            first, second = [api('/api/auth/guest', 'POST', {'displayName': 'Battle integration'}) for _ in range(2)]
            first = api('/api/auth/refresh', 'POST', {'refreshToken': first['refreshToken']})
            assert api('/api/users/me', token=first['accessToken'])['playerId'] == first['user']['playerId']
            data = Path(executable).parent / 'Data' / 'HyakkiYagyo'
            definitions = json.loads((data / 'Decks.json').read_text(encoding='utf-8'))['Decks']
            saved = []
            for identity, deck in zip((first, second), definitions):
                counts = collections.Counter(deck['Cards'])
                saved.append(api('/api/decks', 'POST', dict(name='Integration deck', cards=[dict(cardId=card, quantity=count) for card, count in counts.items()]), identity['accessToken']))
            assert api('/api/decks', token=first['accessToken'])[0]['id'] == saved[0]['id']
            submitted = []
            responses = []

            class Proxy(http.server.BaseHTTPRequestHandler):
                def log_message(self, *_):
                    pass

                def do_POST(self):
                    body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                    assert self.headers['X-Battle-Api-Key'] == battle_key
                    response = api(self.path, 'POST', body, internal=True)
                    status = 200
                    if self.path.endswith('/matches'):
                        submitted.append(body)
                        responses.append(response)
                        # Commit really happened, but the Battle Server loses the first ack.
                        status = 503 if len(submitted) == 1 else 200
                    payload = json.dumps(response).encode()
                    self.send_response(status)
                    self.send_header('Content-Type', 'application/json')
                    self.send_header('Content-Length', str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload)

            proxy = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Proxy)
            threading.Thread(target=proxy.serve_forever, daemon=True).start()
            battle_port = free_port()
            battle_env = dict(env, WEBSITE_API_URL=f'http://127.0.0.1:{proxy.server_port}', BATTLE_RESULT_OUTBOX=root)
            battle = subprocess.Popen([executable, '--port', str(battle_port), '--mulligan', 'off', '--data', str(data)], env=battle_env, stdout=log, stderr=subprocess.STDOUT)
            processes.append(battle)
            for _ in range(100):
                try:
                    a = Peer(battle_port)
                    break
                except ConnectionRefusedError:
                    time.sleep(.1)
            else:
                raise RuntimeError('Battle Server did not start')
            b = Peer(battle_port)
            try:
                for client, identity, deck in zip((a, b), (first, second), saved):
                    client.socket.settimeout(15)
                    client.send('QueueMatch', AccessToken=identity['accessToken'], DeckId=deck['id'])
                    client.until('MatchmakingQueued')
                for client in (a, b):
                    client.until('MatchmakingFound')
                    client.until('StateSnapshot')
                    assert client.state['Started']
                finish_normal(a, b)
                a.until('MatchEnded')
                b.until('MatchEnded')
                changes = [client.until('RankUpdated')['Payload'] for client in (a, b)]
                assert submitted[0] == submitted[1]
                assert responses[0]['duplicate'] is False and responses[1]['duplicate'] is True
                assert responses[0]['rankChanges'] == responses[1]['rankChanges']
                ranks = [api('/api/rank/me', token=identity['accessToken']) for identity in (first, second)]
                for rank, change in zip(ranks, changes):
                    assert rank['mmr'] == change['mmrAfter'] and rank['rp'] == change['rp']
                duplicate = api('/api/internal/battle/matches', 'POST', submitted[0], internal=True)
                assert duplicate['duplicate'] and duplicate['rankChanges'] == responses[0]['rankChanges']
                assert ranks == [api('/api/rank/me', token=identity['accessToken']) for identity in (first, second)]
                count = execute(['docker', 'exec', container, 'psql', '-U', 'postgres', '-d', 'projectc_test', '-tAc',
                                 'SELECT count(*) FROM game."Matches" WHERE "MatchId" = \'' + a.match + '\'::uuid'])
                assert count == '1'
                print('C++ -> NestJS -> PostgreSQL: guest/refresh, two owned decks, auto start, normal finish, DB result, RP/MMR, lost ack and idempotency passed', flush=True)
            finally:
                a.close()
                b.close()
                for process in reversed(processes):
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                processes.clear()
                log.close()
    finally:
        for process in processes:
            process.terminate()
            process.wait(timeout=10)
        if proxy:
            proxy.shutdown()
            proxy.server_close()
        subprocess.run(['docker', 'rm', '-f', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == '__main__':
    run(str(Path(sys.argv[1]).resolve()), Path(sys.argv[2]).resolve())
