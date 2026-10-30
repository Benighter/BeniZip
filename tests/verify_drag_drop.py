"""Verify BeniZip virtual-file transfers against the Windows Shell using synthetic data."""
from pathlib import Path
import argparse
import json
import shutil
import struct
import subprocess
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--runtime', type=Path, default=ROOT / 'build' / 'app-0.2.6.0')
args = parser.parse_args()
runtime = args.runtime.resolve()
work = ROOT / '.work' / 'drag-regression' / ('run-' + str(time.time_ns()))
work.mkdir(parents=True)
shutil.copy2(runtime / 'BeniZip.Cli.exe', work / 'BeniZip.Cli.exe')
shutil.copytree(runtime / 'engine', work / 'engine')
sample = work / 'sample.zip'
with zipfile.ZipFile(sample, 'w', zipfile.ZIP_DEFLATED) as archive:
    archive.writestr('hello.txt', 'BeniZip single file drag test\n')
    archive.writestr('folder/inside.txt', 'BeniZip nested file drag test\n')
    archive.writestr('folder/empty/', '')
    archive.writestr('second.txt', 'BeniZip second file drag test\n')

(work / 'secret.txt').write_text('Encrypted drag test\n', encoding='utf-8', newline='')
encrypted = work / 'encrypted.zip'
subprocess.run([str(work / 'engine' / '7z.exe'), 'a', '-tzip', '-mem=AES256',
                '-pdrag-test-password', str(encrypted), 'secret.txt'], cwd=work,
               check=True, capture_output=True)

damaged = work / 'damaged.zip'
with zipfile.ZipFile(damaged, 'w', zipfile.ZIP_STORED) as archive:
    archive.writestr('damaged.txt', 'This member will be deliberately corrupted.')
data = bytearray(damaged.read_bytes())
name_len, extra_len = struct.unpack_from('<HH', data, 26)
data[30 + name_len + extra_len] ^= 1
damaged.write_bytes(data)
stale = work / 'stale.zip'
shutil.copy2(sample, stale)
large = work / 'large.zip'
with zipfile.ZipFile(large, 'w', zipfile.ZIP_DEFLATED) as archive:
    with archive.open('large.bin', 'w') as member:
        for _ in range(32):
            member.write(b'k' * (1024 * 1024))

(work / 'rarfile.txt').write_text('RAR drag test\n', encoding='utf-8', newline='')
rar = work / 'sample.rar'
request = work / 'rar-request.json'
request.write_text(json.dumps({'action': 'create', 'format': 'RAR', 'level': 1,
                               'inputs': [str(work / 'rarfile.txt')], 'output': str(rar)}), encoding='utf-8')
result = subprocess.run([str(work / 'BeniZip.Cli.exe'), '--cli', str(request)],
                        capture_output=True, text=True, encoding='utf-8', timeout=120)
assert result.returncode == 0 and json.loads(result.stdout)['ok'], result.stdout + result.stderr

(work / 'protected-rarfile.txt').write_text('Protected RAR drag test\n', encoding='utf-8', newline='')
protected_rar = work / 'protected.rar'
request.write_text(json.dumps({'action': 'create', 'format': 'RAR', 'level': 1,
                               'password': 'drag-test-password', 'hideNames': True,
                               'inputs': [str(work / 'protected-rarfile.txt')],
                               'output': str(protected_rar)}), encoding='utf-8')
result = subprocess.run([str(work / 'BeniZip.Cli.exe'), '--cli', str(request)],
                        capture_output=True, text=True, encoding='utf-8', timeout=120)
assert result.returncode == 0 and json.loads(result.stdout)['ok'], result.stdout + result.stderr

compiler = Path(r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe')
runner = work / 'DragDropIntegration.exe'
subprocess.run([str(compiler), '/nologo', '/platform:x64', '/langversion:5',
                '/reference:' + str(runtime / 'BeniZip.Cli.exe'), '/out:' + str(runner),
                str(ROOT / 'tests' / 'DragDropIntegration.cs')], check=True)
subprocess.run([str(runner), str(sample), str(work / 'shell-results'), str(encrypted),
                str(damaged), str(stale), str(rar), str(large), str(protected_rar)], check=True, timeout=180)
