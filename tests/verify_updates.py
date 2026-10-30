"""Verify the actual release manifest/package and reject tampering through the production CLI."""
from pathlib import Path
import base64
import json
import os
import subprocess
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--version', default='0.2.4.0')
parser.add_argument('--release-dir', type=Path)
parser.add_argument('--report', type=Path)
args = parser.parse_args()

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / '.work' / 'update-verification'
WORK.mkdir(parents=True, exist_ok=True)
CLI = ROOT / 'build' / ('app-' + args.version) / 'BeniZip.Cli.exe'
CHANNEL = args.release_dir or Path(os.environ['LOCALAPPDATA']) / 'BeniZip' / 'releases'
manifest = CHANNEL / 'manifest.json'
package = next(CHANNEL.glob('BeniZip-*-win-x64.zip')) if args.release_dir else CHANNEL / ('BeniZip-' + args.version + '-win-x64.zip')
checks = []

def run(name, request, expected):
    path = WORK / 'request.json'
    path.write_text(json.dumps(request), encoding='utf-8')
    process = subprocess.run([str(CLI), '--cli', str(path)], capture_output=True, encoding='utf-8', timeout=30)
    result = json.loads(process.stdout)
    assert result['ok'] is expected, (name, result)
    checks.append({'check': name, 'passed': True})

run('Valid pinned-key signed manifest accepted', {'action': 'verify-update', 'archive': str(manifest)}, True)
run('Actual immutable package checksum and size accepted', {'action': 'verify-update-package', 'archive': str(manifest), 'output': str(package)}, True)
envelope = json.loads(manifest.read_text(encoding='utf-8'))
payload = json.loads(base64.b64decode(envelope['payload']))
payload['notes'] += ' altered'
altered = dict(envelope)
altered['payload'] = base64.b64encode(json.dumps(payload).encode()).decode()
bad_manifest = WORK / 'tampered-payload.json'
bad_manifest.write_text(json.dumps(altered), encoding='utf-8')
run('Tampered payload rejected', {'action': 'verify-update', 'archive': str(bad_manifest)}, False)
altered = dict(envelope)
signature = bytearray(base64.b64decode(altered['signature']))
signature[0] ^= 0x40
altered['signature'] = base64.b64encode(signature).decode()
bad_manifest = WORK / 'tampered-signature.json'
bad_manifest.write_text(json.dumps(altered), encoding='utf-8')
run('Tampered signature rejected', {'action': 'verify-update', 'archive': str(bad_manifest)}, False)
data = bytearray(package.read_bytes())
data[len(data) // 2] ^= 0x40
bad_package = WORK / 'corrupt-package.zip'
bad_package.write_bytes(data)
run('Same-size corrupt package rejected', {'action': 'verify-update-package', 'archive': str(manifest), 'output': str(bad_package)}, False)
bad_package.write_bytes(data[:-100])
run('Incomplete package rejected', {'action': 'verify-update-package', 'archive': str(manifest), 'output': str(bad_package)}, False)
report = args.report or ROOT / 'analysis' / 'update-validation.json'
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps({'allPassed': True, 'checks': checks}, indent=2), encoding='utf-8')
print(f'{len(checks)} update signature/package checks passed')
