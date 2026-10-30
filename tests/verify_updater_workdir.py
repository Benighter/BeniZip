"""Reproduce the Windows folder lock and verify the real updater launch settings."""

import os
import subprocess
import sys
import time
import uuid
from pathlib import Path


def wait_for(path, timeout=15):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        if path.exists():
            return
        time.sleep(0.05)
    raise TimeoutError(str(path))


# The production launch configuration passes four arguments. In this test the
# first is this script, so Python runs it as the harmless stand-in helper.
if len(sys.argv) == 4 and sys.argv[3] == "8675309":
    marker = Path(sys.argv[1])
    (marker / "started.txt").write_text(os.getcwd(), encoding="utf-8")
    wait_for(marker / "stop.txt", timeout=45)
    (marker / "finished.txt").write_text("done", encoding="utf-8")
    sys.exit(0)

root = Path(__file__).resolve().parents[1]
work = root / ".work" / "update-verification" / ("cwd-" + uuid.uuid4().hex)
work.mkdir(parents=True)

# Prove that the actual Windows lock has the same observable error as reported.
legacy = work / "legacy-install"
legacy.mkdir()
holder = subprocess.Popen([sys.executable, "-u", "-c", "print('ready', flush=True); input()"],
                          cwd=legacy, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
try:
    assert holder.stdout.readline().strip() == "ready"
    try:
        legacy.rename(work / "legacy-moved")
        raise AssertionError("Windows did not lock a process's working directory")
    except OSError as error:
        assert error.winerror == 32, error
finally:
    holder.stdin.write("\n")
    holder.stdin.flush()
    holder.wait(timeout=10)

# Start a parent in a mock installation folder, then have the real launch
# factory start a long-lived helper. Once the parent exits, the mock folder
# must be movable while the helper continues running.
install = work / "installation"
install.mkdir()
markers = work / "markers"
markers.mkdir()
cli = root / "build" / "app-0.2.6.0" / "BeniZip.Cli.exe"
env = os.environ.copy()
env.update({"BENIZIP_TEST_CLI": str(cli), "BENIZIP_TEST_PYTHON": sys.executable,
            "BENIZIP_TEST_SCRIPT": str(Path(__file__).resolve()),
            "BENIZIP_TEST_MARKERS": str(markers), "BENIZIP_TEST_INSTALL": str(install)})
parent = r"""
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom($env:BENIZIP_TEST_CLI)
$method=[BeniZip.Updates].GetMethod('UpdateHelperStartInfo',[Reflection.BindingFlags]'Static,NonPublic')
$info=$method.Invoke($null,@($env:BENIZIP_TEST_PYTHON,$env:BENIZIP_TEST_SCRIPT,$env:BENIZIP_TEST_MARKERS,$env:BENIZIP_TEST_INSTALL,[int]8675309))
$info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($info)
Write-Output $process.Id
"""
started = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", parent],
                         cwd=install, env=env, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL, timeout=20)
assert started.returncode == 0, started.returncode
try:
    wait_for(markers / "started.txt")
    helper_cwd = Path((markers / "started.txt").read_text(encoding="utf-8")).resolve()
    assert helper_cwd != install.resolve(), helper_cwd
    install.rename(work / "installation-moved")
    print("Updater can replace an installation launched from its own folder: PASS")
finally:
    (markers / "stop.txt").write_text("stop", encoding="utf-8")
    if (markers / "started.txt").exists():
        wait_for(markers / "finished.txt")
