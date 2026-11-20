param(
    [string]$TestResultsPath,
    [int]$MinimumTotal = 1,
    [string]$RequiredCategory,
    [int]$RequiredCategoryCount = 0,
    [string]$UnityVersion,
    [string]$EditorLogPath,
    [string]$RunnerOutcome,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
# Keep one policy for the PowerShell entry point and Python feature-mode verifier.
# Raw NUnit XML is never rewritten; accepted known failures remain failures there.
$validator = Join-Path $PSScriptRoot 'CI/verify_test_results.py'
$arguments = @($validator, '--results', $TestResultsPath, '--minimum-total', "$MinimumTotal")
if ($RequiredCategory) { $arguments += @('--required-category', $RequiredCategory, '--required-category-count', "$RequiredCategoryCount") }
if ($UnityVersion) { $arguments += @('--unity-version', $UnityVersion) }
if ($EditorLogPath) { $arguments += @('--editor-log', $EditorLogPath) }
if ($RunnerOutcome) { $arguments += @('--runner-outcome', $RunnerOutcome) }
if ($ReportPath) { $arguments += @('--report', $ReportPath) }
$output = & python @arguments 2>&1
if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
$output | ForEach-Object { Write-Host $_ }
