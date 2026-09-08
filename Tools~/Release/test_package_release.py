import json
import io
import tarfile
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest.mock import patch

import package_release as release


class ReleaseTests(unittest.TestCase):
    def files(self):
        return {'package.json': json.dumps({'name': 'net.32ba.test', 'version': '2.0.0-beta.1'}).encode(),
                'package.json.meta': b'guid: ' + b'a' * 32 + b'\n',
                'Runtime.meta': b'guid: ' + b'b' * 32 + b'\nfolderAsset: yes\n',
                'Runtime/code.cs': b'class Example {}\n',
                'Runtime/code.cs.meta': b'guid: ' + b'c' * 32 + b'\n',
                'Blobs~/logo.png': b'ignored-media'}

    def test_release_classification_and_invalid_versions(self):
        for version, expected in [('2.0.0-beta.1', True), ('2.0.0', False), ('2.0.0+build-with-hyphen', False)]:
            self.assertEqual(release.version_info({'name': 'net.32ba.test', 'version': version})[2], expected)
        for version in ['2.00.0', '2.0.0-beta..1', '2.0.0-01', '2.0.0/', '2.0.0\n']:
            with self.assertRaises(ValueError):
                release.version_info({'name': 'net.32ba.test', 'version': version})

    def test_distribution_policy(self):
        for path in ['Tests/a.cs', 'Tests.meta', 'Tools~/tool.py', '.github/workflows/release.yml',
                     'AGENTS.md', 'Docs~/Architecture/evidence.json', 'Packages/duplicate']:
            self.assertFalse(release.selected(path), path)
        for path in ['Runtime/code.cs', 'Editor/tool.cs.meta', 'README.md', 'LICENSE', 'Blobs~/logo.png']:
            self.assertTrue(release.selected(path), path)

    def test_missing_duplicate_and_orphan_metadata_rejected(self):
        for mutation in ['missing', 'duplicate', 'orphan', 'parent', 'folder']:
            files = self.files()
            if mutation == 'missing': del files['Runtime/code.cs.meta']
            if mutation == 'duplicate': files['Runtime/code.cs.meta'] = files['package.json.meta']
            if mutation == 'orphan': del files['Runtime/code.cs']
            if mutation == 'parent': del files['Runtime.meta']
            if mutation == 'folder': files['Runtime.meta'] += b'guid: invalid\n'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                release.unity_entries(files, 'net.32ba.test')

    def test_folder_guid_and_packages_destination_preserved(self):
        files = self.files()
        files['Empty.meta'] = b'guid: ' + b'd' * 32 + b'\nfolderAsset: yes\n'
        entries = release.unity_entries(files, 'net.32ba.test')
        self.assertEqual(entries['d' * 32 + '/pathname'], b'Packages/net.32ba.test/Empty')
        self.assertEqual(entries['b' * 32 + '/pathname'], b'Packages/net.32ba.test/Runtime')
        self.assertNotIn('b' * 32 + '/asset', entries)
        self.assertEqual(entries['c' * 32 + '/asset'], b'class Example {}\n')

    def test_reproducibility_tamper_rejection_and_no_overwrite(self):
        files = self.files()
        with tempfile.TemporaryDirectory() as temporary, patch.object(release, 'snapshot', return_value=('a' * 40, files)):
            first, second = Path(temporary) / 'one', Path(temporary) / 'two'
            release.build('HEAD', first)
            release.build('HEAD', second)
            for path in first.iterdir():
                self.assertEqual(path.read_bytes(), (second / path.name).read_bytes())
            with self.assertRaises(ValueError): release.build('HEAD', first)
            stem = 'net.32ba.test-2.0.0-beta.1'
            with zipfile.ZipFile(first / (stem + '.zip'), 'a') as archive:
                archive.writestr('../extra', b'bad')
            with self.assertRaises(ValueError):
                release.verify_archives(first, stem, files, release.unity_entries(files, 'net.32ba.test'))

    def test_unity_archive_corruption_rejected(self):
        files = self.files()
        unity = release.unity_entries(files, 'net.32ba.test')
        stem = 'test'
        for mutation in ['content', 'duplicate', 'missing', 'symlink']:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                release.write_archives(directory, stem, files, unity)
                entries = list(unity.items())
                if mutation == 'missing': entries.pop()
                if mutation == 'duplicate': entries.append(entries[0])
                with tarfile.open(directory / (stem + '.unitypackage'), 'w:gz') as archive:
                    for index, (name, data) in enumerate(entries):
                        item = tarfile.TarInfo(name)
                        if index == 0 and mutation == 'content': data += b'changed'
                        if index == 0 and mutation == 'symlink':
                            item.type, item.linkname = tarfile.SYMTYPE, '../../outside'
                            archive.addfile(item)
                        else:
                            item.size = len(data)
                            archive.addfile(item, io.BytesIO(data))
                with self.assertRaises(ValueError):
                    release.verify_archives(directory, stem, files, unity)


if __name__ == '__main__':
    unittest.main()
