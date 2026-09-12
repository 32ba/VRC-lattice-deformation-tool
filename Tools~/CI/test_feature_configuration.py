import tempfile
import unittest
from pathlib import Path
from feature_configuration import DEFINE, MARKERS, configure, verify_results


class FeatureConfigurationTests(unittest.TestCase):
    def test_roundtrip_preserves_other_settings_and_line_endings(self):
        for newline in ('\n', '\r\n'):
            source = newline.join(['  unrelated:', '    Standalone: 1',
                '  scriptingDefineSymbols:', '    Android: MOBILE',
                '    Standalone: VRC_SDK_VRCSDK3;OTHER', '  nextSetting: 1', ''])
            enabled = configure(source, 'next-release')
            self.assertIn('VRC_SDK_VRCSDK3;OTHER;' + DEFINE, enabled)
            self.assertEqual(enabled, configure(enabled, 'next-release'))
            self.assertEqual(source, configure(enabled, 'default'))

    def test_rejects_unknown_or_duplicate_layout(self):
        for source in ('  scriptingDefineSymbols: {}\n',
                       '  scriptingDefineSymbols:\n    Android: A\n',
                       '  scriptingDefineSymbols:\n    Standalone: A\n    Standalone: B\n'):
            with self.assertRaises(ValueError):
                configure(source, 'next-release')

    def test_requires_correct_compiled_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'result.xml'
            for mode in MARKERS:
                for result in ('Passed', 'Skipped'):
                    path.write_text('<test-run result="Passed"><test-case fullname="' +
                        MARKERS[mode] + '" result="' + result + '"/></test-run>')
                    if result == 'Passed':
                        verify_results(path, mode)
                    else:
                        with self.assertRaises(ValueError):
                            verify_results(path, mode)
                    opposite = 'default' if mode == 'next-release' else 'next-release'
                    with self.assertRaises(ValueError):
                        verify_results(path, opposite)


if __name__ == '__main__':
    unittest.main()
