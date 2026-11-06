"""Start the actual ASP.NET process and inspect HTTP + JSON logs. No external service."""
import argparse, concurrent.futures, json, os, pathlib, socket, subprocess, tempfile, threading, time, urllib.request, urllib.error
ap = argparse.ArgumentParser(); ap.add_argument('--dll', required=True); args = ap.parse_args()
dotnet = os.environ.get('DOTNET_EXE', 'dotnet')
checks = 0
def check(ok, name):
    global checks
    if not ok: raise AssertionError(name)
    checks += 1; print('PASS ' + name)
def run_case(secret, fail):
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix='operations-http-') as temp:
        log = pathlib.Path(temp) / 'console.log'
        env = {**os.environ, 'ASPNETCORE_URLS': f'http://127.0.0.1:{port}',
               'LAB_DEPENDENCY_TOKEN': secret, 'LAB_FAIL_READINESS': str(fail).lower(),
               'SOURCE_COMMIT': 'local-http-check', 'ASPNETCORE_ENVIRONMENT': 'Production'}
        with log.open('w', encoding='utf-8') as output:
            process = subprocess.Popen([dotnet, args.dll], env=env, stdout=output, stderr=subprocess.STDOUT)
            def request(path):
                try:
                    with urllib.request.urlopen(f'http://127.0.0.1:{port}' + path, timeout=5) as r:
                        return r.status, r.headers, r.read()
                except urllib.error.HTTPError as e: return e.code, e.headers, e.read()
            try:
                for _ in range(300):
                    if process.poll() is not None: raise AssertionError('API exited before ready')
                    try:
                        if request('/health/live')[0] == 200: break
                    except OSError: pass
                    time.sleep(.1)
                else:
                    safe = log.read_text(encoding='utf-8').replace(secret, '[REDACTED]') if secret else log.read_text(encoding='utf-8')
                    raise AssertionError('API startup timeout; sanitized diagnostics: ' + safe[-1800:])
                check(request('/health/live')[0] == 200, 'liveness independent of secret readiness')
                expected = 200 if secret and not fail else 503
                check(request('/health/ready')[0] == expected, 'readiness follows configuration')
                status, headers, body = request('/version')
                version = json.loads(body)
                check(status == 200 and version['mode'] == 'synthetic-read-only', 'version boundary explicit')
                check(version['sourceCommit'] == 'local-http-check', 'source identity returned')
                check(len(headers['X-Correlation-Id']) == 32, 'generated request correlation')
                if secret and not fail:
                    check(request('/api/reference')[0] == 200, 'actual reference read succeeds')
                    check(request('/api/reference?delayMs=-1')[0] == 400, 'invalid delay rejected')
                    check(request('/api/reference?delayMs=2001')[0] == 400, 'unbounded delay rejected')
                    check(request('/api/reference?delayMs=1000')[0] == 504, 'actual cooperative timeout mapped')
                    check(request('/api/reference')[0] == 200, 'capacity recovers after timeout')
                    check(request('/not-a-real-route?token=never-log-this')[0] == 404, 'unknown route rejected')
                    barrier = threading.Barrier(24)
                    def concurrent_read(_):
                        barrier.wait(timeout=10)
                        return request('/api/reference?delayMs=1000')
                    with concurrent.futures.ThreadPoolExecutor(max_workers=24) as pool:
                        burst = list(pool.map(concurrent_read, range(24)))
                    rejected = [r for r in burst if r[0] == 429]
                    check(bool(rejected), 'concurrent burst observes actual rate rejection')
                    check(all(len(r[1]['X-Correlation-Id']) == 32 for r in rejected), 'rate rejections have generated correlation')
                time.sleep(.15)
            finally:
                process.terminate()
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
        lines = [json.loads(line) for line in log.read_text(encoding='utf-8').splitlines() if line.startswith('{')]
        completed = [line for line in lines if 'request_completed' in line.get('Message', '')]
        check(bool(completed), 'actual structured request logs observed')
        if secret and not fail:
            check(any(line.get('State', {}).get('Status') == 429 for line in completed), 'rate rejection present in application logs')
        text = log.read_text(encoding='utf-8')
        check((not secret or secret not in text) and 'never-log-this' not in text and 'not-a-real-route' not in text, 'secrets raw paths and query values absent from logs')
run_case('LOCAL_SYNTHETIC_SENTINEL_DO_NOT_LOG', False)
run_case('', False)
run_case('LOCAL_SYNTHETIC_SENTINEL_DO_NOT_LOG', True)
print(f'PASS {checks} HTTP/log checks; Azure not exercised')
