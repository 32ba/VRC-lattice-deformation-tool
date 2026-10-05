"""Verify the compiled shipping feature state without changing project settings."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET

PREFIX = 'Net._32Ba.LatticeDeformationTool.Editor.Tests.LatticeDeformationFeatureFlagsTests.'
SHIPPING_MARKER = PREFIX + 'NextReleaseFeatures_AreDisabledByDefault'
DEVELOPMENT_MARKER = PREFIX + 'NextReleaseFeatures_AreEnabledWhenRequested'


def verify_results(path, unity_version=None, editor_log=None):
    root = ET.parse(path).getroot()
    if unity_version:
        from verify_test_results import verify_run
        verify_run(path, unity_version=unity_version, editor_log=editor_log)
    elif root.tag != 'test-run' or root.get('result') != 'Passed':
        raise ValueError('Expected a successful Unity test run.')
    cases = list(root.iter('test-case'))
    expected = [c for c in cases if c.get('fullname') == SHIPPING_MARKER]
    if len(expected) != 1 or expected[0].get('result') != 'Passed':
        raise ValueError('Shipping feature marker did not run and pass exactly once.')
    if any(c.get('fullname') == DEVELOPMENT_MARKER for c in cases):
        raise ValueError('Non-shipping development features were enabled.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-version')
    parser.add_argument('--editor-log', type=Path)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    verify_results(args.results, args.unity_version, args.editor_log)
    print('Verified shipping feature state.')


if __name__ == '__main__':
    main()
