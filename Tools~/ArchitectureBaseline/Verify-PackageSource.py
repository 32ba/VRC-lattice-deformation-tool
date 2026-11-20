"""Compare an isolated Unity package's compilation inputs and metadata to a commit."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--commit', required=True)
parser.add_argument('--package', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--include-tests', action='store_true')
args = parser.parse_args()
paths = ['Runtime', 'Editor', 'package.json'] + (['Tests'] if args.include_tests else [])
entries = subprocess.check_output(['git', 'ls-tree', '-r', args.commit, *paths], text=True).splitlines()
records = []
failures = []


def blob(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()


for entry in entries:
    metadata, name = entry.split('\t', 1)
    if not name.endswith(('.cs', '.asmdef', '.meta')) and name != 'package.json':
        continue
    path = args.package / name
    if not path.exists():
        failures.append(name + ': missing')
        continue
    expected = metadata.split()[2]
    raw = path.read_bytes()
    normalized = raw.replace(b'\r\n', b'\n')
    mode = 'exact' if blob(raw) == expected else 'line-endings' if blob(normalized) == expected else None
    if mode is None and name.endswith('.meta'):
        # Unity adds one trailing space to these empty importer values. Do not
        # normalize GUIDs, payload values, arbitrary whitespace, or C# source.
        normalized = re.sub(rb'^(\s*(?:userData|assetBundleName|assetBundleVariant):) +$',
                            rb'\1', normalized, flags=re.MULTILINE)
        if blob(normalized) == expected:
            mode = 'unity-empty-importer-value-spacing'
    if mode is None:
        failures.append(name + ': differs')
    records.append(dict(path=name, gitBlob=expected, sha256=hashlib.sha256(raw).hexdigest(), match=mode))

report = dict(schemaVersion=1, commit=args.commit, package=str(args.package.resolve()),
              checked=len(records), failures=failures, sources=records)
args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(checked=len(records), failures=failures)))
raise SystemExit(1 if failures else 0)
