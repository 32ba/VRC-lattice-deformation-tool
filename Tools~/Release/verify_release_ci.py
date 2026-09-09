"""Require successful CI for the exact release commit before any publishing step."""
import argparse
import json
import re
import subprocess

REQUIRED_JOBS = {'Package archives', 'EditMode Tests (default)', 'EditMode Tests (next-release)'}


def select_run(runs, commit):
    # PR jobs normally test a synthetic merge commit, not the published head.
    candidates = [r for r in runs if r.get('headSha') == commit and
                  r.get('event') in ('push', 'workflow_dispatch')]
    if not candidates:
        raise ValueError('No push/manual Test workflow run for the release commit.')
    latest = max(candidates, key=lambda r: int(r['databaseId']))
    if latest.get('status') != 'completed' or latest.get('conclusion') != 'success':
        raise ValueError('Latest matching CI run has not completed successfully.')
    return latest


def verify_jobs(run, commit):
    if (run.get('headSha') != commit or run.get('status') != 'completed' or
            run.get('conclusion') != 'success' or
            run.get('event') not in ('push', 'workflow_dispatch')):
        raise ValueError('CI details do not match a successful release-commit run.')
    for name in REQUIRED_JOBS:
        jobs = [j for j in run.get('jobs', []) if j.get('name') == name]
        if len(jobs) != 1 or jobs[0].get('status') != 'completed' or jobs[0].get('conclusion') != 'success':
            raise ValueError('Required CI job did not succeed: ' + name)


def gh(*args):
    return json.loads(subprocess.check_output(['gh', *args], text=True))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--commit', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', args.commit):
        raise ValueError('Expected a full commit SHA.')
    runs = gh('run', 'list', '--workflow', 'test.yml', '--commit', args.commit,
              '--limit', '100', '--json', 'databaseId,headSha,event,status,conclusion')
    selected = select_run(runs, args.commit)
    details = gh('run', 'view', str(selected['databaseId']), '--json',
                 'headSha,event,status,conclusion,jobs,url')
    verify_jobs(details, args.commit)
    print(json.dumps({'commit': args.commit, 'runId': selected['databaseId'],
                      'url': details['url'], 'requiredJobs': sorted(REQUIRED_JOBS)}))


if __name__ == '__main__':
    main()
