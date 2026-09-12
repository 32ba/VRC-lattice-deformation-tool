"""Configure a settled, stopped Unity CI project and verify its compiled test mode."""
import argparse
from pathlib import Path
import re
import xml.etree.ElementTree as ET

DEFINE = 'LATTICE_DEFORMATION_TOOL_ENABLE_NEXT_RELEASE_FEATURES'
PREFIX = 'Net._32Ba.LatticeDeformationTool.Editor.Tests.LatticeDeformationFeatureFlagsTests.'
MARKERS = {'default': PREFIX + 'NextReleaseFeatures_AreDisabledByDefault',
           'next-release': PREFIX + 'NextReleaseFeatures_AreEnabledWhenRequested'}


def configure(text, mode):
    # Only edit the Standalone entry in this one mapping; other Unity settings
    # also have Standalone keys. Reject unfamiliar layouts instead of guessing.
    mapping = re.compile(r'(?m)^  scriptingDefineSymbols:\r?\n((?:    [^\r\n]*\r?\n)+)')
    matches = list(mapping.finditer(text))
    if len(matches) != 1:
        raise ValueError('Expected one expanded scriptingDefineSymbols mapping.')
    match = matches[0]
    body = match.group(1)
    entries = list(re.finditer(r'(?m)^    Standalone: *([A-Za-z0-9_;]*)\r?$', body))
    if len(entries) != 1:
        raise ValueError('Expected one plain Standalone define entry.')
    entry = entries[0]
    definitions = [s for s in entry.group(1).split(';') if s and s != DEFINE]
    if mode == 'next-release':
        definitions.append(DEFINE)
    body = body[:entry.start(1)] + ';'.join(definitions) + body[entry.end(1):]
    return text[:match.start(1)] + body + text[match.end(1):]


def verify_results(path, mode):
    root = ET.parse(path).getroot()
    if root.tag != 'test-run' or root.get('result') != 'Passed':
        raise ValueError('Expected a successful Unity test run.')
    cases = list(root.iter('test-case'))
    expected = [c for c in cases if c.get('fullname') == MARKERS[mode]]
    forbidden = MARKERS['default' if mode == 'next-release' else 'next-release']
    if len(expected) != 1 or expected[0].get('result') != 'Passed':
        raise ValueError('Requested configuration marker did not run and pass.')
    if any(c.get('fullname') == forbidden for c in cases):
        raise ValueError('Opposite configuration marker was compiled.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', required=True, choices=MARKERS)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--settings', type=Path)
    target.add_argument('--results', type=Path)
    args = parser.parse_args()
    if args.results:
        verify_results(args.results, args.mode)
    else:
        original = args.settings.read_bytes()
        updated = configure(original.decode('utf-8'), args.mode).encode('utf-8')
        if updated != original:
            args.settings.write_bytes(updated)
    print('Verified configuration: ' + args.mode)


if __name__ == '__main__':
    main()
