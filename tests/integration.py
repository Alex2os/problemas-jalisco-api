#!/usr/bin/env python3
"""Real HTTP/PostgreSQL regressions using ONLY a disposable, local database and fake geocoder.
Requires an unprivileged Unix user, PostgreSQL tools, Python 3 and a built API. No Python packages.
"""
import base64
import concurrent.futures
import hashlib
import hmac
import http.server
import json
import os
from pathlib import Path
import re
import secrets
import struct
import zlib
import shutil
import socket
import subprocess
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = Path(os.environ.get('JALISCO_TEST_API_DLL', str(ROOT / 'bin/Debug/net10.0/JaliscoProblemsAPI.dll'))).resolve()


def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def request(base, method, path, body=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None, headers=headers, method=method)
    try:
        response = urllib.request.urlopen(req, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        text = response.read().decode()
        return response.status, json.loads(text) if text else {}, response.headers

def png(width=2, height=2):
    def chunk(name, data):
        return struct.pack('>I', len(data)) + name + data + struct.pack('>I', zlib.crc32(name + data) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress((b'\x00' + b'\x80\x40\x20' * width) * height)) + chunk(b'IEND', b'')


def multipart(base, path, token, files, fields=None):
    boundary = 'test-' + secrets.token_hex(12)
    body = bytearray()
    for name, value in (fields or {}).items():
        body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    for data in files:
        # Deliberately untrusted filename and MIME: the server validates decoded content.
        body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="photos"; filename="../../unsafe.png"\r\nContent-Type: image/png\r\n\r\n'.encode())
        body.extend(data); body.extend(b'\r\n')
    body.extend(f'--{boundary}--\r\n'.encode())
    headers = {'Content-Type': 'multipart/form-data; boundary=' + boundary}
    if token: headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(base + path, data=bytes(body), headers=headers, method='POST')
    try: response = urllib.request.urlopen(req, timeout=30)
    except urllib.error.HTTPError as error: response = error
    with response:
        return response.status, json.loads(response.read()), response.headers


class Geocoder(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
        with self.server.lock:
            self.server.calls.append((time.monotonic(), query))
        lat = query.get('lat', [''])[0]
        status, payload = 200, {'display_name': 'Guadalajara, Jalisco'}
        if lat == '21.123':
            status, payload = 429, {}
        elif lat == '21.124':
            payload = {}
        elif lat == '21.125':
            payload = {'display_name': '😀' * 300}
        elif lat == '21.126':
            time.sleep(6)
        data = json.dumps(payload, ensure_ascii=False).encode()
        try:
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass


class ApiIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            raise RuntimeError('Build the API before running integration.py.')
        if hasattr(os, 'geteuid') and os.geteuid() == 0:
            raise RuntimeError('Run as an unprivileged user; initdb refuses root.')
        pg_config = shutil.which('pg_config')
        if not pg_config:
            raise RuntimeError('Install PostgreSQL server/client tools to run these integration tests.')
        cls.pg_bin = Path(subprocess.check_output([pg_config, '--bindir'], text=True).strip())
        cls.temp = tempfile.TemporaryDirectory(prefix='jalisco-api-tests-')
        cls.addClassCleanup(cls.temp.cleanup)
        cls.runtime = Path(cls.temp.name)
        cls.data = cls.runtime / 'data'
        cls.db_port = free_port()
        subprocess.run([str(cls.pg_bin / 'initdb'), '-D', str(cls.data), '-A', 'trust', '-U', 'postgres', '--no-locale', '--encoding=UTF8'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run([str(cls.pg_bin / 'pg_ctl'), '-D', str(cls.data), '-l', str(cls.runtime / 'postgres.log'), '-o', f"-F -p {cls.db_port} -k {cls.runtime} -c listen_addresses=''", '-w', 'start'], check=True, stdout=subprocess.DEVNULL)
        cls.addClassCleanup(lambda: subprocess.run([str(cls.pg_bin / 'pg_ctl'), '-D', str(cls.data), '-m', 'fast', '-w', 'stop'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        schema_path = ROOT / 'schema.sql'
        if not schema_path.exists():
            schema_path = ROOT / 'jalisco-problemas_schema.sql'
        schema = schema_path.read_text()
        schema = re.sub(r'ALTER DEFAULT PRIVILEGES.*?;', '', schema, flags=re.S)
        schema = '\n'.join(line for line in schema.splitlines() if not line.startswith(('\\restrict', '\\unrestrict')))
        base_schema = schema.split('-- Additive application schema extensions')[0]
        cls.sql(base_schema + "\nCOMMIT;")
        # A real pre-feature row exercises the non-destructive backfill, not only new installs.
        cls.sql("INSERT INTO users(username,email,passwordhash) VALUES('legacy_seed','legacy@example.invalid','not-used')")
        cls.sql("INSERT INTO reports(longitude,latitude,ubication,type,user_id) VALUES(-103,20,'Legacy pre-feature','otros',1)")
        migration = (ROOT / 'database/migrations/001_report_photos_status.sql').read_text()
        cls.sql(migration)
        cls.sql(migration)  # Safe to repeat; history and existing rows must survive.
        cls.geocoder = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Geocoder)
        cls.geocoder.calls, cls.geocoder.lock = [], threading.Lock()
        cls.addClassCleanup(cls.geocoder.server_close)
        threading.Thread(target=cls.geocoder.serve_forever, daemon=True).start()
        cls.addClassCleanup(cls.geocoder.shutdown)
        cls.key = secrets.token_urlsafe(48)
        cls.base = cls.start_api()
        cls.password = secrets.token_urlsafe(15)
        status, _, _ = request(cls.base, 'POST', '/auth/register', {'username': 'smoke_user', 'email': 'smoke@example.invalid', 'password': cls.password})
        if status != 200:
            raise RuntimeError('Fixture registration failed.')
        status, body, _ = request(cls.base, 'POST', '/auth/login', {'email': 'smoke@example.invalid', 'password': cls.password})
        if status != 200:
            raise RuntimeError('Fixture login failed.')
        cls.token, cls.user_id = body['token'], body['user']['id']
        request(cls.base, 'POST', '/reports/create', {'type': 'otros', 'latitude': 20.6597, 'longitude': -103.3496}, cls.token)
        cls.report_id = request(cls.base, 'GET', '/reports')[1]['reports'][0]['id']

    @classmethod
    def sql(cls, sql):
        return subprocess.run([str(cls.pg_bin / 'psql'), '-h', str(cls.runtime), '-p', str(cls.db_port), '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-tAc', sql], check=True, capture_output=True, text=True).stdout.strip()

    @classmethod
    def start_api(cls, extra=None):
        port = free_port()
        env = dict(os.environ)
        env.pop('ASPNETCORE_HTTPS_PORT', None)
        env.update({
            'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': f'http://127.0.0.1:{port}',
            # Always overrides any real user-secrets/DB environment settings.
            'ConnectionStrings__DefaultConnection': f'Host={cls.runtime};Port={cls.db_port};Username=postgres;Database=postgres;Maximum Pool Size=2;Timeout=2;Command Timeout=5',
            'JWT_KEY': cls.key, 'Jwt__Key': cls.key, 'Jwt__Issuer': 'test-issuer', 'Jwt__Audience': 'test-mobile',
            'Geocoding__BaseUrl': f'http://127.0.0.1:{cls.geocoder.server_port}/',
            'Logging__LogLevel__Default': 'Warning', 'RateLimiting__AuthPermitLimit': '1000', 'RateLimiting__WritePermitLimit': '1000',
            'PhotoStorage__RootPath': str(cls.runtime / 'photos'),
            'Cors__AllowedOrigins__0': 'https://mobile.example.invalid',
        })
        env.update(extra or {})
        log = open(cls.runtime / f'api-{port}.log', 'w')
        cls.addClassCleanup(log.close)
        process = subprocess.Popen(['dotnet', str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=log)
        def stop():
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        cls.addClassCleanup(stop)
        base = f'http://127.0.0.1:{port}'
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError('Isolated API failed to start. Inspect its local test log; secrets are not printed.')
            try:
                if request(base, 'GET', '/not-an-endpoint')[0] == 404:
                    return base
            except (OSError, urllib.error.URLError):
                time.sleep(.1)
        raise RuntimeError('Isolated API startup timed out.')

    def problem(self, response, status):
        actual, body, headers = response
        self.assertEqual(status, actual)
        self.assertEqual(status, body['status'])
        self.assertIsInstance(body['message'], str)
        self.assertNotIn('exception', body)
        return body, headers

    def jwt(self, overrides=None, key=None, algorithm='HS256'):
        now = int(time.time())
        payload = {'iss': 'test-issuer', 'aud': 'test-mobile', 'exp': now + 3600, 'user_id': str(self.user_id)}
        payload.update(overrides or {})
        encode = lambda value: base64.urlsafe_b64encode(json.dumps(value).encode()).rstrip(b'=')
        signing = encode({'alg': algorithm, 'typ': 'JWT'}) + b'.' + encode(payload)
        signature = hmac.new((key or self.key).encode(), signing, hashlib.sha256).digest()
        return (signing + b'.' + base64.urlsafe_b64encode(signature).rstrip(b'=')).decode()

    def test_01_public_contracts_and_openapi(self):
        status, body, _ = request(self.base, 'GET', '/reports')
        self.assertEqual(200, status)
        self.assertIn('message', body)
        self.assertTrue({'id', 'latitude', 'longitude', 'ubication', 'description', 'type', 'created_at', 'user_id', 'username'} <= body['reports'][0].keys())
        for path in ['/swagger/v1/swagger.json', '/openapi/v1.json']:
            status, body, _ = request(self.base, 'GET', path)
            self.assertEqual(200, status)
            self.assertIn('/reports/create', body['paths'])
            if path.startswith('/swagger'):
                self.assertIn('Bearer', body['components']['securitySchemes'])
                self.assertTrue(body['paths']['/reports/create']['post']['security'])
                self.assertFalse(body['paths']['/auth/login']['post'].get('security'))
        self.problem(request(self.base, 'GET', '/not-an-endpoint'), 404)

    def test_02_authorization_and_identity_spoofing(self):
        data = {'type': 'otros', 'latitude': 20.6597, 'longitude': -103.3496, 'user_id': 99999}
        self.problem(request(self.base, 'POST', '/reports/create', data), 401)
        for token in ['malformed', self.jwt({'exp': int(time.time()) - 600}), self.jwt({'iss': 'wrong'}), self.jwt({'aud': 'wrong'}), self.jwt({'user_id': '-1'}), self.jwt({'user_id': 'not-a-number'}), self.jwt(key='different-test-key-at-least-32-bytes')]:
            self.problem(request(self.base, 'POST', '/reports/create', data, token), 401)
        self.problem(request(self.base, 'POST', '/reports/create', data, self.jwt({'user_id': '99999'})), 401)
        self.assertEqual(200, request(self.base, 'POST', '/reports/create', data, self.token)[0])
        item = request(self.base, 'GET', '/reports')[1]['reports'][0]
        self.assertEqual(self.user_id, item['user_id'])

    def test_03_validation_is_readable_by_expo(self):
        invalid = {'type': 'otros', 'latitude': 0, 'longitude': -103}
        body, _ = self.problem(request(self.base, 'POST', '/reports/create', invalid, self.token), 400)
        self.assertIn('errors', body)
        self.problem(request(self.base, 'POST', '/auth/register', {'username': 'x' * 21, 'email': 'other@example.invalid', 'password': self.password}), 400)
        self.problem(request(self.base, 'POST', '/auth/register', {'username': 'other_user', 'email': 'other@example.invalid', 'password': 'é' * 37}), 400)
        self.problem(request(self.base, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': ' '}, self.token), 400)
        self.problem(request(self.base, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': 'x' * 101}, self.token), 400)
        for path in ['/reports/0', '/reports?limit=0', '/reports?beforeId=-1', '/reports/search', '/reports/search?query=%20%20', '/reports?limit=201']:
            self.problem(request(self.base, 'GET', path), 400)

    def test_04_missing_reports_are_not_empty_successes(self):
        self.problem(request(self.base, 'GET', '/reports/99999'), 404)
        self.problem(request(self.base, 'GET', '/reports/99999/comments'), 404)
        self.problem(request(self.base, 'POST', '/reports/99999/comments', {'user_comment': 'test'}, self.token), 404)

    def test_05_comments_and_nullable_legacy_fields(self):
        self.assertEqual(200, request(self.base, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': 'Useful detail', 'user_id': 99999}, self.token)[0])
        status, body, _ = request(self.base, 'GET', f'/reports/{self.report_id}/comments')
        self.assertEqual(200, status)
        self.assertEqual(self.user_id, body['comments'][-1]['user_id'])
        self.assertEqual('Useful detail', body['comments'][-1]['user_comment'])
        legacy = self.sql(f"INSERT INTO reports(longitude,latitude,ubication,description,type,created_at,user_id) VALUES(-103,20,'Legacy',NULL,'otros',NULL,{self.user_id}) RETURNING id")
        legacy = int(legacy.splitlines()[0])
        status, body, _ = request(self.base, 'GET', f'/reports/{legacy}')
        self.assertEqual(200, status)
        self.assertEqual('', body['report']['description'])
        self.assertIsNone(body['report']['created_at'])

    def test_06_duplicate_registration_race_and_login(self):
        registration = {'username': 'racing_user', 'email': 'race@example.invalid', 'password': self.password}
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            responses = list(pool.map(lambda _: request(self.base, 'POST', '/auth/register', registration), range(2)))
        self.assertEqual([200, 409], sorted(r[0] for r in responses))
        missing = self.problem(request(self.base, 'POST', '/auth/login', {'email': 'missing@example.invalid', 'password': self.password}), 401)[0]
        wrong = self.problem(request(self.base, 'POST', '/auth/login', {'email': 'smoke@example.invalid', 'password': 'wrong-password'}), 401)[0]
        self.assertEqual(missing['message'], wrong['message'])
        status, body, _ = request(self.base, 'POST', '/auth/login', {'email': 'smoke@example.invalid', 'password': self.password})
        self.assertEqual(200, status)
        self.assertEqual({'id', 'username', 'email'}, set(body['user']))

    def test_07_keyset_pagination_search_and_small_pool_concurrency(self):
        all_reports = request(self.base, 'GET', '/reports')[1]['reports']
        first = request(self.base, 'GET', '/reports?limit=1')[1]['reports']
        self.assertEqual(1, len(first))
        next_page = request(self.base, 'GET', f'/reports?limit=1&beforeId={first[0]["id"]}')[1]['reports']
        self.assertLess(next_page[0]['id'], first[0]['id'])
        self.assertGreater(len(all_reports), len(first))
        self.assertEqual(200, request(self.base, 'GET', '/reports/search?query=Guadalajara')[0])
        injection = urllib.parse.quote("' OR 1=1 --")
        self.assertEqual([], request(self.base, 'GET', '/reports/search?query=' + injection)[1]['reports'])
        paths = ['/reports', f'/reports/{self.report_id}', f'/reports/{self.report_id}/comments'] * 8
        with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
            statuses = list(pool.map(lambda path: request(self.base, 'GET', path)[0], paths))
        self.assertTrue(all(status == 200 for status in statuses))

    def test_08_geocoder_cache_failures_and_unicode(self):
        with self.geocoder.lock:
            before = len(self.geocoder.calls)
        for _ in range(2):
            self.assertEqual(200, request(self.base, 'POST', '/reports/create', {'type': 'otros', 'latitude': 20.6597, 'longitude': -103.3496}, self.token)[0])
        with self.geocoder.lock:
            self.assertEqual(before, len(self.geocoder.calls))
        count = int(self.sql('SELECT count(*) FROM reports'))
        for latitude in [21.123, 21.124, 21.126]:
            self.problem(request(self.base, 'POST', '/reports/create', {'type': 'otros', 'latitude': latitude, 'longitude': -103}, self.token), 503)
        self.assertEqual(count, int(self.sql('SELECT count(*) FROM reports')))
        self.assertEqual(200, request(self.base, 'POST', '/reports/create', {'type': 'otros', 'latitude': 21.125, 'longitude': -103}, self.token)[0])
        location = request(self.base, 'GET', '/reports')[1]['reports'][0]['ubication']
        self.assertEqual(255, len(location))
        self.assertNotIn('\uFFFD', location)
        with self.geocoder.lock:
            times = [entry[0] for entry in self.geocoder.calls]
        self.assertTrue(all(b - a >= .98 for a, b in zip(times, times[1:])))

    def test_09_rate_limits_are_explicit_and_per_authenticated_user(self):
        limited = self.start_api({'RateLimiting__AuthPermitLimit': '2', 'RateLimiting__WritePermitLimit': '2'})
        credentials = {'email': 'missing@example.invalid', 'password': self.password}
        for _ in range(2):
            self.assertEqual(401, request(limited, 'POST', '/auth/login', credentials)[0])
        _, headers = self.problem(request(limited, 'POST', '/auth/login', credentials), 429)
        self.assertIn('Retry-After', headers)
        for _ in range(2):
            self.assertEqual(200, request(limited, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': 'rate limit test'}, self.token)[0])
        self.problem(request(limited, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': 'rate limit test'}, self.token), 429)
        status, body, _ = request(self.base, 'POST', '/auth/login', {'email': 'race@example.invalid', 'password': self.password})
        self.assertEqual(200, status)
        self.assertEqual(200, request(limited, 'POST', f'/reports/{self.report_id}/comments', {'user_comment': 'another user'}, body['token'])[0])

    def test_10_cors_and_sanitized_database_errors(self):
        req = urllib.request.Request(self.base + '/reports', method='OPTIONS', headers={'Origin': 'https://mobile.example.invalid', 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization'})
        with urllib.request.urlopen(req) as response:
            self.assertEqual('https://mobile.example.invalid', response.headers.get('Access-Control-Allow-Origin'))
        dead_port = free_port()
        unavailable = self.start_api({'ConnectionStrings__DefaultConnection': f'Host=127.0.0.1;Port={dead_port};Username=test;Database=test;Timeout=1'})
        body, _ = self.problem(request(unavailable, 'GET', '/reports'), 503)
        self.assertNotIn('Npgsql', json.dumps(body))
        self.assertNotIn('Host=', json.dumps(body))
        bad_database = self.start_api({'ConnectionStrings__DefaultConnection': f'Host={self.runtime};Port={self.db_port};Username=postgres;Database=missing_test_database;Timeout=1'})
        body, _ = self.problem(request(bad_database, 'GET', '/reports'), 500)
        self.assertNotIn('PostgresException', json.dumps(body))
        self.assertNotIn('stack', json.dumps(body).lower())


    def other_user(self):
        cls = type(self)
        if not hasattr(cls, 'other_token'):
            self.assertEqual(200, request(self.base, 'POST', '/auth/register', {'username': 'other_user', 'email': 'other@example.invalid', 'password': self.password})[0])
            status, body, _ = request(self.base, 'POST', '/auth/login', {'email': 'other@example.invalid', 'password': self.password})
            self.assertEqual(200, status)
            cls.other_token, cls.other_id = body['token'], body['user']['id']
        return cls.other_token

    def test_11_likes_are_idempotent_personalized_concurrent_and_persisted(self):
        endpoint = f'/reports/{self.report_id}/like'
        for method in ['PUT', 'DELETE']:
            self.problem(request(self.base, method, endpoint), 401)
            self.problem(request(self.base, method, '/reports/99999/like', token=self.token), 404)
        baseline = request(self.base, 'GET', f'/reports/{self.report_id}', token=self.token)[1]['report']['like_count']
        for _ in range(2):
            status, body, _ = request(self.base, 'PUT', endpoint, {'user_id': 99999}, self.token)
            self.assertEqual(200, status)
            self.assertEqual(baseline + 1, body['interactions']['like_count'])
            self.assertTrue(body['interactions']['liked_by_me'])
        self.assertEqual('1', self.sql(f'SELECT count(*) FROM likes WHERE user_id={self.user_id} AND report_id={self.report_id}'))
        anonymous = request(self.base, 'GET', f'/reports/{self.report_id}')[1]['report']
        self.assertFalse(anonymous['liked_by_me'])
        self.assertEqual(baseline + 1, anonymous['like_count'])
        self.assertTrue(request(self.base, 'GET', f'/reports/{self.report_id}', token=self.token)[1]['report']['liked_by_me'])
        self.assertTrue(next(item for item in request(self.base, 'GET', '/reports', token=self.token)[1]['reports'] if item['id'] == self.report_id)['liked_by_me'])
        self.assertEqual(200, request(self.base, 'PUT', endpoint, token=self.other_user())[0])
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            responses = list(pool.map(lambda _: request(self.base, 'PUT', endpoint, token=self.token), range(16)))
        self.assertTrue(all(item[0] == 200 for item in responses))
        current = request(self.base, 'GET', f'/reports/{self.report_id}', token=self.token)[1]['report']
        self.assertEqual(baseline + 2, current['like_count'])
        self.assertEqual('2', self.sql(f'SELECT count(*) FROM likes WHERE report_id={self.report_id}'))
        restarted = self.start_api()
        persisted = request(restarted, 'GET', f'/reports/{self.report_id}', token=self.other_user())[1]['report']
        self.assertTrue(persisted['liked_by_me'])
        self.assertEqual(current['like_count'], persisted['like_count'])
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            responses = list(pool.map(lambda _: request(self.base, 'DELETE', endpoint, token=self.token), range(16)))
        self.assertTrue(all(item[0] == 200 for item in responses))
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            list(pool.map(lambda method: request(self.base, method, endpoint, token=self.token), ['PUT', 'DELETE'] * 6))
        # Concurrent opposing intents may finish in either order; a subsequent explicit delete is deterministic.
        self.assertEqual(200, request(self.base, 'DELETE', endpoint, token=self.token)[0])
        self.assertFalse(request(self.base, 'GET', f'/reports/{self.report_id}', token=self.token)[1]['report']['liked_by_me'])
        self.assertEqual(200, request(self.base, 'DELETE', endpoint, token=self.other_user())[0])
        self.assertEqual(baseline, request(self.base, 'GET', f'/reports/{self.report_id}')[1]['report']['like_count'])

    def test_12_comment_creation_pagination_ownership_deletion_and_counts(self):
        endpoint = f'/reports/{self.report_id}/comments'
        created = []
        baseline = request(self.base, 'GET', f'/reports/{self.report_id}')[1]['report']['comment_count']
        for _ in range(3):
            status, body, _ = request(self.base, 'POST', endpoint, {'user_comment': '  Created through API  ', 'user_id': 99999}, self.token)
            self.assertEqual(200, status)
            comment = body['comment']
            self.assertEqual(self.user_id, comment['user_id'])
            self.assertEqual(self.report_id, comment['report_id'])
            self.assertEqual('Created through API', comment['user_comment'])
            self.assertEqual('smoke_user', comment['username'])
            self.assertTrue(comment['created_at'])
            self.assertTrue(comment['can_delete'])
            created.append(comment)
        self.assertEqual(baseline + 3, body['interactions']['comment_count'])
        self.assertEqual(baseline + 3, request(self.base, 'GET', f'/reports/{self.report_id}')[1]['report']['comment_count'])
        self.assertTrue(all(not comment['can_delete'] for comment in request(self.base, 'GET', endpoint)[1]['comments']))
        other_view = request(self.base, 'GET', endpoint, token=self.other_user())[1]['comments']
        self.assertTrue(all(not comment['can_delete'] for comment in other_view if comment['user_id'] == self.user_id))
        ids, cursor = [], None
        while True:
            path = endpoint + '?limit=2' + (f'&afterId={cursor}' if cursor else '')
            status, page, _ = request(self.base, 'GET', path, token=self.token)
            self.assertEqual(200, status)
            self.assertLessEqual(len(page['comments']), 2)
            for comment in page['comments']:
                self.assertEqual(comment['user_id'] == self.user_id, comment['can_delete'])
            ids.extend(comment['id'] for comment in page['comments'])
            if not page['has_more']:
                self.assertIsNone(page['next_after_id'])
                break
            cursor = page['next_after_id']
            self.assertEqual(page['comments'][-1]['id'], cursor)
        self.assertEqual(sorted(set(ids)), ids)
        self.assertEqual(baseline + 3, len(ids))
        for comment in created:
            deletion = endpoint + f'/{comment["id"]}'
            self.problem(request(self.base, 'DELETE', deletion), 401)
            self.problem(request(self.base, 'DELETE', deletion, token=self.other_user()), 403)
            self.assertEqual('1', self.sql(f'SELECT count(*) FROM comments WHERE id={comment["id"]}'))
            status, body, _ = request(self.base, 'DELETE', deletion, token=self.token)
            self.assertEqual(200, status)
            self.assertEqual(comment['id'], body['comment_id'])
            self.problem(request(self.base, 'DELETE', deletion, token=self.token), 404)
        self.assertEqual(baseline, request(self.base, 'GET', f'/reports/{self.report_id}')[1]['report']['comment_count'])
        self.problem(request(self.base, 'DELETE', '/reports/99999/comments/99999', token=self.token), 404)
        self.problem(request(self.base, 'DELETE', endpoint + '/99999', token=self.token), 404)

    def test_13_actual_mobile_api_services_use_the_real_backend_and_database(self):
        frontend = Path(os.environ.get('JALISCO_TEST_FRONTEND_DIR', str(ROOT.parent / 'jalisco-problems')))
        script = frontend / 'tests/api-integration.cjs'
        if not script.is_file() or not shutil.which('node'):
            self.skipTest('Mobile project/Node unavailable. Run this check with both repositories present.')
        env = dict(os.environ)
        env['EXPO_PUBLIC_API_URL'] = self.base
        env['JALISCO_TEST_REPORT_ID'] = str(self.report_id)
        result = subprocess.run(['node', str(script)], cwd=frontend, env=env, capture_output=True, text=True, timeout=45)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        print(result.stdout.strip())


    def test_14_status_permissions_history_concurrency_and_backfill(self):
        legacy = request(self.base, 'GET', '/reports/1')[1]['report']
        self.assertEqual('reported', legacy['status'])
        self.assertEqual([], legacy['photos'])
        history = request(self.base, 'GET', '/reports/1/status/history')[1]['history']
        self.assertEqual(1, len(history)); self.assertEqual('backfill', history[0]['source']); self.assertIsNone(history[0]['actor_id'])
        endpoint = f'/reports/{self.report_id}/status'
        original = request(self.base, 'GET', endpoint)[1]
        self.assertEqual('reported', original['status'])
        self.assertFalse(original['can_manage'])
        change = {'status': 'still_present', 'expected_version': original['status_version'], 'actor_id': 99999}
        self.problem(request(self.base, 'PUT', endpoint, change), 401)
        self.problem(request(self.base, 'PUT', endpoint, change, self.other_user()), 403)
        for invalid in [{'status': 'in_progress', 'expected_version': 0}, {'status': 'resolved'}, {'status': 'resolved', 'expected_version': -1}]:
            self.problem(request(self.base, 'PUT', endpoint, invalid, self.token), 400)
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: request(self.base, 'PUT', endpoint, change, self.token), range(2)))
        self.assertEqual([200, 409], sorted(result[0] for result in results))
        current = request(self.base, 'GET', endpoint, token=self.token)[1]
        self.assertEqual('still_present', current['status']); self.assertTrue(current['can_manage'])
        self.assertEqual(1, current['status_version'])
        history = request(self.base, 'GET', endpoint + '/history?limit=1')[1]
        self.assertTrue(history['has_more']); self.assertEqual(self.user_id, history['history'][0]['actor_id'])
        self.assertEqual('reported', history['history'][0]['previous_status'])
        next_page = request(self.base, 'GET', endpoint + '/history?beforeVersion=' + str(history['next_before_version']))[1]
        self.assertEqual(0, next_page['history'][0]['version'])
        self.assertEqual(200, request(self.base, 'PUT', endpoint, {'status': 'resolved', 'expected_version': 1}, self.token)[0])
        self.problem(request(self.base, 'PUT', endpoint, {'status': 'reported', 'expected_version': 2}, self.token), 400)
        self.assertEqual(200, request(self.base, 'PUT', endpoint, {'status': 'resolved', 'expected_version': 2}, self.token)[0])
        self.assertEqual('3', self.sql(f'SELECT count(*) FROM report_status_history WHERE report_id={self.report_id}'))
        self.assertEqual(200, request(self.base, 'PUT', endpoint, {'status': 'still_present', 'expected_version': 2}, self.token)[0])
        restarted = self.start_api()
        self.assertEqual(3, request(restarted, 'GET', endpoint)[1]['status_version'])
        self.assertEqual('4', self.sql(f'SELECT count(*) FROM report_status_history WHERE report_id={self.report_id}'))
        self.problem(request(self.base, 'PUT', '/reports/99999/status', {'status': 'resolved', 'expected_version': 0}, self.token), 404)

    def test_15_photos_atomic_create_limits_validation_and_persistence(self):
        fields = {'type': 'otros', 'description': 'With real photos', 'latitude': 20.6597, 'longitude': -103.3496}
        self.problem(multipart(self.base, '/reports/create-with-photos', None, [png()], fields), 401)
        before = int(self.sql('SELECT count(*) FROM reports'))
        self.problem(multipart(self.base, '/reports/create-with-photos', self.token, [png(), b'not an image'], fields), 400)
        self.assertEqual(before, int(self.sql('SELECT count(*) FROM reports')))
        status, created, _ = multipart(self.base, '/reports/create-with-photos', self.token, [png()], fields)
        self.assertEqual(200, status)
        report = created['report']; report_id = report['id']; endpoint = f'/reports/{report_id}/photos'
        self.assertEqual(1, len(report['photos'])); self.assertTrue(report['can_manage']); self.assertEqual('reported', report['status'])
        for token, expected in [(None, 401), (self.other_user(), 403)]:
            self.problem(multipart(self.base, endpoint, token, [png()]), expected)
        for invalid in [b'<svg></svg>', b'\xff\xd8\xffinvalid', png()[:24], b'x' * (5*1024*1024+1), png(6001, 1)]:
            self.problem(multipart(self.base, endpoint, self.token, [invalid]), 400)
        self.problem(multipart(self.base, endpoint, self.token, [png()] * 4), 400)
        self.problem(multipart(self.base, '/reports/99999/photos', self.token, [png()]), 404)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda _: multipart(self.base, endpoint, self.token, [png()]), range(4)))
        self.assertEqual([200, 200, 409, 409], sorted(result[0] for result in results))
        current = request(self.base, 'GET', f'/reports/{report_id}')[1]['report']
        self.assertEqual(3, len(current['photos'])); self.assertEqual([1,2,3], [photo['display_order'] for photo in current['photos']])
        self.assertEqual('3', self.sql(f'SELECT count(*) FROM report_images WHERE report_id={report_id}'))
        self.assertEqual('image/jpeg', self.sql(f'SELECT DISTINCT content_type FROM report_images WHERE report_id={report_id}'))
        first = current['photos'][0]
        with urllib.request.urlopen(self.base + first['url']) as response:
            content = response.read(); self.assertTrue(content.startswith(b'\xff\xd8\xff')); self.assertEqual('image/jpeg', response.headers['Content-Type']); self.assertEqual('nosniff', response.headers['X-Content-Type-Options'])
        restarted = self.start_api()
        self.assertEqual(3, len(request(restarted, 'GET', f'/reports/{report_id}')[1]['report']['photos']))
        with urllib.request.urlopen(restarted + first['url']) as response: self.assertEqual(content, response.read())
        deletion = endpoint + '/' + str(first['id'])
        self.problem(request(self.base, 'DELETE', deletion), 401)
        self.problem(request(self.base, 'DELETE', deletion, token=self.other_user()), 403)
        self.assertEqual(200, request(self.base, 'DELETE', deletion, token=self.token)[0])
        self.problem(request(self.base, 'DELETE', deletion, token=self.token), 404)
        self.problem(request(self.base, 'GET', first['url']), 404)
        self.assertEqual(200, multipart(self.base, endpoint, self.token, [png()])[0])
        current = request(self.base, 'GET', f'/reports/{report_id}')[1]['report']
        # Missing disk data is an explicit 404, never an unrelated path or an unhandled exception.
        missing = current['photos'][-1]
        key = self.sql(f"SELECT storage_key FROM report_images WHERE id={missing['id']}")
        (self.runtime / 'photos' / key).unlink()
        self.problem(request(self.base, 'GET', missing['url']), 404)
        # New functionality must preserve real interactions on a photographed report.
        self.assertEqual(200, request(self.base, 'PUT', f'/reports/{report_id}/like', token=self.token)[0])
        self.assertEqual(200, request(self.base, 'POST', f'/reports/{report_id}/comments', {'user_comment': 'Photo verified'}, self.token)[0])
        final = request(self.base, 'GET', f'/reports/{report_id}', token=self.token)[1]['report']
        self.assertEqual(1, final['like_count']); self.assertEqual(1, final['comment_count'])
        self.assertEqual(200, request(self.base, 'PUT', f'/reports/{report_id}/status', {'status':'resolved','expected_version':0}, self.token)[0])



    def test_16_actual_mobile_photo_status_services(self):
        frontend = Path(os.environ.get('JALISCO_TEST_FRONTEND_DIR', str(ROOT.parent / 'jalisco-problems')))
        script = frontend / 'tests/media-api-integration.cjs'
        if not script.is_file() or not shutil.which('node'):
            self.skipTest('Mobile project/Node unavailable.')
        # Obtain real normalized JPEG bytes from the API, then use them as a native-selected file.
        _, body, _ = multipart(self.base, '/reports/create-with-photos', self.token, [png()], {'type':'otros','latitude':20.6597,'longitude':-103.3496})
        photo = self.runtime / 'mobile-selected.jpg'
        with urllib.request.urlopen(self.base + body['report']['photos'][0]['url']) as response:
            photo.write_bytes(response.read())
        env = dict(os.environ, EXPO_PUBLIC_API_URL=self.base, JALISCO_TEST_PHOTO_PATH=str(photo))
        result = subprocess.run(['node',str(script)],cwd=frontend,env=env,capture_output=True,text=True,timeout=60)
        self.assertEqual(0,result.returncode,result.stdout+result.stderr)
        print(result.stdout.strip())



    def test_17_pasteable_schema_preserves_existing_data_when_repeated(self):
        schema = (ROOT / 'schema.sql').read_text()
        self.assertEqual(schema, (ROOT / 'jalisco-problemas_schema.sql').read_text())
        tables = ['users', 'reports', 'likes', 'comments', 'report_images', 'report_status_history']
        before = {table: self.sql(f'SELECT count(*) FROM public.{table}') for table in tables}
        statuses = self.sql('SELECT id,status,status_version,status_updated_at FROM public.reports ORDER BY id')
        self.sql(schema)
        self.sql(schema)
        self.assertEqual(before, {table: self.sql(f'SELECT count(*) FROM public.{table}') for table in tables})
        self.assertEqual(statuses, self.sql('SELECT id,status,status_version,status_updated_at FROM public.reports ORDER BY id'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
