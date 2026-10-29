"""Управление демонстрационным сервером на Python.

Примеры:
    py samples/run_demo.py --seed --fill
    py samples/run_demo.py --port 5200
    py samples/run_demo.py --status
    py samples/run_demo.py --stop
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "src" / "Wedding.Api" / "Wedding.Api.csproj"
DB = ROOT / "src" / "Wedding.Api" / "demo.db"
GUESTS_JSON = ROOT / "samples" / "demo_guests.json"
PID_FILE = Path(tempfile.gettempdir()) / "wedding-demo.pid"
LOG_FILE = Path(tempfile.gettempdir()) / "wedding-demo.log"


def _process(pid: int) -> psutil.Process | None:
    try:
        proc = psutil.Process(pid)
        return proc if proc.is_running() else None
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        return None


def _is_server_process(proc: psutil.Process | None) -> bool:
    if proc is None or proc.pid == os.getpid():
        return False
    try:
        name = proc.name().lower()
        cmdline = " ".join(proc.cmdline()).lower()
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        return False
    if not (name.startswith("dotnet") or name.startswith("wedding.api")):
        return False
    return str(CSPROJ).lower() in cmdline or "wedding.api" in cmdline


def _read_pid_file() -> dict | None:
    if not PID_FILE.exists():
        return None
    raw = PID_FILE.read_text(encoding="utf-8").strip()
    if not raw:
        return None
    try:
        data = json.loads(raw)
        if isinstance(data, dict):
            return data
        if isinstance(data, int):
            return {"pid": data}
    except json.JSONDecodeError:
        pass
    try:
        return {"pid": int(raw)}
    except ValueError:
        return None


def _listener_pids(port: int) -> set[int]:
    try:
        connections = psutil.net_connections(kind="tcp")
    except psutil.AccessDenied as exc:
        raise RuntimeError("cannot inspect TCP ports: psutil AccessDenied") from exc
    pids: set[int] = set()
    for conn in connections:
        if conn.status != psutil.CONN_LISTEN or not conn.pid:
            continue
        if getattr(conn.laddr, "port", None) == port:
            pids.add(conn.pid)
    return pids


def _tree(pid: int) -> list[psutil.Process]:
    proc = _process(pid)
    if proc is None:
        return []
    try:
        children = proc.children(recursive=True)
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        children = []
    return [*children, proc]


def _stop_pids(pids: set[int]) -> None:
    procs: dict[int, psutil.Process] = {}
    for pid in pids:
        for proc in _tree(pid):
            if proc.pid != os.getpid():
                procs[proc.pid] = proc
    if not procs:
        return
    for proc in procs.values():
        try:
            proc.terminate()
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    _, alive = psutil.wait_procs(list(procs.values()), timeout=5)
    for proc in alive:
        try:
            proc.kill()
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    print("stopped:", ", ".join(str(pid) for pid in sorted(procs)))


def _server_pids(port: int, extra: set[int] | None = None) -> set[int]:
    pids = {pid for pid in (extra or set()) if _is_server_process(_process(pid))}
    pids.update(pid for pid in _listener_pids(port) if _is_server_process(_process(pid)))
    return pids


def _wait_port_free(port: int, timeout: float = 10.0) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not _listener_pids(port):
            return
        time.sleep(0.1)
    raise RuntimeError(f"port {port} is still busy")


def stop_existing(port: int) -> None:
    data = _read_pid_file()
    extra: set[int] = set()
    if data and data.get("pid"):
        try:
            extra.add(int(data["pid"]))
        except (TypeError, ValueError):
            pass

    listeners = _listener_pids(port)
    unknown = []
    for pid in listeners:
        proc = _process(pid)
        if not _is_server_process(proc) and proc is not None:
            unknown.append(f"{pid} ({proc.name()})")
    if unknown:
        raise RuntimeError(f"port {port} is busy with another process: {', '.join(unknown)}")

    pids = _server_pids(port, extra)
    if pids:
        _stop_pids(pids)
        _wait_port_free(port)
    else:
        print("server is not running")
    PID_FILE.unlink(missing_ok=True)


def _log_tail(lines: int = 40) -> str:
    try:
        text = LOG_FILE.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return "(log is empty)"
    return "\n".join(text.splitlines()[-lines:]) or "(log is empty)"


def _wait_ready(url: str, proc: subprocess.Popen[bytes], timeout: float) -> None:
    deadline = time.monotonic() + timeout
    last_error = "no response"
    while time.monotonic() < deadline:
        code = proc.poll()
        if code is not None:
            raise RuntimeError(f"server exited with code {code}\n{_log_tail()}")
        try:
            with urllib.request.urlopen(url + "/admin/login.html", timeout=2) as resp:
                if resp.status in (200, 302):
                    return
        except Exception as exc:
            last_error = f"{type(exc).__name__}: {exc}"
        time.sleep(0.25)
    raise RuntimeError(f"server is not ready ({last_error})\n{_log_tail()}")


def _run_script(name: str, *args: str) -> None:
    env = os.environ.copy()
    env.setdefault("PYTHONIOENCODING", "utf-8")
    subprocess.run(
        [sys.executable, str(ROOT / "samples" / name), *args],
        check=True,
        cwd=ROOT,
        env=env,
    )


def start(args: argparse.Namespace) -> int:
    if args.reset and not args.fill:
        raise SystemExit("--reset requires --fill")

    stop_existing(args.port)
    if args.seed:
        _run_script("seed_demo.py")

    url = f"http://{args.host}:{args.port}"
    ready_url = f"http://127.0.0.1:{args.port}" if args.host in ("0.0.0.0", "::") else url
    env = os.environ.copy()
    env.update(
        {
            "DB_PATH": str(DB),
            "ASPNETCORE_URLS": url,
            "ASPNETCORE_ENVIRONMENT": "Production",
        }
    )

    cmd = ["dotnet", "run", "--project", str(CSPROJ), "-c", "Release", "--no-launch-profile"]
    if args.no_build:
        cmd.append("--no-build")

    creationflags = 0
    popen_kwargs: dict = {}
    if os.name == "nt":
        creationflags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
        if args.detach:
            creationflags |= getattr(subprocess, "DETACHED_PROCESS", 0)
            creationflags |= getattr(subprocess, "CREATE_NO_WINDOW", 0)
    elif args.detach:
        popen_kwargs["start_new_session"] = True

    log = open(LOG_FILE, "w", encoding="utf-8")
    proc = subprocess.Popen(
        cmd,
        cwd=ROOT,
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=log,
        stderr=subprocess.STDOUT,
        creationflags=creationflags,
        **popen_kwargs,
    )
    PID_FILE.write_text(
        json.dumps({"pid": proc.pid, "port": args.port, "url": url, "db": str(DB)}),
        encoding="utf-8",
    )
    print(f"pid {proc.pid}; log {LOG_FILE}")
    print(f"admin {url}/admin/")

    detached = False
    try:
        _wait_ready(ready_url, proc, args.timeout)
        print("ready", url)
        if args.fill:
            fill_args = [ready_url]
            if args.reset:
                fill_args.append("--reset")
            _run_script("fill_demo_answers.py", *fill_args)
        if GUESTS_JSON.exists():
            print("guest links:", GUESTS_JSON)
        if args.detach:
            detached = True
            print(f"detached; stop with: py samples/run_demo.py --stop --port {args.port}")
            return 0
        print("server is running; press Ctrl+C to stop")
        proc.wait()
        return 0
    except KeyboardInterrupt:
        print("stopping")
        return 130
    finally:
        if not detached:
            _stop_pids(_server_pids(args.port, {proc.pid}))
            PID_FILE.unlink(missing_ok=True)
            log.close()


def show_status(port: int) -> int:
    data = _read_pid_file()
    extra = set()
    if data and data.get("pid"):
        try:
            extra.add(int(data["pid"]))
        except (TypeError, ValueError):
            pass
    pids = _server_pids(port, extra)
    if not pids:
        print("server is not running")
        return 1
    for pid in sorted(pids):
        proc = _process(pid)
        if proc is not None:
            print(f"{pid}\t{proc.name()}\t{proc.status()}")
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/admin/login.html", timeout=2) as resp:
            print("http", resp.status)
    except Exception as exc:
        print("http", exc)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5199)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--seed", action="store_true", help="пересоздать demo.db")
    parser.add_argument("--fill", action="store_true", help="заполнить демо-ответы")
    parser.add_argument("--reset", action="store_true", help="очистить ответы перед заполнением")
    parser.add_argument("--stop", action="store_true")
    parser.add_argument("--status", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--detach", action="store_true", help="оставить сервер работать и выйти")
    parser.add_argument("--timeout", type=float, default=30.0)
    args = parser.parse_args()

    if args.stop:
        try:
            stop_existing(args.port)
        except RuntimeError as exc:
            print("error:", exc)
            return 1
        return 0
    if args.status:
        return show_status(args.port)

    try:
        return start(args)
    except RuntimeError as exc:
        print("error:", exc)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
