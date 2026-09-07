"""Copy hash-matching validation artifacts out of temporary Unity projects.

Original artifacts are never deleted or rewritten. Different historic versions of
one path remain separate by SHA-256. Missing/changed artifacts are reported rather
than silently archived under an obsolete hash. Run again after recovering a source
or adding a validation manifest; already preserved objects are verified and reused.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import tempfile


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    destination = args.archive.resolve()
    objects = destination / 'objects'
    objects.mkdir(parents=True, exist_ok=True)
    entries = {}
    for manifest in sorted((repository / 'Docs~/Architecture').glob('*validation.json')):
        data = json.loads(manifest.read_text(encoding='utf-8-sig'))
        for artifact in data.get('artifacts', []):
            if not isinstance(artifact, dict) or not {'path', 'sha256'} <= artifact.keys():
                continue
            key = (artifact['path'], artifact['sha256'])
            record = entries.setdefault(key, dict(sourcePath=key[0], expectedSha256=key[1], manifests=[]))
            record['manifests'].append(manifest.relative_to(repository).as_posix())
    copied_bytes = 0
    for record in entries.values():
        expected = record['expectedSha256']
        if len(expected) != 64 or any(c not in '0123456789abcdef' for c in expected):
            raise ValueError('Invalid expected hash: ' + expected)
        preserved = objects / expected
        if preserved.exists():
            if sha256(preserved) != expected:
                raise ValueError('Archive object is corrupt: ' + str(preserved))
            record.update(status='preserved', archivePath=str(preserved), bytes=preserved.stat().st_size)
            continue
        source = Path(record['sourcePath'])
        if not source.is_absolute():
            source = repository / source
        if not source.is_file():
            record.update(status='source-missing')
            continue
        actual = sha256(source)
        if actual != expected:
            record.update(status='source-changed', observedSha256=actual)
            continue
        # An independently verified temporary copy becomes visible as an object only
        # after its complete content matches the historical manifest.
        with tempfile.NamedTemporaryFile(dir=objects, suffix='.partial', delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            shutil.copyfile(source, temporary_path)
            if sha256(temporary_path) != expected:
                raise ValueError('Source changed during copy: ' + str(source))
            temporary_path.replace(preserved)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()
        record.update(status='preserved', archivePath=str(preserved), bytes=preserved.stat().st_size)
        copied_bytes += preserved.stat().st_size
    statuses = {}
    for entry in entries.values():
        statuses[entry['status']] = statuses.get(entry['status'], 0) + 1
    result = dict(schemaVersion=1, generatedUtc=datetime.now(timezone.utc).isoformat(),
                  archive=str(destination), complete=all(e['status'] == 'preserved' for e in entries.values()),
                  copiedBytes=copied_bytes, statuses=statuses, artifacts=list(entries.values()))
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key != 'artifacts'}))


if __name__ == '__main__':
    main()
