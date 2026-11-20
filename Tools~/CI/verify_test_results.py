"""Validate raw NUnit results; narrowly account for the approved UUM-85059 exception."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ISSUE_VERSION = '6000.0.67f1'
ISSUE = 'UUM-85059'
PREFIX = 'Net._32Ba.LatticeDeformationTool.Tests.Editor.'
ISSUE_CASES = frozenset(PREFIX + name for name in (
    'AuthoringGestureEndToEndTests.BrushEscape_AfterAnotherUndoOperationPreservesBothEdits',
    'BrushToolOverlayTests.AllToolLanguagesAndModes_DrawWithoutChangingPayload',
    'GuidedAuthoringTests.GuidedPaint_AllLanguagesPreserveNullPayloadWithoutInitializingIt',
    'PeripheralInspectorTests.RebuildInspector_PaintPreservesMixedValuesAndUnknownEnumWithoutUndoOrDirtyChanges',
))
ASSERTION = ("Assertion failed on expression: '!(o->TestHideFlag(Object::kDontSaveInEditor) "
             "&& (options & kAllowDontSaveObjectsToBePersistent) == 0)'")
FAILURE_MESSAGE = "Unhandled log message: '[Assert] " + ASSERTION + "'. Use UnityEngine.TestTools.LogAssert.Expect"
FONT_STACK = ('UnityEditor.AssetDatabase:AddObjectToAsset',
              'UnityEditor.TextCore.Text.TextEditorResourceManager:AddTextureToAsset',
              'UnityEngine.TextCore.Text.FontAsset:SetupNewAtlasTexture')


def verify_run(path, unity_version=None, editor_log=None, minimum_total=1,
               required_category=None, required_category_count=0, runner_outcome=None):
    root = ET.parse(path).getroot()
    if root.tag != 'test-run':
        raise ValueError('Expected a test-run XML root.')
    cases = list(root.iter('test-case'))
    names = [c.get('fullname') for c in cases]
    if any(not n for n in names) or len(set(names)) != len(names):
        raise ValueError('Missing or duplicate test-case fullname.')
    counts = {k: int(root.get(k, '0')) for k in ('total', 'passed', 'failed', 'skipped', 'inconclusive')}
    observed = Counter(c.get('result') for c in cases)
    if counts['total'] < minimum_total:
        raise ValueError(f"Test run total {counts['total']} is below required minimum {minimum_total}.")
    if counts['total'] != len(cases) or any(
            counts[k] != observed[status] for k, status in
            [('passed', 'Passed'), ('failed', 'Failed'), ('skipped', 'Skipped'), ('inconclusive', 'Inconclusive')]):
        raise ValueError('XML summary does not match test-case results.')
    if set(observed) - {'Passed', 'Failed', 'Skipped', 'Inconclusive'}:
        raise ValueError('Unknown test-case result.')

    log = None
    if unity_version:
        if editor_log is None:
            raise ValueError('Editor log is required to verify the actual Unity version and completion.')
        log = Path(editor_log).read_text(encoding='utf-8', errors='replace')
        versions = re.findall(r'^Initialize engine version: (\S+)', log, re.MULTILINE)
        if versions != [unity_version]:
            raise ValueError('Editor log Unity version does not match the requested version.')
        code = 2 if counts['failed'] else 0
        if len(re.findall(r'^Test run completed\. Exiting with code ' + str(code) + r'\b', log, re.MULTILINE)) != 1:
            raise ValueError('Editor log does not confirm normal Test Runner completion.')
        if re.search(r'Caught fatal signal|Fatal error!|Crash!!!', log):
            raise ValueError('Editor log reports a crash.')
        saved = re.findall(r'^Saving results to: (.+)$', log, re.MULTILINE)
        if not any(p.strip().replace('\\', '/').endswith('/' + Path(path).name) for p in saved):
            raise ValueError('Editor log does not identify this result XML.')

    known = []
    if unity_version == ISSUE_VERSION and not ISSUE_CASES.issubset(names):
        raise ValueError('The full set of four issue-scoped tests must be present, not filtered out.')
    for case in cases:
        status, name = case.get('result'), case.get('fullname')
        if status == 'Passed':
            continue
        if (unity_version == ISSUE_VERSION and name in ISSUE_CASES and status == 'Failed'
                and len(case.findall('failure')) == 1
                and (case.findtext('failure/message') or '').strip() == FAILURE_MESSAGE
                and ASSERTION in (case.findtext('output') or '')):
            known.append(name)
        else:
            raise ValueError(f'Unapproved test result: {name}={status}')

    expected_root = {'Failed', 'Failed(Child)'} if known else {'Passed'}
    if root.get('result') not in expected_root:
        raise ValueError('Test run root result is inconsistent with approved case results.')
    for suite in root.iter('test-suite'):
        failed_children = [c for c in suite.iter('test-case') if c.get('result') == 'Failed']
        if suite.get('result', '').startswith('Failed') and not failed_children:
            raise ValueError('Unapproved suite failure without an approved failed child.')
        for failure in suite.findall('failure'):
            if (not failed_children or
                    (failure.findtext('message') or '').strip() != 'One or more child tests had errors'):
                raise ValueError('Unapproved suite/setup/teardown failure.')
    if log is not None:
        # The assertion text alone is shared by unrelated AssetDatabase failures.
        # Require every native assertion to have the confirmed font-atlas stack,
        # and one occurrence per approved failed case. Never accept other skips.
        blocks = re.findall(r'^Assertion failed on expression: ([^\r\n]+)\r?\n((?:[^\r\n]+\r?\n)+)', log, re.MULTILINE)
        assertion_count = len(re.findall(r'^Assertion failed on expression:', log, re.MULTILINE))
        if assertion_count != len(known) or len(blocks) != len(known) or any(
                'Assertion failed on expression: ' + message != ASSERTION
                or not all(frame in stack for frame in FONT_STACK)
                for message, stack in blocks):
            raise ValueError('Native assertion count or font-atlas stack does not match UUM-85059.')
    if runner_outcome and runner_outcome != ('failure' if known else 'success'):
        raise ValueError('Runner outcome is inconsistent with the validated XML.')

    if required_category:
        if required_category_count <= 0:
            raise ValueError('RequiredCategoryCount must be greater than zero when RequiredCategory is specified.')
        selected = []

        def walk(node, inherited=frozenset()):
            tags = inherited | {p.get('value') for p in node.findall('properties/property')
                                if p.get('name') == 'Category'}
            if node.tag == 'test-case' and required_category in tags:
                selected.append(node)
            for child in node:
                if child.tag in ('test-suite', 'test-case'):
                    walk(child, tags)

        walk(root)
        if len(selected) != required_category_count:
            raise ValueError(f"Required category '{required_category}' contained {len(selected)} tests; expected exactly {required_category_count}.")
    return {'status': 'accepted_with_known_issue' if known else 'passed',
            'unity_version': unity_version, 'counts': counts,
            'known_issue': ISSUE if known else None, 'known_issue_count': len(known),
            'known_issue_cases': sorted(known), 'unknown_failures': 0,
            'required_category': required_category,
            'required_category_count': required_category_count if required_category else None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--unity-version')
    parser.add_argument('--editor-log', type=Path)
    parser.add_argument('--minimum-total', type=int, default=1)
    parser.add_argument('--required-category')
    parser.add_argument('--required-category-count', type=int, default=0)
    parser.add_argument('--runner-outcome', choices=['success', 'failure'])
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    result = verify_run(args.results, args.unity_version, args.editor_log, args.minimum_total,
                        args.required_category, args.required_category_count, args.runner_outcome)
    output = json.dumps(result, indent=2)
    if args.report:
        args.report.write_text(output + '\n', encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
