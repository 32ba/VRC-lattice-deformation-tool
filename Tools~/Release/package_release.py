"""Build and verify release archives from immutable Git bytes; never publishes."""
import argparse
import gzip
import hashlib
import io
import json
import platform
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile
import tempfile
import zipfile
import zlib


def selected(name):
    parts = PurePosixPath(name).parts
    return (not any(p.startswith('.') for p in parts)
            and parts[0] not in ('Tests', 'Tools~', 'Packages')
            and name not in ('Tests.meta', 'AGENTS.md', 'AGENTS.md.meta', 'CLAUDE.md', 'CLAUDE.md.meta')
            and not name.startswith('Docs~/Architecture/'))


def version_info(package):
    name, version = package['name'], package['version']
    if not re.fullmatch(r'[a-z0-9]+(?:[.-][a-z0-9]+)+', name):
        raise ValueError('Invalid package name')
    match = re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
                         r'(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?'
                         r'(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?', version)
    if not match or (match[4] and any(p.isdigit() and len(p) > 1 and p[0] == '0'
                                    for p in match[4].split('.'))):
        raise ValueError('Invalid semantic version')
    return name, version, bool(match[4])


def snapshot(commit):
    commit = subprocess.check_output(['git', 'rev-parse', '--verify', '--end-of-options',
                                      commit + '^{commit}'], text=True).strip()
    raw = subprocess.check_output(['git', 'archive', '--format=tar', commit])
    files = {}
    with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
        for item in archive:
            if not selected(item.name) or item.isdir():
                continue
            if not item.isfile():
                raise ValueError('Unsupported Git entry: ' + item.name)
            files[item.name] = archive.extractfile(item).read()
    return commit, files


def ignored(name):
    return any(p.endswith('~') for p in PurePosixPath(name).parts)


def unity_entries(files, package_name):
    """Match the pinned legacy exporter: GUID/{asset.meta,pathname[,asset]}."""
    entries, guids = {}, {}
    for name, data in sorted(files.items()):
        if ignored(name):
            continue  # Unity-ignored documentation/media are ZIP-only, as before.
        if not name.endswith('.meta'):
            if name + '.meta' not in files:
                raise ValueError('Missing meta: ' + name)
            continue
        matches = re.findall(rb'^guid: ([0-9a-f]{32})\s*$', data, re.MULTILINE)
        if len(matches) != 1 or len(re.findall(rb'^guid:', data, re.MULTILINE)) != 1:
            raise ValueError('Invalid GUID: ' + name)
        guid = matches[0].decode()
        if guid in guids:
            raise ValueError('Duplicate GUID: ' + name + ' / ' + guids[guid])
        guids[guid] = name
        asset = name[:-5]
        folder = re.search(rb'^folderAsset: yes\s*$', data, re.MULTILINE) is not None
        if folder:
            if asset in files:
                raise ValueError('Invalid folder meta: ' + name)
        elif asset not in files:
            raise ValueError('Orphan meta: ' + name)
        entries[guid + '/asset.meta'] = data
        entries[guid + '/pathname'] = ('Packages/' + package_name + '/' + asset).encode()
        if not folder:
            entries[guid + '/asset'] = files[asset]
    for name in files:
        if ignored(name):
            continue
        for parent in PurePosixPath(name).parents:
            if str(parent) != '.':
                metadata = files.get(str(parent) + '.meta', b'')
                if re.search(rb'^folderAsset: yes\s*$', metadata, re.MULTILINE) is None:
                    raise ValueError('Missing parent folder meta: ' + name)
    return entries


def write_archives(directory, stem, files, unity):
    with zipfile.ZipFile(directory / (stem + '.zip'), 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            item = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            item.create_system = 3
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data, compress_type=zipfile.ZIP_DEFLATED)
    with (directory / (stem + '.unitypackage')).open('wb') as stream:
        with gzip.GzipFile(filename='', fileobj=stream, mode='wb', mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode='w', format=tarfile.USTAR_FORMAT) as archive:
                for name, data in sorted(unity.items()):
                    item = tarfile.TarInfo(name)
                    item.size, item.mode, item.mtime = len(data), 0o644, 0
                    archive.addfile(item, io.BytesIO(data))


def verify_archives(directory, stem, files, unity):
    with zipfile.ZipFile(directory / (stem + '.zip')) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or set(names) != set(files):
            raise ValueError('ZIP inventory mismatch')
        for name in names:
            if archive.read(name) != files[name]:
                raise ValueError('ZIP content mismatch: ' + name)
    with tarfile.open(directory / (stem + '.unitypackage'), 'r:gz') as archive:
        members = archive.getmembers()
        names = [m.name for m in members]
        if len(names) != len(set(names)) or set(names) != set(unity):
            raise ValueError('UnityPackage inventory mismatch')
        for item in members:
            if not item.isfile() or archive.extractfile(item).read() != unity[item.name]:
                raise ValueError('UnityPackage content/type mismatch: ' + item.name)


def build(commit, output):
    commit, files = snapshot(commit)
    name, version, prerelease = version_info(json.loads(files['package.json']))
    unity = unity_entries(files, name)
    stem = name + '-' + version
    output = Path(output).resolve()
    if output.exists():
        raise ValueError('Output must be a new directory: ' + str(output))
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='ldt-release-', dir=output.parent) as temporary:
        stage = Path(temporary) / 'result'
        stage.mkdir()
        write_archives(stage, stem, files, unity)
        verify_archives(stage, stem, files, unity)
        (stage / 'package.json').write_bytes(files['package.json'])
        artifacts = [{"file": p.name, "bytes": p.stat().st_size,
                      "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(stage.iterdir())]
        report = dict(schemaVersion=1, sourceCommit=commit, name=name, version=version,
                      buildEnvironment=dict(python=platform.python_version(),
                                            pythonImplementation=platform.python_implementation(),
                                            zlib=zlib.ZLIB_RUNTIME_VERSION),
                      prerelease=prerelease, verified=True, unityImportVerified=False,
                      zipFiles=len(files), unityAssets=sum(n.endswith('/asset') for n in unity),
                      unityGuids=sum(n.endswith('/pathname') for n in unity),
                      zipOnlyIgnoredFiles=sorted(n for n in files if ignored(n)), artifacts=artifacts,
                      sources=[dict(path=n, sha256=hashlib.sha256(d).hexdigest()) for n, d in sorted(files.items())])
        (stage / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
        stage.rename(output)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--commit', default='HEAD')
    parser.add_argument('--output', required=True)
    parser.add_argument('--github-output', type=Path)
    args = parser.parse_args()
    report = build(args.commit, args.output)
    if args.github_output:
        stem = report['name'] + '-' + report['version']
        with args.github_output.open('a', encoding='utf-8', newline='\n') as out:
            for key, value in dict(version=report['version'], prerelease=str(report['prerelease']).lower(),
                                   zipFile=stem + '.zip', unityPackage=stem + '.unitypackage').items():
                out.write(key + '=' + value + '\n')
    print(json.dumps({k: report[k] for k in ('sourceCommit', 'version', 'prerelease', 'zipFiles', 'unityGuids', 'verified')}))


if __name__ == '__main__':
    main()
