"""Verify the saved probe round trip without relying on Unity's import callback."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path


def verify(root):
    with zipfile.ZipFile(next((root / 'old-archives').glob('*.zip'))) as archive:
        for name in archive.namelist():
            assert (root / 'OldProject/Packages/net.32ba.lattice-deformation-tool' / name).read_bytes() == archive.read(name)
        baseline_entries = len(archive.namelist())
    before = json.loads((root / 'before.json').read_text())
    after = json.loads((root / 'reloaded.json').read_text())
    saved = json.loads((root / 'saved.json').read_text())
    assert saved == after, 'Saved and reloaded probe results differ'
    assert json.loads(before['package'])['version'] == '1.4.6-beta.1'
    assert json.loads(after['package'])['version'] == '2.0.0-beta.1'
    assert before['unity'] == after['unity'] == '2022.3.22f1'
    assert before['source'] == after['source'], 'Source mesh channels changed'
    assert before['objects'] == after['objects'], 'Output, selection, references or overrides changed'
    assert [o['path'] for o in after['objects']] == ['Base', 'Variant', 'Profile', 'Scene/Variant']
    assert all(o['meshGuid'] for o in after['objects']), 'Missing mesh reference'
    assert after['objects'][2]['profileGuid'], 'Missing profile reference'
    assert after['objects'][3]['variant'], 'Lost scene prefab variant connection'
    assert '_groups.Array.data[0]._layers.Array.data[1]._weight=0.37' in after['objects'][1]['overrides']
    files = json.loads((root / 'before-files.json').read_text())
    changed = []
    for record in files:
        old = root / 'OldProject' / record['path']
        new = root / 'UnityPackageProject' / record['path']
        assert hashlib.sha256(old.read_bytes()).hexdigest() == record['sha256'], 'Old project changed'
        if hashlib.sha256(new.read_bytes()).hexdigest() != record['sha256']:
            changed.append(record['path'])
    assert set(changed) <= {'Assets/NormalUpgrade/Base.prefab', 'Assets/NormalUpgrade/Profile.prefab'}
    return dict(schemaVersion=1, roundTrips=4, oldFilesPreserved=len(files),
                sourceAndMetadataPreserved=True, outputsAndOverridesEqual=True,
                changedSavedFiles=changed, verifiedAfterDependencyRestart=True,
                baselinePackageEntriesMatched=baseline_entries)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    result = verify(args.evidence)
    with args.output.open('x', encoding='utf-8') as output:
        output.write(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))
