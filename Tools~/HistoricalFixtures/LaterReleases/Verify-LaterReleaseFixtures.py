"""Verify later-release provenance, file identities and optional byte-identical regeneration.

Read-only. Run from any directory; --corpus and --compare name generated LaterReleases
directories, not Unity projects. This does not invoke candidate deformation code or
rewrite any golden output. The original 14-tag corpus is outside this tool's scope.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def meta_guid(tag, relative):
    identity = f'net.32ba.lattice-deformation-tool/historical-fixture-meta-guid/v1\n{tag}\n{relative}'
    return hashlib.sha256(identity.encode()).hexdigest()[:32]


def prefab_id(tag, relative, class_id, ordinal):
    identity = f'net.32ba.lattice-deformation-tool/historical-fixture-prefab-file-id/v1\n{tag}\n{relative}\n{class_id}\n{ordinal}'
    value = int.from_bytes(hashlib.sha256(identity.encode()).digest()[:8], 'big')
    return str((value & 0x3FFFFFFFFFFFFFFF) | 0x4000000000000000)


def verify(corpus, repository, releases):
    require(corpus.is_dir() and not corpus.is_symlink(), f'Not an ordinary corpus directory: {corpus}')
    require(sorted(p.name for p in corpus.iterdir() if p.is_dir()) == sorted(r['tag'] for r in releases), 'Release directory set differs')
    tool_names = [
        'Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs',
        'Tools~/HistoricalFixtures/LaterReleases/LaterReleaseMeshCapture.cs',
        'Tools~/HistoricalFixtures/LaterReleases/LaterReleaseFixtureGenerator.cs',
        'Tools~/HistoricalFixtures/LaterReleases/Generate-LaterReleaseFixtures.ps1',
    ]
    tools = {name: digest(repository / name) for name in tool_names}
    files = {}
    fixture_count = 0
    guids = set()
    for release in releases:
        tag = release['tag']
        directory = corpus / tag
        manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
        for key, expected in {'schemaVersion': 1, 'tag': tag, 'commitSha': release['commit'],
                              'packageVersion': release['packageVersion'], 'unityVersion': '2022.3.22f1',
                              'generationMode': 'unity-batchmode-tag-checkout',
                              'goldenOutputSource': 'historical-runtime-deform',
                              'metaGuidScheme': 'sha256-v1:tag/relative-asset-path',
                              'prefabFileIdScheme': 'sha256-v1:tag/relative-prefab/class/ordinal'}.items():
            require(manifest[key] == expected, f'{tag}: wrong {key}')
        require(len(manifest['tools']) == len(tools), f'{tag}: wrong tool count')
        require({tool['path']: tool['sha256'] for tool in manifest['tools']} == tools, f'{tag}: helper provenance mismatch')
        kinds = ['embedded-preserve', 'embedded-rebuild'] + ([] if tag == '1.4.1' else ['profile'])
        require(sorted(f['kind'] for f in manifest['fixtures']) == sorted(kinds), f'{tag}: wrong fixture set')
        required = {'source.asset', 'source.asset.meta'}
        for fixture in manifest['fixtures']:
            kind = fixture['kind']
            require(fixture == dict(kind=kind, prefab=kind + '.prefab', expected=kind + '.json',
                                    source='source.asset', profile='profile.asset' if kind == 'profile' else ''), f'{tag}/{kind}: fixture paths differ')
            for path in [fixture['prefab'], fixture['expected'], fixture['profile']]:
                if path:
                    required.update([path, path + '.meta'])
            expected = json.loads((directory / fixture['expected']).read_text(encoding='utf-8'))
            require(expected['tag'] == tag and expected['kind'] == kind, f'{tag}/{kind}: wrong expected identity')
            require(expected['rawVersion'] == (-1 if tag == '1.4.1' else 15), f'{tag}/{kind}: unexpected saved version')
            require(expected['sourceBefore'] == expected['sourceAfter'], f'{tag}/{kind}: source mutation')
            require(expected['rendererRetainedSource'] and expected['inactivePrefab'] and expected['disabledComponent'], f'{tag}/{kind}: inactive/source contract failed')
            require(len(expected['output']['frames']) > 4, f'{tag}/{kind}: generated frames missing')
            for value in expected['componentValues'] + expected['profileValues']:
                require(not value['path'].endswith('.m_FileID'), f'{tag}/{kind}: volatile Unity reference captured')
                if value['type'] == 'AnimationCurve':
                    require(len(json.loads(value['value'])['keys']) > 0, f'{tag}/{kind}: curve keys missing')
            fixture_count += 1
        require(len(manifest['files']) == len(required), f'{tag}: duplicate/missing manifest files')
        require({f['path'] for f in manifest['files']} == required, f'{tag}: wrong manifest file set')
        for file in manifest['files']:
            require(digest(directory / file['path']) == file['sha256'], f'{tag}: hash mismatch for {file["path"]}')
        require({p.name for p in directory.iterdir()} == required | {'manifest.json', 'manifest.json.meta'}, f'{tag}: extra/missing files')
        tag_files = list(directory.iterdir()) + [directory.with_name(tag + '.meta')]
        for path in tag_files:
            require(path.is_file() and not path.is_symlink(), f'Not an ordinary file: {path}')
            relative = path.relative_to(corpus).as_posix()
            files[relative] = digest(path)
            if path.suffix == '.meta':
                asset = '.' if path.parent == corpus else path.name[:-5]
                matches = re.findall(r'^guid: ([0-9a-f]{32})\r?$', path.read_text(encoding='utf-8'), flags=re.M)
                require(matches == [meta_guid(tag, asset)], f'{relative}: GUID mismatch')
                require(matches[0] not in guids, f'{relative}: duplicated GUID')
                guids.add(matches[0])
            if path.suffix == '.prefab':
                anchors = re.findall(r'^--- !u!(\d+) &(\d+)\r?$', path.read_text(encoding='utf-8'), flags=re.M)
                require(bool(anchors), f'{relative}: missing Prefab anchors')
                for ordinal, (class_id, actual) in enumerate(anchors):
                    require(actual == prefab_id(tag, path.name, class_id, ordinal), f'{relative}: file ID mismatch')
    require(fixture_count == 83, 'Wrong fixture count')
    return files, fixture_count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--corpus', type=Path, required=True)
    parser.add_argument('--compare', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[3]
    releases = [r for r in json.loads((repository / 'Docs~/Architecture/2026-09-07-published-releases.json').read_text(encoding='utf-8'))['releases'] if not r['historicalCorpus']]
    require(len(releases) == 28, 'Wrong release count')
    files, count = verify(args.corpus.resolve(), repository, releases)
    report = dict(schemaVersion=1, corpus=str(args.corpus.resolve()), releases=28, fixtures=count, files=len(files),
                  compare=None, byteIdentical=None, artifacts=[dict(path=k, sha256=v) for k, v in sorted(files.items())])
    if args.compare:
        compared, _ = verify(args.compare.resolve(), repository, releases)
        require(files == compared, 'Regenerated corpus differs: ' + ', '.join(k for k in files.keys() | compared.keys() if files.get(k) != compared.get(k)))
        report.update(compare=str(args.compare.resolve()), byteIdentical=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'artifacts'}))


if __name__ == '__main__':
    main()
