param(
    [string]$ProjectPath,
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe",
    [string]$ResultsPath,
    [string]$TestResultsPath,
    [int]$MinimumTestTotal = 287,
    [switch]$PreflightOnly,
    [switch]$EnforceLineCoverage,
    [switch]$NoCleanResults,
    [switch]$AllowExternalResultsClean,
    [switch]$AllowOpenEditor
)

$ErrorActionPreference = "Stop"

$packageRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Resolve-Path (Join-Path $packageRoot "..\..")
} else {
    $ProjectPath = Resolve-Path $ProjectPath
}

if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
    $ResultsPath = Join-Path $ProjectPath "Temp\LatticeCoverage"
}

if ([string]::IsNullOrWhiteSpace($TestResultsPath)) {
    $TestResultsPath = Join-Path $ResultsPath "test-results.xml"
}

$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$ResultsPath = [System.IO.Path]::GetFullPath($ResultsPath)
$TestResultsPath = [System.IO.Path]::GetFullPath($TestResultsPath)

if (-not (Test-Path $UnityPath)) {
    throw "Unity executable was not found: $UnityPath"
}

$packagePathForFileCheck = Join-Path $ProjectPath "Packages\net.32ba.lattice-deformation-tool"
if (-not (Test-Path $packagePathForFileCheck)) {
    throw "Package path was not found: $packagePathForFileCheck"
}

$lockPath = Join-Path $ProjectPath "Packages\packages-lock.json"
$coveragePackageCache = Get-ChildItem -LiteralPath (Join-Path $ProjectPath "Library\PackageCache") -Directory -Filter "com.unity.testtools.codecoverage@*" -ErrorAction SilentlyContinue
$coverageInLock = (Test-Path $lockPath) -and ((Get-Content -LiteralPath $lockPath -Raw) -match '"com\.unity\.testtools\.codecoverage"')
if (-not $coverageInLock -and -not $coveragePackageCache) {
    throw "Unity Code Coverage package was not found. Add com.unity.testtools.codecoverage to the Unity project before running coverage."
}

$vrchatPackageCache = Get-ChildItem -LiteralPath (Join-Path $ProjectPath "Library\PackageCache") -Directory -Filter "com.vrchat.avatars@*" -ErrorAction SilentlyContinue
$vrchatEmbeddedPackage = Test-Path (Join-Path $ProjectPath "Packages\com.vrchat.avatars\package.json")
$vrchatInLock = (Test-Path $lockPath) -and ((Get-Content -LiteralPath $lockPath -Raw) -match '"com\.vrchat\.avatars"')
$vrchatAvailable = $vrchatInLock -or $vrchatPackageCache -or $vrchatEmbeddedPackage

& (Join-Path $PSScriptRoot "Assert-IgnoredUnityFolders.ps1") -PackagePath $packagePathForFileCheck

$targetAssemblies = @(
    "net.32ba.lattice-deformation-tool",
    "net.32ba.lattice-deformation-tool.editor"
)
if ($vrchatAvailable) {
    $targetAssemblies += "net.32ba.lattice-deformation-tool.vrchat"
}

$testAssemblies = @("net.32ba.lattice-deformation-tool.tests.editor")
if ($vrchatAvailable) {
    $testAssemblies += "net.32ba.lattice-deformation-tool.tests.editor.vrchat"
}

$assemblyFilters = "assemblyFilters:+" + ($targetAssemblies -join ",+")
$packagePath = $packagePathForFileCheck.Replace("\", "/")
$coverageOptions = @(
    "generateAdditionalMetrics"
    "generateHtmlReport"
    "generateBadgeReport"
    "generateAdditionalReports"
    "generateTestReferences"
    $assemblyFilters
    "pathFilters:+$packagePath/Runtime/**,+$packagePath/Editor/**,-**/Tests/**"
) -join ";"

Write-Host "ProjectPath: $ProjectPath"
Write-Host "PackagePath: $packagePathForFileCheck"
Write-Host "ResultsPath: $ResultsPath"
Write-Host "TestResultsPath: $TestResultsPath"
Write-Host "MinimumTestTotal: $MinimumTestTotal"
Write-Host "VRChatAvailable: $vrchatAvailable"
Write-Host "TestAssemblies: $($testAssemblies -join ', ')"
Write-Host "TargetAssemblies: $($targetAssemblies -join ', ')"
Write-Host "CoverageOptions: $coverageOptions"

if ($PreflightOnly) {
    Write-Host "Preflight completed. Unity was not launched, and coverage results were not cleaned."
    return
}

if (-not $AllowOpenEditor) {
    $projectName = Split-Path $ProjectPath -Leaf
    $openUnity = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowTitle -like "*$projectName*"
    }
    if ($openUnity) {
        throw "Unity is already running. Close the editor before batchmode coverage, or pass -AllowOpenEditor if you know this project is not open."
    }
}

if (-not $NoCleanResults -and (Test-Path $ResultsPath)) {
    $projectRootWithSeparator = $ProjectPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resultsInsideProject = $ResultsPath.StartsWith($projectRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $resultsInsideProject -and -not $AllowExternalResultsClean) {
        throw "Refusing to clean coverage results outside ProjectPath: $ResultsPath. Pass -AllowExternalResultsClean or choose a ResultsPath inside $ProjectPath."
    }

    Remove-Item -LiteralPath $ResultsPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $ResultsPath | Out-Null

$arguments = @(
    "-batchmode"
    "-projectPath", $ProjectPath
    "-runTests"
    "-testPlatform", "editmode"
    "-assemblyNames", ($testAssemblies -join ",")
    "-testResults", $TestResultsPath
    "-debugCodeOptimization"
    "-burst-disable-compilation"
    "-enableCodeCoverage"
    "-coverageResultsPath", $ResultsPath
    "-coverageOptions", $coverageOptions
    "-logFile", (Join-Path $ResultsPath "unity-coverage.log")
    "-quit"
)

Write-Host "ProjectPath: $ProjectPath"
Write-Host "ResultsPath: $ResultsPath"
Write-Host "CoverageOptions: $coverageOptions"

& $UnityPath @arguments
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "Unity coverage run failed with exit code $exitCode. See $(Join-Path $ResultsPath 'unity-coverage.log')"
}

Write-Host "Coverage run completed."
Write-Host "Test results: $TestResultsPath"
Write-Host "Coverage results: $ResultsPath"

& (Join-Path $PSScriptRoot "Assert-TestResults.ps1") -TestResultsPath $TestResultsPath -MinimumTotal $MinimumTestTotal
& (Join-Path $PSScriptRoot "Assert-CoverageArtifacts.ps1") -ResultsPath $ResultsPath

if ($EnforceLineCoverage) {
    & (Join-Path $PSScriptRoot "Assert-Coverage.ps1") `
        -ResultsPath $ResultsPath `
        -MinimumLineCoverage 100.0 `
        -TargetAssemblies $targetAssemblies
}
