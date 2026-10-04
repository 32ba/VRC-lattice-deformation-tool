import unittest
from verify_release_ci import REQUIRED_JOBS, select_run, verify_jobs

SHA = 'a' * 40


def run(identifier=1, **changes):
    result = dict(databaseId=identifier, headSha=SHA, event='workflow_dispatch',
                  status='completed', conclusion='success',
                  jobs=[dict(name=n, status='completed', conclusion='success') for n in REQUIRED_JOBS])
    result.update(changes)
    return result


class ReleaseCiTests(unittest.TestCase):
    def test_accepts_both_shipping_editors_and_archives(self):
        for event in ('push', 'workflow_dispatch'):
            candidate = select_run([run(event=event)], SHA)
            verify_jobs(candidate, SHA)

    def test_rejects_previous_single_editor_job_names(self):
        candidate = run(jobs=[dict(name=name, status='completed', conclusion='success')
                              for name in ('Package archives', 'EditMode Tests (default)',
                                           'EditMode Tests (next-release)')])
        with self.assertRaises(ValueError):
            verify_jobs(candidate, SHA)

    def test_requires_exactly_two_shipping_editor_jobs(self):
        self.assertEqual(REQUIRED_JOBS, {
            'Package archives',
            'EditMode Tests (2022.3.22f1)',
            'EditMode Tests (6000.0.67f1)',
        })

    def test_old_four_configuration_jobs_cannot_replace_shipping_jobs(self):
        candidate = run(jobs=[dict(name='Package archives', status='completed', conclusion='success')] + [
            dict(name=f'EditMode Tests ({version}, {configuration})', status='completed', conclusion='success')
            for version in ('2022.3.22f1', '6000.0.67f1')
            for configuration in ('default', 'next-release')
        ])
        with self.assertRaises(ValueError):
            verify_jobs(candidate, SHA)

    def test_rejects_wrong_commit_and_pr_merge_evidence(self):
        for candidate in (run(headSha='b' * 40), run(event='pull_request')):
            with self.assertRaises(ValueError):
                select_run([candidate], SHA)
            with self.assertRaises(ValueError):
                verify_jobs(candidate, SHA)

    def test_does_not_fall_back_to_old_success(self):
        for changes in (dict(status='in_progress'), dict(conclusion='failure'), dict(conclusion='cancelled')):
            with self.assertRaises(ValueError):
                select_run([run(1), run(2, **changes)], SHA)

    def test_rejects_missing_skipped_or_duplicate_job(self):
        for name in REQUIRED_JOBS:
            for kind in ('missing', 'skipped', 'duplicate'):
                candidate = run()
                job = next(j for j in candidate['jobs'] if j['name'] == name)
                if kind == 'missing':
                    candidate['jobs'].remove(job)
                elif kind == 'skipped':
                    job['conclusion'] = 'skipped'
                else:
                    candidate['jobs'].append(job.copy())
                with self.assertRaises(ValueError):
                    verify_jobs(candidate, SHA)


if __name__ == '__main__':
    unittest.main()
