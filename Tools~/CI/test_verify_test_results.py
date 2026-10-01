import copy
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from feature_configuration import MARKERS, verify_results
from verify_test_results import (ASSERTION, FAILURE_MESSAGE, FONT_STACK, ISSUE_CASES,
                                 ISSUE_VERSION, verify_run)


class TestResultsPolicyTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / 'results.xml'
        self.log_path = Path(self.directory.name) / 'editor.log'
        self.root = ET.Element('test-run')
        for name in sorted(ISSUE_CASES):
            c = ET.SubElement(self.root, 'test-case', fullname=name, result='Failed')
            ET.SubElement(ET.SubElement(c, 'failure'), 'message').text = FAILURE_MESSAGE
            ET.SubElement(c, 'output').text = ASSERTION + '\n'
        ET.SubElement(self.root, 'test-case', fullname=MARKERS['default'], result='Passed')

    def write(self, root=None, version=ISSUE_VERSION, transform_log=lambda s: s):
        root = self.root if root is None else root
        cases = list(root.iter('test-case'))
        root.set('total', str(len(cases)))
        for k, v in [('passed', 'Passed'), ('failed', 'Failed'), ('skipped', 'Skipped'), ('inconclusive', 'Inconclusive')]:
            root.set(k, str(sum(c.get('result') == v for c in cases)))
        failed = int(root.get('failed'))
        root.set('result', 'Failed(Child)' if failed else 'Passed')
        ET.ElementTree(root).write(self.path)
        log = f'Initialize engine version: {version} (hash)\n'
        log += (ASSERTION + '\n' + '\n'.join(FONT_STACK) + '\n\n') * failed
        log += f'Saving results to: /artifacts/{self.path.name}\n'
        log += f'Test run completed. Exiting with code {2 if failed else 0} (result).\n'
        self.log_path.write_text(transform_log(log))

    def verify(self, **kwargs):
        return verify_run(self.path, unity_version=ISSUE_VERSION, editor_log=self.log_path, **kwargs)

    def test_preserves_raw_failures_and_reports_four_exceptions(self):
        self.write()
        before = self.path.read_bytes()
        report = self.verify(runner_outcome='failure')
        self.assertEqual(report['status'], 'accepted_with_known_issue')
        self.assertEqual(report['known_issue_count'], 4)
        self.assertEqual(report['counts']['failed'], 4)
        self.assertEqual(self.path.read_bytes(), before)

    def test_passed_scoped_cases_are_not_counted_as_exceptions(self):
        self.root[0].set('result', 'Passed')
        self.root[0].remove(self.root[0].find('failure'))
        self.write()
        self.assertEqual(self.verify()['known_issue_count'], 3)

    def test_clean_run_has_no_exception(self):
        for c in self.root:
            c.set('result', 'Passed')
            if c.find('failure') is not None:
                c.remove(c.find('failure'))
        self.write()
        self.assertEqual(self.verify(runner_outcome='success')['status'], 'passed')

    def test_rejects_native_assert_outside_test_cases(self):
        for c in self.root:
            c.set('result', 'Passed')
        self.write(transform_log=lambda s: s + ASSERTION + '\n' + '\n'.join(FONT_STACK) + '\n\n')
        with self.assertRaisesRegex(ValueError, 'Native assertion count'):
            self.verify()

    def test_exception_is_never_enabled_without_exact_version(self):
        for version in [None, '2022.3.22f1', '6000.0.66f1', '6000.0.68f1', '6000.0.69f1', '6000.2.0f1']:
            with self.subTest(version=version):
                self.write(version=version or ISSUE_VERSION)
                with self.assertRaises(ValueError):
                    verify_run(self.path, unity_version=version, editor_log=self.log_path)

    def test_rejects_unknown_test_even_with_same_assertion(self):
        unknown = copy.deepcopy(self.root[0])
        unknown.set('fullname', 'Other.Test')
        self.root.append(unknown)
        self.write()
        with self.assertRaisesRegex(ValueError, 'Unapproved'):
            self.verify()

    def test_rejects_other_failure_in_scoped_test(self):
        self.root[0].find('failure/message').text = 'Expected unchanged payload but it changed.'
        self.write()
        with self.assertRaisesRegex(ValueError, 'Unapproved'):
            self.verify()

    def test_rejects_assertion_with_additional_failure_text(self):
        self.root[0].find('failure/message').text += '\nAnother exception'
        self.write()
        with self.assertRaises(ValueError):
            self.verify()

    def test_rejects_every_skip_and_inconclusive_including_scoped_tests(self):
        for status in ['Skipped', 'Inconclusive']:
            with self.subTest(status=status):
                root = copy.deepcopy(self.root)
                root[0].set('result', status)
                self.write(root)
                with self.assertRaises(ValueError):
                    self.verify()

    def test_rejects_missing_duplicate_or_filtered_scoped_test(self):
        for change in ['missing', 'duplicate']:
            with self.subTest(change=change):
                root = copy.deepcopy(self.root)
                if change == 'missing':
                    root.remove(root[0])
                else:
                    root.append(copy.deepcopy(root[0]))
                self.write(root)
                with self.assertRaises(ValueError):
                    self.verify()

    def test_rejects_fifth_failure(self):
        self.root[-1].set('result', 'Failed')
        ET.SubElement(ET.SubElement(self.root[-1], 'failure'), 'message').text = FAILURE_MESSAGE
        self.write()
        with self.assertRaises(ValueError):
            self.verify()

    def test_rejects_wrong_log_version_missing_stack_extra_assert_or_crash(self):
        changes = [lambda s: s.replace(ISSUE_VERSION, '6000.0.69f1'),
                   lambda s: s.replace(FONT_STACK[1], 'Unrelated:AssetOperation'),
                   lambda s: s.replace('Test run completed.', 'Interrupted.'),
                   lambda s: s.replace('/results.xml', '/other.xml'),
                   lambda s: s + 'Caught fatal signal\n',
                   lambda s: s + ASSERTION + '\n' + '\n'.join(FONT_STACK) + '\n\n']
        for change in changes:
            with self.subTest(change=change):
                self.write(transform_log=change)
                with self.assertRaises(ValueError):
                    self.verify()

    def test_rejects_missing_log_or_xml(self):
        self.write()
        with self.assertRaises(ValueError):
            verify_run(self.path, unity_version=ISSUE_VERSION)
        self.path.unlink()
        with self.assertRaises(FileNotFoundError):
            self.verify()

    def test_rejects_mismatched_root_summary_and_runner_outcome(self):
        self.write()
        with self.assertRaises(ValueError):
            self.verify(runner_outcome='success')
        self.root.set('passed', '99')
        ET.ElementTree(self.root).write(self.path)
        with self.assertRaises(ValueError):
            self.verify()

    def test_rejects_suite_teardown_failure(self):
        suite = ET.SubElement(self.root, 'test-suite')
        ET.SubElement(ET.SubElement(suite, 'failure'), 'message').text = 'TearDown failed'
        self.write()
        with self.assertRaisesRegex(ValueError, 'suite/setup/teardown'):
            self.verify()

    def test_rejects_failed_suite_without_failed_leaf(self):
        ET.SubElement(self.root, 'test-suite', result='Failed')
        self.write()
        with self.assertRaisesRegex(ValueError, 'suite failure'):
            self.verify()

    def test_category_count_stays_exact_with_known_failure(self):
        p = ET.SubElement(self.root[0], 'properties')
        ET.SubElement(p, 'property', name='Category', value='InteractionE2E')
        self.write()
        self.verify(required_category='InteractionE2E', required_category_count=1)
        with self.assertRaisesRegex(ValueError, 'contained 1 tests'):
            self.verify(required_category='InteractionE2E', required_category_count=2)

    def test_feature_marker_uses_same_policy_and_rejects_wrong_mode(self):
        self.write()
        verify_results(self.path, 'default', ISSUE_VERSION, self.log_path)
        with self.assertRaises(ValueError):
            verify_results(self.path, 'next-release', ISSUE_VERSION, self.log_path)
        self.root[0].find('failure/message').text = 'Unknown failure'
        self.write()
        with self.assertRaises(ValueError):
            verify_results(self.path, 'default', ISSUE_VERSION, self.log_path)


if __name__ == '__main__':
    unittest.main()
