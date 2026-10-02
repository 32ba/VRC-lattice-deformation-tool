"""Require explicit, initialized SDK test configuration in the Editor run."""
import argparse
import json
from pathlib import Path


def verify(path):
    lines = Path(path).read_text(encoding='utf-8', errors='replace').splitlines()
    marker = 'LATTICE_OFFLINE_SDK_CONFIG initialized without a remote request'
    if '-latticeOfflineSdkConfig' not in lines or marker not in lines:
        raise ValueError('SDK test configuration was not explicitly requested and initialized')
    return {'status': 'passed', 'sdk_config': 'local-defaults', 'live_sdk_service_tested': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--editor-log', required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.editor_log)))
