"""Exercise BeniZip's built-in RAR creation and safe editing end to end."""

import argparse
import hashlib
import json
import random
import re
import subprocess
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--runtime", type=Path, default=ROOT / "build" / "app-0.2.3.0")
args = parser.parse_args()
CLI = args.runtime.resolve() / "BeniZip.Cli.exe"
SEVEN = args.runtime.resolve() / "engine" / "7z.exe"
WORK = ROOT / ".work" / "rar-regression" / ("run-" + str(time.time_ns()))
WORK.mkdir(parents=True)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def request(action, expected=True, **fields):
    path = WORK / "request.json"
    path.write_text(json.dumps({"action": action, **fields}, ensure_ascii=False), encoding="utf-8")
    process = subprocess.run([str(CLI), "--cli", str(path)], capture_output=True,
                             text=True, encoding="utf-8", timeout=120)
    result = json.loads(process.stdout)
    assert result["ok"] is expected, (action, result, process.stderr)
    assert process.returncode == (0 if expected else 2)
    return result


def extract(archive, destination, password=None):
    command = [str(SEVEN), "x", "-y", "-bso0", "-o" + str(destination)]
    if password:
        command.append("-p" + password)
    result = subprocess.run(command + [str(archive)], capture_output=True, timeout=120)
    assert result.returncode == 0, (result.stdout, result.stderr)
    return {path.relative_to(destination).as_posix(): digest(path)
            for path in destination.rglob("*") if path.is_file()}


source = WORK / "source"
(source / "docs").mkdir(parents=True)
(source / "empty").mkdir()
(source / "docs" / "hello.txt").write_text("Archive content\n" * 3000, encoding="utf-8")
(source / "unicode-世界.txt").write_text("Unicode contents", encoding="utf-8")
extra = WORK / "added.txt"
extra.write_text("added content", encoding="utf-8")

# The helper protocol must also work when Windows uses the UTF-8 console code
# page, where the default .NET StreamWriter may otherwise prepend a BOM.
utf8_archive = WORK / "utf8-console.rar"
utf8_request = WORK / "utf8-request.json"
utf8_request.write_text(json.dumps({"action": "create", "inputs": [str(extra)],
                                    "output": str(utf8_archive), "format": "RAR"}), encoding="utf-8")
command = "chcp 65001>nul && " + subprocess.list2cmdline([str(CLI), "--cli", str(utf8_request)])
utf8_run = subprocess.run(["cmd.exe", "/d", "/c", command], capture_output=True,
                          text=True, encoding="utf-8", timeout=120)
assert utf8_run.returncode == 0 and json.loads(utf8_run.stdout)["ok"], (utf8_run.stdout, utf8_run.stderr)
assert extract(utf8_archive, WORK / "utf8-extract")["added.txt"] == digest(extra)
print("RAR helper works with the UTF-8 Windows console code page: PASS", flush=True)

for password in (None, "Synthetic-Only!"):
    label = "protected" if password else "plain"
    archive = WORK / (label + ".rar")
    request("create", inputs=[str(source)], output=str(archive), format="RAR", level=5,
            password=password, hideNames=bool(password))
    initial = extract(archive, WORK / (label + "-initial"), password)
    assert initial["source/docs/hello.txt"] == digest(source / "docs" / "hello.txt")
    assert initial["source/unicode-世界.txt"] == digest(source / "unicode-世界.txt")
    request("add", archive=str(archive), inputs=[str(extra)], password=password)
    after_add = extract(archive, WORK / (label + "-added"), password)
    assert all(after_add[name] == value for name, value in initial.items())
    assert after_add["added.txt"] == digest(extra)
    before_refusal = digest(archive)
    request("add", expected=False, archive=str(archive), inputs=[str(extra)], password=password)
    assert digest(archive) == before_refusal
    if password:
        request("add", expected=False, archive=str(archive), inputs=[str(extra)], password="wrong")
        assert digest(archive) == before_refusal
    request("delete", archive=str(archive), selection=["source/docs"], password=password)
    after_delete = extract(archive, WORK / (label + "-deleted"), password)
    assert "source/docs/hello.txt" not in after_delete
    assert after_delete["source/unicode-世界.txt"] == initial["source/unicode-世界.txt"]
    assert after_delete["added.txt"] == digest(extra)
    if password:
        no_password = subprocess.run([str(SEVEN), "t", "-bso0", "-pwrong", str(archive)], capture_output=True)
        assert no_password.returncode != 0
        listing = subprocess.run([str(SEVEN), "l", "-slt", "-sccUTF-8", "-p" + password, str(archive)],
                                 capture_output=True, text=True, encoding="utf-8", check=True).stdout
        added_entry = next(record for record in listing.split("\n\n") if "Path = added.txt" in record)
        assert re.search(r"^Encrypted = \+\s*$", added_entry, re.MULTILINE), added_entry
    print(label + " RAR create, add, delete, contents and protection: PASS", flush=True)

fixture = ROOT / "tests" / "fixtures" / "rar5-default-external.rar"
assert digest(fixture) == "3029e7d1a03e9afaf9d5384caa929cf1fe10165e8b15e38a6e509364a300ad59"
external = WORK / "external.rar"
external.write_bytes(fixture.read_bytes())
original_hash = digest(external)
original_contents = extract(external, WORK / "external-before")
request("add", expected=False, archive=str(external), inputs=[str(extra)])
assert digest(external) == original_hash
request("add", archive=str(external), inputs=[str(extra)], allowRarRebuild=True)
assert digest(Path(str(external) + ".backup.rar")) == original_hash
after = extract(external, WORK / "external-after")
assert all(after[name] == value for name, value in original_contents.items())
assert after["added.txt"] == digest(extra)
print("Existing RAR5 refuses unsafe edits until approved; rebuild keeps a backup: PASS", flush=True)

split_source = WORK / "split.bin"
split_source.write_bytes(random.Random(42).randbytes(120_000))
request("create", inputs=[str(split_source)], output=str(WORK / "split.rar"),
        format="RAR", level=0, split="20k")
parts = sorted(WORK.glob("split.part*.rar"))
assert len(parts) > 1
check = subprocess.run([str(SEVEN), "t", "-bso0", str(parts[0])], capture_output=True)
assert check.returncode == 0, (check.stdout, check.stderr)
part_hashes = {part: digest(part) for part in parts}
request("add", expected=False, archive=str(parts[0]), inputs=[str(extra)], allowRarRebuild=True)
assert all(digest(part) == before for part, before in part_hashes.items())
print("Split RAR volumes independently verified: PASS", flush=True)

cancel_source = WORK / "cancel.bin"
cancel_source.write_bytes(random.Random(43).randbytes(4 * 1024 * 1024))
cancel_archive = WORK / "cancel.rar"
request("create", expected=False, inputs=[str(cancel_source)], output=str(cancel_archive),
        format="RAR", level=5, cancelAfterMs=5)
assert not cancel_archive.exists()
print("Cancelled RAR creation leaves no published archive: PASS", flush=True)
