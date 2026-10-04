import tempfile
import unittest
from pathlib import Path
from feature_configuration import SHIPPING_MARKER, DEVELOPMENT_MARKER, verify_results


class ShippingFeatureTests(unittest.TestCase):
    def verify(self, cases, result='Passed'):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'result.xml'
            path.write_text('<test-run result="' + result + '">' + ''.join(
                '<test-case fullname="' + name + '" result="' + status + '"/>'
                for name, status in cases) + '</test-run>')
            verify_results(path)

    def test_accepts_shipping_marker(self):
        self.verify([(SHIPPING_MARKER, 'Passed')])

    def test_rejects_missing_duplicate_failed_or_skipped_marker(self):
        for cases in ([], [(SHIPPING_MARKER, 'Passed')]*2,
                      [(SHIPPING_MARKER, 'Failed')], [(SHIPPING_MARKER, 'Skipped')]):
            with self.subTest(cases=cases), self.assertRaises(ValueError):
                self.verify(cases)

    def test_rejects_development_features_even_with_shipping_marker(self):
        for cases in ([(DEVELOPMENT_MARKER, 'Passed')],
                      [(SHIPPING_MARKER, 'Passed'), (DEVELOPMENT_MARKER, 'Passed')]):
            with self.subTest(cases=cases), self.assertRaises(ValueError):
                self.verify(cases)

    def test_rejects_unsuccessful_run(self):
        with self.assertRaises(ValueError):
            self.verify([(SHIPPING_MARKER, 'Passed')], 'Failed')


if __name__ == '__main__':
    unittest.main()
