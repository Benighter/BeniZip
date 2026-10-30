"""Production-CLI edit regressions with independent encryption and data checks.

Only synthetic archives under .work/edit-regression are modified. No UI is used.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import random
import subprocess
import sys
import time
import zipfile


ROOT = Path(__file__).resolve().parents[1]
NO_WINDOW = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
PASSWORD = 'Synthetic-edit-check-19!'
WRONG_PASSWORD = 'Wrong-synthetic-edit-check!'


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def file_bytes(folder):
    return {p.relative_to(folder).as_posix(): p.read_bytes()
            for p in folder.rglob('*') if p.is_file()}


class EditingChecks:
    def __init__(self, runtime, work):
        self.runtime = runtime
        self.cli = runtime / 'BeniZip.Cli.exe'
        self.seven = runtime / 'engine' / '7z.exe'
        self.rar = Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'WinRAR' / 'Rar.exe'
        self.work = work
        self.sequence = 0

    @staticmethod
    def process(arguments, cwd=None):
        return subprocess.run([str(arg) for arg in arguments], cwd=cwd,
                              stdin=subprocess.DEVNULL, capture_output=True,
                              timeout=90, creationflags=NO_WINDOW)

    def request(self, action, expected=True, **fields):
        self.sequence += 1
        path = self.work / ('request-%03d.json' % self.sequence)
        request = {'action': action, **fields}
        path.write_text(json.dumps(request), encoding='utf-8')
        run = self.process([self.cli, '--cli', path])
        try:
            result = json.loads(run.stdout.decode('utf-8-sig').strip())
        except (ValueError, UnicodeDecodeError) as error:
            raise AssertionError('CLI returned invalid JSON for %s (exit %s)' %
                                 (action, run.returncode)) from error
        require(result.get('ok') is expected,
                '%s unexpected result: %s' % (action, result))
        require(run.returncode == (0 if expected else 2),
                '%s exit code did not match its result' % action)
        return result

    def seven_call(self, command, archive, password=None, extra=()):
        # -p- deliberately supplies an invalid password instead of allowing an
        # invisible interactive prompt; it does not supply the archive secret.
        args = [self.seven, command, '-y', '-sccUTF-8',
                '-p' + (password if password is not None else '-')]
        return self.process(args + list(extra) + ['--', archive])

    def independent_records(self, archive, password=None):
        run = self.seven_call('l', archive, password, ('-slt', '-ba'))
        require(run.returncode == 0, 'Independent archive listing failed')
        records, current = [], {}
        for line in run.stdout.decode('utf-8-sig').splitlines() + ['']:
            if not line:
                if 'Path' in current:
                    records.append(current)
                current = {}
            else:
                key, separator, value = line.partition(' = ')
                require(bool(separator), 'Unexpected independent listing output')
                current[key] = value
        return {row['Path'].replace('\\', '/'): row for row in records
                if row.get('Folder') != '+'}

    def independent_extract(self, archive, destination, expected, password=None):
        run = self.seven_call('x', archive, password,
                              ('-bso0', '-bse2', '-o' + str(destination)))
        require(run.returncode == 0, 'Independent extraction failed')
        require(file_bytes(destination) == expected,
                'Independent extraction returned different names or bytes')

    def create_fixture(self, case, format_name, protected):
        initial = case / 'initial'
        additions = case / 'additions'
        initial.mkdir(parents=True)
        additions.mkdir()
        original = initial / 'original.txt'
        added = additions / 'added.bin'
        original.write_bytes(b'Original archive member\n' * 1000)
        added.write_bytes(random.Random(20261019).randbytes(32768))
        archive = case / ('fixture.' + format_name.lower())
        if format_name == 'RAR':
            require(self.rar.is_file(), 'Installed RAR helper is required for this check')
            arguments = [self.rar, 'a', '-ma5', '-m1', '-idq', '-y']
            if protected:
                arguments.append('-hp' + PASSWORD)
        else:
            arguments = [self.seven, 'a', '-t' + format_name.lower(),
                         '-mx=1', '-y', '-bso0', '-bse2']
            if protected:
                arguments += ['-p' + PASSWORD,
                              '-mem=AES256' if format_name == 'ZIP' else '-mhe=on']
        run = self.process(arguments + [archive, original.name], cwd=initial)
        require(run.returncode == 0, 'Independent fixture creation failed')
        return archive, original, added

    @staticmethod
    def assert_zip_flags(archive, encrypted):
        with zipfile.ZipFile(archive) as zipped:
            files = [entry for entry in zipped.infolist() if not entry.is_dir()]
            require(bool(files), 'ZIP has no file entries')
            require(all(bool(entry.flag_bits & 1) is encrypted for entry in files),
                    'ZIP encryption flags do not match the expected protection')
            if not encrypted:
                # Reading through Python is independent of BeniZip and 7-Zip.
                return zipped.read('added.bin') if 'added.bin' in zipped.namelist() else None
        return None

    def plain_case(self, format_name):
        case = self.work / ('plain-' + format_name.lower())
        archive, original, added = self.create_fixture(case, format_name, False)
        expected = {original.name: original.read_bytes(), added.name: added.read_bytes()}
        self.request('add', archive=str(archive), inputs=[str(added)])
        records = self.independent_records(archive)
        require(set(records) == set(expected), 'Plain archive member names differ after Add')
        require(all(row.get('Encrypted') != '+' for row in records.values()),
                'Plain archive Add silently encrypted a member')
        if format_name == 'ZIP':
            require(self.assert_zip_flags(archive, False) == added.read_bytes(),
                    'Python could not read added plaintext ZIP bytes without a password')
        production_output = case / 'production-after-add'
        self.request('extract', archive=str(archive), destination=str(production_output))
        require(file_bytes(production_output) == expected,
                'Production extraction returned different added bytes')
        self.independent_extract(archive, case / 'independent-after-add', expected)
        self.request('delete', archive=str(archive), selection=[added.name])
        expected.pop(added.name)
        records = self.independent_records(archive)
        require(set(records) == set(expected), 'Delete did not remove only the added member')
        require(all(row.get('Encrypted') != '+' for row in records.values()),
                'Plain archive Delete changed protection')
        self.request('extract', archive=str(archive), destination=str(case / 'production-after-delete'))
        require(file_bytes(case / 'production-after-delete') == expected,
                'Production extraction changed the original member after Delete')
        self.independent_extract(archive, case / 'independent-after-delete', expected)
        self.request('test', archive=str(archive))
        return {'format': format_name, 'addedBytes': len(added.read_bytes()),
                'noEncryptionFlags': True, 'passwordNotNeeded': True,
                'productionAndIndependentBytesMatchedAfterAddAndDelete': True}

    def protected_case(self, format_name):
        case = self.work / ('protected-' + format_name.lower())
        archive, original, added = self.create_fixture(case, format_name, True)
        initial_hash = sha256(archive)
        attempts = [('missing', {}), ('empty', {'password': ''}),
                    ('wrong', {'password': WRONG_PASSWORD})]
        for label, fields in attempts:
            before = sha256(archive)
            self.request('add', expected=False, archive=str(archive),
                         inputs=[str(added)], **fields)
            require(sha256(archive) == before == initial_hash,
                    '%s password Add modified the protected source archive' % label)
        self.request('add', archive=str(archive), inputs=[str(added)], password=PASSWORD)
        expected = {original.name: original.read_bytes(), added.name: added.read_bytes()}
        records = self.independent_records(archive, PASSWORD)
        require(set(records) == set(expected), 'Protected member names differ after Add')
        require(all(row.get('Encrypted') == '+' for row in records.values()),
                'Protected archive Add left a member unencrypted')
        if format_name == 'ZIP':
            self.assert_zip_flags(archive, True)
        if format_name in ('7Z', 'RAR'):
            require(self.seven_call('l', archive, extra=('-slt', '-ba')).returncode != 0,
                    'Add removed existing archive-header encryption')
        after_add_hash = sha256(archive)
        for label, fields in [('missing', {}), ('wrong', {'password': WRONG_PASSWORD})]:
            output = case / ('new-member-' + label + '-password')
            self.request('extract', expected=False, archive=str(archive),
                         destination=str(output), selection=[added.name], **fields)
            require(not output.exists() or not file_bytes(output),
                    'Protected added data was published without the actual password')
            require(sha256(archive) == after_add_hash,
                    'Rejected extraction changed the protected archive')
        output = case / 'new-member-correct-password'
        self.request('extract', archive=str(archive), destination=str(output),
                     selection=[added.name], password=PASSWORD)
        require(file_bytes(output) == {added.name: added.read_bytes()},
                'Correct password did not recover the added bytes')
        self.independent_extract(archive, case / 'independent-after-add', expected, PASSWORD)
        self.request('delete', archive=str(archive), selection=[added.name], password=PASSWORD)
        expected.pop(added.name)
        records = self.independent_records(archive, PASSWORD)
        require(set(records) == set(expected) and all(row.get('Encrypted') == '+' for row in records.values()),
                'Protected Delete changed the remaining data or its encryption')
        if format_name in ('7Z', 'RAR'):
            require(self.seven_call('l', archive, extra=('-slt', '-ba')).returncode != 0,
                    'Delete removed existing archive-header encryption')
        self.independent_extract(archive, case / 'independent-after-delete', expected, PASSWORD)
        self.request('test', archive=str(archive), password=PASSWORD)
        return {'format': format_name, 'rejectedAddPasswords': [label for label, _ in attempts],
                'rejectedEditsPreservedSourceHash': True, 'addedMemberEncrypted': True,
                'headerEncryptionPreserved': format_name in ('7Z', 'RAR'),
                'missingAndWrongPasswordsCannotReadAddedData': True,
                'correctPasswordRecoveredAddedBytes': True,
                'remainingOriginalBytesAndEncryptionPreservedAfterDelete': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', default=str(ROOT / 'build' / 'app-0.2.4.0'))
    parser.add_argument('--skip-rar', action='store_true')
    parser.add_argument('--report', default=str(ROOT / '.work' / 'edit-regression' / 'latest-report.json'))
    args = parser.parse_args()
    runtime, report_path = Path(args.runtime).resolve(), Path(args.report).resolve()
    work = ROOT / '.work' / 'edit-regression' / ('run-' + str(time.time_ns()))
    work.mkdir(parents=True)
    checks = EditingChecks(runtime, work)
    require(checks.cli.is_file() and checks.seven.is_file(), 'Production CLI or its 7-Zip engine is missing')
    rows = []
    for format_name in (('ZIP', '7Z') if args.skip_rar else ('ZIP', '7Z', 'RAR')):
        for mode, function in [('plaintext', checks.plain_case), ('protected', checks.protected_case)]:
            name = format_name + ' ' + mode + ' Add / byte readback / Delete'
            try:
                detail = function(format_name)
                rows.append({'case': name, 'passed': True, 'detail': detail})
                print(name + ': PASS', flush=True)
            except Exception as error:
                rows.append({'case': name, 'passed': False, 'error': str(error)})
                print(name + ': FAIL: ' + str(error), flush=True)
    report = {'runtime': str(checks.cli), 'runtimeSha256': sha256(checks.cli),
              'work': str(work), 'createdUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'fixturesCreatedIndependently': True, 'uiUsed': False,
              'cases': rows, 'allPassed': all(row['passed'] for row in rows)}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('Report: ' + str(report_path), flush=True)
    return 0 if report['allPassed'] else 1


if __name__ == '__main__':
    sys.exit(main())
