"""Black-box archive compatibility, integrity and performance checks using synthetic data."""
from pathlib import Path
import argparse
import gzip
import hashlib
import io
import json
import os
import random
import statistics
import struct
import subprocess
import tarfile
import time
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / '.work' / 'verification' / ('run-' + str(time.time_ns()))
RESULTS = []
CLI = None
BENCHMARK_REPORT = ROOT / 'analysis' / 'benchmarks.json'

def digest(path):
    with open(path, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def cli(request, expect=True):
    request_path = WORK / 'request.json'
    request_path.write_text(json.dumps(request, ensure_ascii=False), encoding='utf-8')
    run = subprocess.run([str(CLI), '--cli', str(request_path)], capture_output=True, text=True, encoding='utf-8', timeout=180)
    result = json.loads(run.stdout.strip())
    if result['ok'] != expect:
        raise AssertionError(f"{request['action']} unexpected result: {result}")
    return result

def check(name, function):
    started = time.perf_counter()
    result = function()
    RESULTS.append({'check': name, 'passed': True, 'seconds': round(time.perf_counter() - started, 3), 'detail': result})
    print(name + ': PASS', flush=True)

def files_match(source, destination):
    source_files = {str(p.relative_to(source)): digest(p) for p in source.rglob('*') if p.is_file()}
    destination_files = {str(p.relative_to(destination)): digest(p) for p in destination.rglob('*') if p.is_file()}
    assert source_files == destination_files, 'Extracted file names or SHA-256 hashes differ'
    return len(source_files)

def main():
    global CLI, BENCHMARK_REPORT
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', default=str(ROOT / 'build' / 'app-0.2.2.0'))
    parser.add_argument('--skip-rar', action='store_true', help='Skip checks requiring a separately installed licensed RAR helper')
    parser.add_argument('--bench', action='store_true')
    parser.add_argument('--report', default=str(ROOT / 'analysis' / 'verification.json'))
    parser.add_argument('--benchmark-report', default=str(BENCHMARK_REPORT))
    parser.add_argument('--cancel-check', action='store_true', help='Require the cancelAfterMs CLI field')
    parser.add_argument('--engine-probes', action='store_true', help='Use focused EngineRunner-only probes')
    args = parser.parse_args()
    BENCHMARK_REPORT = Path(args.benchmark_report)
    CLI = Path(args.runtime) / 'BeniZip.Cli.exe'
    WORK.mkdir(parents=True, exist_ok=True)
    print('Fixtures: ' + str(WORK), flush=True)
    source = WORK / 'source'
    source.mkdir(exist_ok=True)
    (source / 'Documents').mkdir(exist_ok=True)
    (source / 'données 日本語').mkdir(exist_ok=True)
    (source / 'empty folder').mkdir(exist_ok=True)
    (source / 'Documents' / 'Read me.txt').write_text('BeniZip verification\nArchive preview and Unicode.\n', encoding='utf-8')
    (source / 'données 日本語' / '[café] @notes.txt').write_text('Fichiers de test · 日本語 · Bennet\n' * 1500, encoding='utf-8')
    (source / 'Documents' / 'metadata.json').write_text(json.dumps({'hello': 'Bennet', 'purpose': 'synthetic QA'}, indent=2), encoding='utf-8')
    (source / 'payload.bin').write_bytes(random.Random(20261019).randbytes(3 * 1024 * 1024))
    zip_path = WORK / 'compatibility.zip'
    def zip_roundtrip():
        cli({'action': 'create', 'inputs': [str(source)], 'output': str(zip_path), 'format': 'ZIP', 'level': 1})
        listing = cli({'action': 'list', 'archive': str(zip_path), 'details': True})
        assert any('日本語' in entry['Path'] for entry in listing['result']['Items'])
        destination = WORK / 'zip-extracted'
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination)})
        cli({'action': 'test', 'archive': str(zip_path)})
        assert (destination / 'source' / 'empty folder').is_dir()
        return {'files': files_match(source, destination / 'source')}
    check('ZIP create / browse / extract / CRC test / Unicode / empty folder', zip_roundtrip)

    def format_roundtrip(fmt, password=None, hide=False, split=None):
        archive = WORK / ('compatibility-' + fmt + ('-encrypted' if password else '') + ('-split' if split else '') + '.' + fmt.lower())
        cli({'action': 'create', 'inputs': [str(source)], 'output': str(archive), 'format': fmt, 'level': 1, 'password': password, 'hideNames': hide, 'split': split})
        first = archive
        if split:
            candidates = sorted(p for p in WORK.glob(archive.stem + '*') if p.is_file())
            first = next((p for p in candidates if p.name.endswith('.001') or p.name.endswith('.part1.rar')), candidates[0])
            assert len(candidates) >= 2
        destination = WORK / (archive.stem + '-extracted')
        if password:
            cli({'action': 'extract', 'archive': str(first), 'destination': str(destination), 'password': 'wrong-password'}, expect=False)
            assert not destination.exists() or not list(destination.rglob('*'))
        cli({'action': 'extract', 'archive': str(first), 'destination': str(destination), 'password': password})
        cli({'action': 'test', 'archive': str(first), 'password': password})
        return {'files': files_match(source, destination / 'source'), 'firstVolume': first.name}
    check('7z round trip', lambda: format_roundtrip('7Z'))
    if not args.skip_rar:
        check('RAR5 round trip using installed RAR helper', lambda: format_roundtrip('RAR'))
    check('AES ZIP correct / incorrect password', lambda: format_roundtrip('ZIP', 'test-password'))
    check('Encrypted-header 7z correct / incorrect password', lambda: format_roundtrip('7Z', 'test-password', True))
    if not args.skip_rar:
        check('Encrypted-header RAR correct / incorrect password', lambda: format_roundtrip('RAR', 'test-password', True))
    check('Split ZIP volumes', lambda: format_roundtrip('ZIP', split='1m'))
    if not args.skip_rar:
        check('Split RAR volumes', lambda: format_roundtrip('RAR', split='1m'))
    check('Split 7z volumes', lambda: format_roundtrip('7Z', split='1m'))

    def selected():
        destination = WORK / 'selected'
        target = 'source/données 日本語/[café] @notes.txt'
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'selection': [target]})
        assert [str(p.relative_to(destination)).replace('\\', '/') for p in destination.rglob('*') if p.is_file()] == [target]
        assert digest(destination / target) == digest(source / 'données 日本語' / '[café] @notes.txt')
        return {'selectedFiles': 1}
    check('Literal member selection with brackets, @, spaces and Unicode', selected)

    def collisions():
        destination = WORK / 'collisions'
        file = destination / 'source' / 'Documents' / 'Read me.txt'
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text('KEEP ORIGINAL', encoding='utf-8')
        target = 'source/Documents/Read me.txt'
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'selection': [target], 'collision': 'Rename'})
        assert file.read_text() == 'KEEP ORIGINAL'
        assert (file.parent / 'Read me (2).txt').is_file()
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'selection': [target], 'collision': 'Skip'})
        assert file.read_text() == 'KEEP ORIGINAL'
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'selection': [target], 'collision': 'Overwrite'})
        assert digest(file) == digest(source / 'Documents' / 'Read me.txt')
        return {'policies': ['Rename', 'Skip', 'Overwrite']}
    check('Existing-file rename / skip / explicit overwrite', collisions)

    def unsafe_names():
        rejected = []
        for ordinal, name in enumerate(['../escape.txt', 'nested/../../escape.txt', '..\\escape.txt', 'C:/escape.txt', '/escape.txt', 'file.txt:stream', 'CON.txt', 'trailing. /file']):
            unsafe = WORK / f'unsafe-{ordinal}.zip'
            with zipfile.ZipFile(unsafe, 'w') as archive:
                archive.writestr(name, b'must never escape')
            result = cli({'action': 'extract', 'archive': str(unsafe), 'destination': str(WORK / f'unsafe-output-{ordinal}')}, expect=False)
            assert 'Unsafe' in result['error']
            rejected.append(name)
        unsafe = WORK / 'symlink.zip'
        with zipfile.ZipFile(unsafe, 'w') as archive:
            info = zipfile.ZipInfo('escape-link')
            info.create_system = 3
            info.external_attr = (0o120777 << 16)
            archive.writestr(info, '../elsewhere')
        result = cli({'action': 'extract', 'archive': str(unsafe), 'destination': str(WORK / 'symlink-output')}, expect=False)
        assert 'links' in result['error']
        assert not (WORK / 'escape.txt').exists()
        return {'unsafeNamesRejected': len(rejected), 'symlinkRejected': True}
    check('Traversal / absolute paths / ADS / reserved names / symbolic links rejected', unsafe_names)

    def corrupt_archive():
        broken = WORK / 'corrupt.zip'
        with zipfile.ZipFile(broken, 'w', zipfile.ZIP_STORED) as archive:
            archive.writestr('payload.bin', os.urandom(20000))
        data = bytearray(broken.read_bytes())
        data[100] ^= 0x35
        broken.write_bytes(data)
        destination = WORK / 'corrupt-output'
        cli({'action': 'extract', 'archive': str(broken), 'destination': str(destination)}, expect=False)
        cli({'action': 'test', 'archive': str(broken)}, expect=False)
        assert not destination.exists() or not list(destination.rglob('*'))
        return {'partialFilesPublished': 0}
    check('Corrupt ZIP CRC rejected without publishing partial files', corrupt_archive)

    def edit():
        extra = WORK / 'added.txt'
        extra.write_text('Added after creation', encoding='utf-8')
        cli({'action': 'add', 'archive': str(zip_path), 'inputs': [str(extra)]})
        with zipfile.ZipFile(zip_path) as updated:
            assert updated.read('added.txt') == extra.read_bytes()
            assert not (updated.getinfo('added.txt').flag_bits & 1), 'Plain additions must stay unencrypted'
        listing = cli({'action': 'list', 'archive': str(zip_path), 'details': True})['result']['Items']
        assert any(e['Path'] == 'added.txt' for e in listing)
        cli({'action': 'delete', 'archive': str(zip_path), 'selection': ['added.txt']})
        listing = cli({'action': 'list', 'archive': str(zip_path), 'details': True})['result']['Items']
        assert all(e['Path'] != 'added.txt' for e in listing)
        cli({'action': 'test', 'archive': str(zip_path)})
        return {'addedThenRemoved': True}
    check('Archive add / delete preserve archive integrity', edit)
    additional_checks(source, zip_path, args)
    if args.bench:
        benchmark()
    report = {'runtime': str(CLI), 'checks': RESULTS, 'allPassed': True, 'createdUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text(json.dumps(report, indent=2), encoding='utf-8')

def additional_checks(source, zip_path, args):
    seven = Path(CLI).parent / 'engine' / '7z.exe'

    def tar_and_gzip():
        archive = WORK / 'leading-dot.tar'
        expected = {'folder/Read me.txt': b'Normal TAR relative path\n', 'folder/nested/data.bin': bytes(range(256))}
        with tarfile.open(archive, 'w', format=tarfile.PAX_FORMAT) as tar:
            root = tarfile.TarInfo('./')
            root.type = tarfile.DIRTYPE
            tar.addfile(root)
            for folder in ['./folder/', './folder/nested/']:
                info = tarfile.TarInfo(folder)
                info.type = tarfile.DIRTYPE
                tar.addfile(info)
            for name, payload in expected.items():
                info = tarfile.TarInfo('./' + name)
                info.size = len(payload)
                tar.addfile(info, io.BytesIO(payload))
        listing = cli({'action': 'list', 'archive': str(archive), 'details': True})['result']['Items']
        assert all(not item['Path'].startswith('./') for item in listing)
        destination = WORK / 'tar-extracted'
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)})
        for name, payload in expected.items():
            assert (destination / name).read_bytes() == payload
        selected = WORK / 'tar-selected'
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(selected), 'selection': ['folder/nested']})
        assert [p.relative_to(selected).as_posix() for p in selected.rglob('*') if p.is_file()] == ['folder/nested/data.bin']
        archive = WORK / 'gzip-payload.txt.gz'
        payload = b'Gzip stream round trip\n' * 4000
        with archive.open('wb') as stream:
            with gzip.GzipFile(filename='gzip-payload.txt', mode='wb', fileobj=stream) as compressed:
                compressed.write(payload)
        destination = WORK / 'gzip-extracted'
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)})
        assert (destination / 'gzip-payload.txt').read_bytes() == payload
        cli({'action': 'test', 'archive': str(archive)})
        return {'tarLeadingDotNormalized': True, 'tarFolderSelection': True, 'gzipBytes': len(payload)}
    check('Normal TAR ./ paths / selected folder / gzip stream', tar_and_gzip)

    def ambiguities():
        cases = [('same.txt', 'same.txt'), ('Case.txt', 'case.txt'), ('./normal.txt', 'normal.txt'), ('file', 'file/child.txt')]
        for ordinal, names in enumerate(cases):
            archive = WORK / f'ambiguous-{ordinal}.zip'
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(archive, 'w') as zipped:
                    for name in names:
                        zipped.writestr(name, name)
            destination = WORK / f'ambiguous-output-{ordinal}'
            cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)}, expect=False)
            assert not destination.exists()
        for kind in [tarfile.SYMTYPE, tarfile.LNKTYPE]:
            archive = WORK / ('tar-link-' + kind.decode('ascii') + '.tar')
            with tarfile.open(archive, 'w') as tar:
                info = tarfile.TarInfo('escape-link')
                info.type, info.linkname = kind, '../outside'
                tar.addfile(info)
            cli({'action': 'extract', 'archive': str(archive), 'destination': str(WORK / ('tar-link-output-' + kind.decode('ascii')))}, expect=False)
        return {'ambiguousArchivesRejected': len(cases), 'tarSymbolicAndHardLinksBlocked': True}
    check('Duplicate / case-ambiguous / file-folder paths and TAR links rejected', ambiguities)

    def folder_selection_and_merge():
        destination = WORK / 'folder-selection'
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'selection': ['source/Documents']})
        assert files_match(source / 'Documents', destination / 'source' / 'Documents') == 2
        assert not (destination / 'source' / 'payload.bin').exists()
        (destination / 'keeper.txt').write_bytes(b'preserve existing unrelated data')
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(destination), 'collision': 'Overwrite'})
        assert (destination / 'keeper.txt').read_bytes() == b'preserve existing unrelated data'
        assert files_match(source, destination / 'source') == 4
        conflict = WORK / 'folder-conflict'
        conflict.mkdir()
        (conflict / 'source').write_text('keeper', encoding='utf-8')
        cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(conflict)}, expect=False)
        assert (conflict / 'source').read_text() == 'keeper'
        return {'selectedFolderFiles': 2, 'unrelatedDataPreserved': True, 'fileFolderConflictRejected': True}
    check('Selected folders / merge preserves unrelated files / folder collision preflight', folder_selection_and_merge)

    def source_protection():
        parent = WORK / 'source-protection'
        parent.mkdir()
        archive = parent / 'protected.zip'
        with zipfile.ZipFile(archive, 'w') as zipped:
            zipped.writestr('new.txt', 'must not publish before preflight')
            zipped.writestr('protected.zip', 'must not replace the source archive')
        before = digest(archive)
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(parent), 'collision': 'Overwrite'}, expect=False)
        assert digest(archive) == before and not (parent / 'new.txt').exists()
        return {'sourceArchiveUnchanged': True, 'filesPublishedBeforePreflightFailure': 0}
    check('Extraction cannot replace its source archive; full preflight precedes publication', source_protection)

    def junction_protection():
        outside = WORK / 'junction-outside'
        outside.mkdir()
        (outside / 'keeper.txt').write_text('outside keeper', encoding='utf-8')
        junction = WORK / 'junction-destination'
        subprocess.run(['cmd.exe', '/c', 'mklink', '/J', str(junction), str(outside)], check=True, capture_output=True)
        try:
            cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(junction)}, expect=False)
        finally:
            os.rmdir(junction)
        merge = WORK / 'junction-merge'
        merge.mkdir()
        junction = merge / 'source'
        subprocess.run(['cmd.exe', '/c', 'mklink', '/J', str(junction), str(outside)], check=True, capture_output=True)
        try:
            cli({'action': 'extract', 'archive': str(zip_path), 'destination': str(merge)}, expect=False)
        finally:
            os.rmdir(junction)
        assert [p.name for p in outside.iterdir()] == ['keeper.txt']
        assert (outside / 'keeper.txt').read_text() == 'outside keeper'
        return {'junctionDestinationBlocked': True, 'junctionMergeAncestorBlocked': True, 'outsideDataUnchanged': True}
    check('Junction destination and merge ancestor blocked without outside writes', junction_protection)

    def rar4_and_incomplete_volumes():
        rar = Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'WinRAR' / 'Rar.exe'
        archive = WORK / 'legacy-rar4.rar'
        subprocess.run([str(rar), 'a', '-ma4', '-m1', '-idq', '-y', str(archive), source.name], cwd=source.parent, check=True, capture_output=True, timeout=180)
        destination = WORK / 'rar4-extracted'
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)})
        assert files_match(source, destination / 'source') == 4
        archive = WORK / 'incomplete.7z'
        cli({'action': 'create', 'inputs': [str(source)], 'output': str(archive), 'format': '7Z', 'level': 1, 'split': '1m'})
        volumes = sorted(WORK.glob('incomplete.7z.*'))
        assert len(volumes) >= 2
        volumes[-1].unlink()  # Deliberately damage only this synthetic archive.
        destination = WORK / 'incomplete-output'
        cli({'action': 'extract', 'archive': str(volumes[0]), 'destination': str(destination)}, expect=False)
        assert not destination.exists()
        return {'rar4Files': 4, 'incompleteMultipartRejected': True}
    if not args.skip_rar:
        check('Legacy RAR4 extraction / incomplete multipart archive rejected', rar4_and_incomplete_volumes)

    def corrupt_seven_zip():
        payload = random.Random(82819).randbytes(150000)
        original = WORK / 'crc-payload.bin'
        original.write_bytes(payload)
        archive = WORK / 'corrupt.7z'
        subprocess.run([str(seven), 'a', '-t7z', '-mx=0', '-y', '-bso0', str(archive), str(original)], check=True, capture_output=True, timeout=180)
        data = bytearray(archive.read_bytes())
        offset = data.find(payload[:64])
        assert offset >= 0
        data[offset + 500] ^= 0x63
        archive.write_bytes(data)
        destination = WORK / 'corrupt-7z-output'
        cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)}, expect=False)
        cli({'action': 'test', 'archive': str(archive)}, expect=False)
        assert not destination.exists()
        return {'partialFilesPublished': 0}
    check('Corrupt 7z CRC rejected without publishing partial files', corrupt_seven_zip)

    if args.engine_probes:
        def identity_and_root():
            same = cli({'action': 'common-parent', 'inputs': [r'C:\first.txt', r'C:\folder\second.txt']})
            assert same['result']['parent'].lower() == 'c:\\'
            cli({'action': 'common-parent', 'inputs': [r'C:\first.txt', r'D:\second.txt']}, expect=False)
            archive = WORK / 'stale.zip'
            archive.write_bytes(zip_path.read_bytes())
            destination = WORK / 'stale-output'
            result = cli({'action': 'stale-index', 'archive': str(archive), 'destination': str(destination)}, expect=False)
            assert 'changed' in result['error'] and not destination.exists()
            return {'driveRootCommonParent': True, 'differentDrivesRejected': True, 'staleSourceRejected': True}
        check('Drive-root common parent / cross-drive rejection / changed-source rejection', identity_and_root)

    if args.cancel_check:
        def cancellation():
            folder = WORK / 'cancel-source'
            folder.mkdir()
            payload = random.Random(201019).randbytes(4096)
            for ordinal in range(4000):
                (folder / f'entry-{ordinal:05d}.bin').write_bytes(payload)
            archive = WORK / 'cancel.zip'
            subprocess.run([str(seven), 'a', '-tzip', '-mx=0', '-y', '-bso0', str(archive), str(folder)], check=True, capture_output=True, timeout=180)
            destination = WORK / 'cancel-extracted'
            result = cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination), 'cancelAfterMs': 200}, expect=False)
            assert result.get('cancelled') or 'cancel' in result['error'].lower()
            assert not destination.exists()
            output = WORK / 'cancel-created.7z'
            result = cli({'action': 'create', 'inputs': [str(folder)], 'output': str(output), 'format': '7Z', 'level': 9, 'cancelAfterMs': 200}, expect=False)
            assert result.get('cancelled') or 'cancel' in result['error'].lower()
            assert not output.exists()
            assert not list(WORK.glob('.benizip-partial-*'))
            cli({'action': 'test', 'archive': str(archive)})
            return {'extractionCancelledBeforePublication': True, 'creationCancelledWithoutOutput': True, 'runtimeStillWorks': True}
        check('Cancel extraction and creation cleanly; subsequent archive operation succeeds', cancellation)

