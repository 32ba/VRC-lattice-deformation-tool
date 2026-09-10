"""Compare VPM update evidence with the independently saved old Unity probe."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

PACKAGE = 'net.32ba.lattice-deformation-tool'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify(baseline, evidence, release):
    before = read(baseline / 'before.json')
    after = read(evidence / 'reloaded.json')
    saved = read(evidence / 'saved.json')
    require(saved == after, 'Saved and reloaded probe results differ')
    require(json.loads(before['package'])['version'] == '1.4.6-beta.1', 'Unexpected baseline')
    require(json.loads(after['package'])['version'] == '2.0.0-beta.1', 'Unexpected candidate')
    require(before['unity'] == after['unity'] == '2022.3.22f1', 'Unity version changed')
    require(before['source'] == after['source'], 'Source mesh channels changed')
    require(before['objects'] == after['objects'], 'Output, selection, references or overrides changed')
    require([o['path'] for o in after['objects']] ==
            ['Base', 'Variant', 'Profile', 'Scene/Variant'], 'Incomplete probe')

    changed = []
    records = read(baseline / 'before-files.json')
    for record in records:
        original = baseline / 'OldProject' / record['path']
        updated = evidence / 'Project' / record['path']
        require(hashlib.sha256(original.read_bytes()).hexdigest() == record['sha256'],
                'Baseline changed: ' + record['path'])
        if hashlib.sha256(updated.read_bytes()).hexdigest() != record['sha256']:
            changed.append(record['path'])
    require(set(changed) <= {'Assets/NormalUpgrade/Base.prefab', 'Assets/NormalUpgrade/Profile.prefab'},
            'Unexpected saved asset changes: ' + repr(changed))

    old_lock = read(evidence / 'old-vpm-manifest.json')
    new_lock = read(evidence / 'new-vpm-manifest.json')
    for section in ('dependencies', 'locked'):
        require(old_lock[section][PACKAGE]['version'] == '1.4.6-beta.1', 'Old VPM version differs')
        # upgrade updates the resolved lock while retaining the declared requirement.
        expected = '2.0.0-beta.1' if section == 'locked' else '1.4.6-beta.1'
        require(new_lock[section][PACKAGE]['version'] == expected, 'New VPM version differs')
        require({k: v for k, v in old_lock[section].items() if k != PACKAGE} ==
                {k: v for k, v in new_lock[section].items() if k != PACKAGE},
                'Other VPM dependencies changed')
    require(read(evidence / 'Project/Packages/vpm-manifest.json') == new_lock, 'VPM lock changed after import')

    package = evidence / 'Project/Packages' / PACKAGE
    with zipfile.ZipFile(release) as archive:
        names = archive.namelist()
        for name in names:
            require((package / name).read_bytes() == archive.read(name), 'Package changed: ' + name)
        require({p.relative_to(package).as_posix() for p in package.rglob('*.cs')} ==
                {n for n in names if n.endswith('.cs')}, 'Unexpected leftover source files')
    return dict(schemaVersion=1, roundTrips=4, oldFilesPreserved=len(records),
                sourceAndOutputsEqual=True, referencesAndOverridesEqual=True,
                changedSavedFiles=changed, otherVpmDependenciesUnchanged=True,
                candidatePackageFilesMatched=len(names), unitySaveReloadVerified=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--release', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.baseline, args.evidence, args.release)
    with args.output.open('x', encoding='utf-8') as output:
        output.write(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))
