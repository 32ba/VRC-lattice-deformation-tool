$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$path = Join-Path $directory 'results.xml'
$verifier = Join-Path $PSScriptRoot 'Assert-TestResults.ps1'
try {
    # One inherited category and one repeated on both suite and case must count
    # as two cases, not one or three.
    $document = @'
<test-run result="Passed" total="2" passed="2" failed="0" skipped="0" inconclusive="0">
  <test-suite><properties><property name="Category" value="GraphicsE2E"/></properties>
    <test-case fullname="Inherited" result="Passed"/>
    <test-case fullname="Repeated" result="Passed"><properties><property name="Category" value="GraphicsE2E"/></properties></test-case>
  </test-suite>
</test-run>
'@
    Set-Content -LiteralPath $path -Value $document
    & $verifier -TestResultsPath $path -RequiredCategory GraphicsE2E -RequiredCategoryCount 2
    foreach ($scenario in @('missing', 'skipped')) {
        $changed = if ($scenario -eq 'missing') {
            $document.Replace('<test-case fullname="Inherited" result="Passed"/>', '')
        } else {
            $document.Replace('fullname="Inherited" result="Passed"', 'fullname="Inherited" result="Skipped"')
        }
        Set-Content -LiteralPath $path -Value $changed
        $rejected = $false
        try { & $verifier -TestResultsPath $path -RequiredCategory GraphicsE2E -RequiredCategoryCount 2 }
        catch {
            $expected = if ($scenario -eq 'missing') { '*contained 1 tests*' } else { '*Inherited=Skipped*' }
            if ($_.Exception.Message -notlike $expected) { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Verifier accepted $scenario inherited category case." }
    }
    Write-Host 'Category inheritance, duplicate category, missing case and skipped case checks passed.'
} finally {
    # Only the single file and empty temporary directory created above are removed.
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    [IO.Directory]::Delete($directory)
}