def benchmark():
    benchmark_root = WORK / 'bench-source'
    benchmark_root.mkdir(exist_ok=True)
    rng = random.Random(20261019)
    many = benchmark_root / 'many-small-files'
    many.mkdir(exist_ok=True)
    chunk = rng.randbytes(8192)
    for i in range(2500):
        (many / f'file-{i:04d}.txt').write_bytes(f'Entry {i}\n'.encode() + chunk[:4096] + (b'archive benchmark\n' * 250))
    large = benchmark_root / 'large-files'
    large.mkdir(exist_ok=True)
    block = rng.randbytes(1024 * 1024)
    with (large / 'large.bin').open('wb') as stream:
        for i in range(96):
            stream.write(block)
    seven = Path(CLI).parent / 'engine' / '7z.exe'
    winrar = Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'WinRAR' / 'WinRAR.exe'
    measurements = []
    for label, source in [('many-small-files', many), ('large-files', large)]:
        archive = WORK / (label + '.zip')
        subprocess.run([str(seven), 'a', '-tzip', '-mx=1', '-y', '-bso0', str(archive), str(source)], check=True, capture_output=True, timeout=180)
        bins = {'BeniZip': [], 'WinRAR 5.40': []}
        for trial in range(3):
            for app in (['BeniZip', 'WinRAR 5.40'] if trial % 2 == 0 else ['WinRAR 5.40', 'BeniZip']):
                destination = WORK / f'bench-{label}-{trial}-{app.replace(" ", "-")}'
                started = time.perf_counter()
                if app == 'BeniZip':
                    result = cli({'action': 'extract', 'archive': str(archive), 'destination': str(destination)})
                else:
                    subprocess.run([str(winrar), 'x', '-inul', '-ibck', '-y', '-o+', str(archive), str(destination) + '\\'], check=True, timeout=180)
                bins[app].append(time.perf_counter() - started)
                files_match(source, destination / source.name)
        listing = [cli({'action': 'list', 'archive': str(archive)})['elapsedMs'] for _ in range(3)]
        b = statistics.median(bins['BeniZip'])
        w = statistics.median(bins['WinRAR 5.40'])
        measurements.append({'fixture': label, 'sourceFiles': len(list(source.rglob('*'))), 'unpackedBytes': sum(p.stat().st_size for p in source.rglob('*') if p.is_file()), 'archiveBytes': archive.stat().st_size, 'extractSeconds': bins, 'beniZipMedianSeconds': b, 'winrarMedianSeconds': w, 'winrarOverBeniZipRatio': w / b, 'beniZipIndexMedianMs': statistics.median(listing), 'hashesMatchedEveryRun': True})
        print(f'BENCH {label}: BeniZip {b:.3f}s, WinRAR {w:.3f}s; ratio {w/b:.2f}x', flush=True)
    BENCHMARK_REPORT.write_text(json.dumps({'method': '3 alternating-order warm-cache runs per engine; same ZIPs; full extracted SHA-256 equality; wall time includes app startup, listing and staging for BeniZip', 'results': measurements}, indent=2), encoding='utf-8')

if __name__ == '__main__':
    main()
