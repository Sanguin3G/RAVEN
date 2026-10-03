"""Exercise built images locally with disposable data and no live provider calls.

Run from the repository root after building raven-api-ci and raven-frontend-ci.
Requires Docker, Python 3, and OpenSSL (also run by GitHub CI).
"""
import base64
import http.cookies
import json
import os
from pathlib import Path
import secrets
import ssl
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid


def docker(*args, **kwargs):
    return subprocess.run(["docker", *args], check=True, text=True,
                          stdout=subprocess.PIPE, **kwargs).stdout.strip()


def main():
    prefix = "raven-smoke-" + uuid.uuid4().hex[:12]
    network, api, frontend = prefix, prefix + "-api", prefix + "-frontend"
    volumes = [prefix + "-empty", prefix + "-seed"]
    environment = os.environ.copy()
    environment.update({
        "RAVEN_BOOTSTRAP_ADMIN_EMAIL": "smoke@raven.example",
        "RAVEN_BOOTSTRAP_ADMIN_PASSWORD": secrets.token_hex(24) + "aA1!",
        "RAVEN_CREDENTIAL_MASTER_KEY": base64.b64encode(secrets.token_bytes(32)).decode(),
    })
    # Certificate verification is disabled only for this disposable localhost certificate.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}),
        urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))
    cookies = {}
    origin = ""

    def request(path, expected=200, payload=None, csrf=None):
        headers = {"Cookie": "; ".join(f"{k}={v}" for k, v in cookies.items())}
        if csrf:
            headers["X-RAVEN-CSRF"] = csrf
        data = None if payload is None else json.dumps(payload).encode()
        if data is not None:
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(origin + path, data=data, headers=headers)
        try:
            response = opener.open(req, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            assert response.code == expected, f"{path}: expected {expected}, got {response.code}"
            for value in response.headers.get_all("Set-Cookie", []):
                parsed = http.cookies.SimpleCookie()
                parsed.load(value)
                for name, morsel in parsed.items():
                    cookies[name] = morsel.value
            return response.read()

    def ready():
        for _ in range(90):
            try:
                request("/api/health")
                return
            except (OSError, AssertionError):
                time.sleep(1)
        raise AssertionError("API did not become healthy")

    try:
        docker("network", "create", network)
        for volume in volumes:
            docker("volume", "create", volume)
        with tempfile.TemporaryDirectory(prefix="raven-container-smoke-") as temporary:
            folder = Path(temporary)
            subprocess.run(["openssl", "req", "-x509", "-nodes", "-newkey", "rsa:2048",
                "-keyout", str(folder / "key.pem"), "-out", str(folder / "cert.pem"),
                "-days", "1", "-subj", "/CN=localhost"], check=True,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            template = Path("frontend/nginx.conf").read_text().replace("listen 8080;",
                "listen 8443 ssl;\n    ssl_certificate /smoke/cert.pem;\n    ssl_certificate_key /smoke/key.pem;")
            (folder / "nginx.conf").write_text(template)

            for index, seeded in enumerate((False, True)):
                cookies.clear()
                docker("run", "-d", "--name", api, "--network", network, "--network-alias", "api",
                    "--mount", f"source={volumes[index]},target=/app/data",
                    "-e", "ASPNETCORE_ENVIRONMENT=Production",
                    "-e", "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true",
                    "-e", f"RAVEN_SEED_DATABASE={str(seeded).lower()}",
                    "-e", "RAVEN_BOOTSTRAP_ADMIN_EMAIL", "-e", "RAVEN_BOOTSTRAP_ADMIN_PASSWORD",
                    "-e", "RAVEN_CREDENTIAL_MASTER_KEY", "raven-api-ci", env=environment)
                docker("run", "-d", "--name", frontend, "--network", network,
                    "-p", "127.0.0.1::8443", "-e", "RAVEN_API_ORIGIN=http://api:8080",
                    "--mount", f"type=bind,source={folder},target=/smoke,readonly",
                    "--mount", f"type=bind,source={folder / 'nginx.conf'},target=/etc/nginx/templates/default.conf.template,readonly",
                    "raven-frontend-ci")
                port = docker("port", frontend, "8443/tcp").rsplit(":", 1)[1]
                origin = "https://127.0.0.1:" + port
                ready()
                assert b'<!doctype html' in request("/companies").lower()
                request("/api/companies", expected=401)
                csrf = json.loads(request("/api/auth/csrf"))["requestToken"]
                user = json.loads(request("/api/auth/login", payload={
                    "email": environment["RAVEN_BOOTSTRAP_ADMIN_EMAIL"],
                    "password": environment["RAVEN_BOOTSTRAP_ADMIN_PASSWORD"]}, csrf=csrf))
                assert user["roles"] == ["Admin"]
                companies = json.loads(request("/api/companies"))
                assert len(companies) == (7 if seeded else 0)
                # Persisted Data Protection keys keep the session valid across process restart.
                docker("restart", api)
                ready()
                request("/api/auth/me")
                assert json.loads(request("/api/companies")) == companies
                docker("rm", "-f", frontend, api)
                print(f"Container smoke passed: seed={seeded}, HTTPS proxy, login, SPA, session after restart")
    finally:
        # Targets are unique resources created by this invocation only.
        subprocess.run(["docker", "rm", "-f", frontend, api], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for volume in volumes:
            subprocess.run(["docker", "volume", "rm", volume], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(["docker", "network", "rm", network], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
