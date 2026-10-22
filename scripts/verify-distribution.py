"""Archive and verify a native distribution on its matching CI runner."""

import json
import os
from pathlib import Path
import platform
import shutil
import signal
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


def request(base_url, path, method="GET", payload=None, expected=200):
    """Check an HTTP status and return its decoded JSON body when present."""
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(base_url + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        response = urllib.request.urlopen(req, timeout=3)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        assert response.status == expected, (method, path, response.status)
        body = response.read()
        return json.loads(body) if body else None


def stop(process):
    """Request graceful host shutdown and fail verification if forced cleanup is needed."""
    if process.poll() is not None:
        raise RuntimeError(f"API exited unexpectedly: {process.returncode}")
    process.send_signal(signal.CTRL_BREAK_EVENT if os.name == "nt" else signal.SIGTERM)
    try:
        code = process.wait(timeout=20)
        assert code == 0, f"API shutdown exit code: {code}"
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
        raise RuntimeError("API did not shut down gracefully")


def main():
    """Verify migrations, configuration, CRUD, and persistence in an extracted package."""
    rid = sys.argv[1]
    expected_platform = {
        "win-x64": ("Windows", "amd64"),
        "osx-arm64": ("Darwin", "arm64"),
        "osx-x64": ("Darwin", "x86_64"),
        "linux-x64": ("Linux", "x86_64"),
    }
    assert (platform.system(), platform.machine().lower()) == expected_platform[rid]
    root = Path(__file__).resolve().parents[1]
    package = root / "artifacts" / "package" / rid
    executable_name = "TaskPilot.Api.exe" if os.name == "nt" else "TaskPilot.Api"
    assert (package / executable_name).is_file()
    assert (package / "appsettings.json").is_file()
    assert not list(package.rglob("*.db")), "A database must never ship in the package"
    shutil.copy2(root / "README.md", package / "README.md")
    archives = root / "artifacts" / "distributions"
    archives.mkdir(parents=True, exist_ok=True)
    archive = shutil.make_archive(str(archives / f"taskpilot-api-{rid}"),
                                  "zip" if os.name == "nt" else "gztar", package)
    logs = root / "artifacts" / "runtime-verification" / rid
    logs.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="taskpilot-distribution-") as temporary:
        work = Path(temporary)
        extracted = work / "package"
        shutil.unpack_archive(archive, extracted)
        executable = extracted / executable_name
        database = work / "data" / "tasks.db"
        # Editing the sidecar proves it is loaded even from a different working directory.
        settings = json.loads((extracted / "appsettings.json").read_text())
        settings["TaskPilot"] = {"DatabasePath": str(database)}
        (extracted / "appsettings.json").write_text(json.dumps(settings))
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(("TaskPilot", "ASPNETCORE_", "DOTNET_", "ConnectionStrings__"))}
        environment["DOTNET_ENVIRONMENT"] = "Production"
        environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = str(work / "bundle")
        created = None
        for attempt in range(2):
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                port = listener.getsockname()[1]
            base_url = f"http://127.0.0.1:{port}"
            log_path = logs / f"startup-{attempt + 1}.log"
            with log_path.open("w") as output:
                process = subprocess.Popen(
                    [str(executable), "--urls", base_url], cwd=work, env=environment,
                    stdout=output, stderr=subprocess.STDOUT,
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
                try:
                    deadline = time.monotonic() + 60
                    while True:
                        if process.poll() is not None:
                            raise RuntimeError(f"API startup failed; see {log_path}")
                        try:
                            assert request(base_url, "/api/health") == {"status": "ok"}
                            break
                        except (urllib.error.URLError, TimeoutError):
                            if time.monotonic() >= deadline:
                                raise RuntimeError(f"API readiness timed out; see {log_path}")
                            time.sleep(0.5)
                    assert database.is_file(), "Configured database was not created"
                    with sqlite3.connect(database) as connection:
                        migrations = connection.execute(
                            'SELECT MigrationId FROM "__EFMigrationsHistory"').fetchall()
                        assert ("20261022102839_InitialCreate",) in migrations
                        assert len(migrations) == len(set(migrations))
                    if attempt == 0:
                        assert request(base_url, "/api/tasks") == []
                        created = request(base_url, "/api/tasks", "POST",
                                          {"title": "Survive packaged restart"}, 201)
                        assert created["id"] and not created["isCompleted"]
                    else:
                        path = f'/api/tasks/{created["id"]}'
                        assert request(base_url, path) == created
                        updated = request(base_url, path, "PUT",
                                          {"title": "Verified", "isCompleted": True})
                        assert updated["isCompleted"] and updated["title"] == "Verified"
                        request(base_url, path, "DELETE", expected=204)
                        request(base_url, path, expected=404)
                    request(base_url, "/api/tasks", "POST", {"title": ""}, 400)
                finally:
                    if process.poll() is None:
                        stop(process)
            assert "Application is shutting down" in log_path.read_text(), "Missing graceful shutdown log"
        assert not (work / "taskpilot.db").exists(), "Working-directory database was created"
    summary = f"{rid}: PASS — native executable launched twice; migrations, HTTP CRUD, validation, persistence, and graceful shutdown verified.\n"
    (logs / "result.txt").write_text(summary, encoding="utf-8")
    print(summary)
    if "GITHUB_STEP_SUMMARY" in os.environ:
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
            output.write(summary)


if __name__ == "__main__":
    main()
