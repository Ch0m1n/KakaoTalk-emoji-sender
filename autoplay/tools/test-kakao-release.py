"""Exercise release retries and failure gates without contacting GitHub."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('publish-kakao-release.ps1')


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell 7 required')
class ReleaseTests(unittest.TestCase):
    def run_release(self, **state):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'artifacts').mkdir()
            (root / 'version.json').write_text('{"version":"11.3.0"}')
            binary = b'test executable'
            (root / 'artifacts/KakaoAutoPlay.exe').write_bytes(binary)
            digest = 'bad' if state.pop('bad_hash', False) else hashlib.sha256(binary).hexdigest()
            (root / 'artifacts/SHA256SUMS.txt').write_text(digest + '  KakaoAutoPlay.exe')
            harness = root / 'run.ps1'
            harness.write_text('''
$state = $env:TEST_RELEASE_STATE | ConvertFrom-Json
function global:git {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'rev-parse') { 'tested-commit' }
    elseif ($state.tagCommit) { $state.tagCommit }
    else { $global:LASTEXITCODE = 1 }
}
function global:gh {
    ConvertTo-Json -InputObject @($args) -Compress | Add-Content calls.jsonl
    $global:LASTEXITCODE = 0
    if ($state.fail -eq $args[1]) { $global:LASTEXITCODE = 1; return }
    if ($args[1] -eq 'list') { ConvertTo-Json -InputObject @($state.releases) -Compress }
}
& $env:TEST_RELEASE_SCRIPT -Tag v11.3.0
''', encoding='utf-8')
            state.setdefault('releases', [])
            result = subprocess.run(['pwsh', '-NoProfile', '-File', str(harness)], cwd=root,
                env=dict(os.environ, TEST_RELEASE_STATE=json.dumps(state), TEST_RELEASE_SCRIPT=str(SCRIPT)),
                capture_output=True, text=True, encoding='utf-8')
            calls = root / 'calls.jsonl'
            operations = [json.loads(line)[1] for line in calls.read_text(encoding='utf-8-sig').splitlines()] if calls.exists() else []
            return result, operations

    def test_new_release(self):
        result, operations = self.run_release()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(operations, ['list', 'create', 'upload', 'edit'])

    def test_draft_retry(self):
        result, operations = self.run_release(tagCommit='tested-commit', releases=[
            dict(tagName='v11.3.0', isDraft=True, isPrerelease=False)])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(operations, ['list', 'upload', 'edit'])

    def test_published_release_is_untouched(self):
        result, operations = self.run_release(tagCommit='tested-commit', releases=[
            dict(tagName='v11.3.0', isDraft=False, isPrerelease=False)])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(operations, ['list'])

    def test_mismatched_tag_stops(self):
        result, operations = self.run_release(tagCommit='other-commit')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(operations, [])

    def test_cannot_downgrade_latest(self):
        result, operations = self.run_release(releases=[
            dict(tagName='v11.4.0', isDraft=False, isPrerelease=False)])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(operations, ['list'])

    def test_bad_hash_stops_before_draft(self):
        result, operations = self.run_release(bad_hash=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(operations, ['list'])

    def test_upload_failure_does_not_publish(self):
        result, operations = self.run_release(fail='upload')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(operations, ['list', 'create', 'upload'])

    def test_lookup_failure_does_not_publish(self):
        result, operations = self.run_release(fail='list')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(operations, ['list'])


if __name__ == '__main__':
    unittest.main()
