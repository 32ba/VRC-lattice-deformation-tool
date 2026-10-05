[CmdletBinding()]
param(
    [string[]]$Tags = @(),
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
    [string]$EvidenceParent = (Join-Path $env:LOCALAPPDATA 'Codex\Lattice2BetaEvidence\LaterFixtures'),
    [int]$TimeoutSeconds = 600,
    [int]$MaxPrivateMb = 6144
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$inventoryPath = Join-Path $repository 'Docs~\Architecture\2026-09-07-published-releases.json'
$published = @((Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json).releases | Where-Object { -not $_.historicalCorpus })
if ($Tags.Count -eq 0) { $Tags = @($published.tag) }
$previousIndex = -1
foreach ($tag in $Tags) {
    $index = [Array]::IndexOf(@($published.tag), $tag)
    if ($index -le $previousIndex) { throw "Tags must be known, unique and in publication order: $tag" }
    $previousIndex = $index
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity is missing: $UnityPath" }

function Assert-Child([string]$Candidate, [string]$Parent) {
    $candidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd('\', '/')
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $candidateFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escaped its owner: $candidateFull" }
}
function Remove-OwnedTree([string]$Candidate, [string]$Parent) {
    Assert-Child $Candidate $Parent
    if (Test-Path -LiteralPath $Candidate) {
        $item = Get-Item -LiteralPath $Candidate -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing non-ordinary directory: $Candidate" }
        Remove-Item -LiteralPath $Candidate -Recurse -Force
    }
}
function Get-Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Git-Text([string[]]$Arguments) {
    $result = & git -C $repository @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $Arguments" }
    return ($result -join "`n").Trim()
}
function Assert-Corpus([string]$TagRoot, $Record, $ToolRecords) {
    $manifest = Get-Content -LiteralPath (Join-Path $TagRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.tag -cne $Record.tag -or $manifest.commitSha -cne $Record.commit -or $manifest.packageVersion -cne $Record.packageVersion -or $manifest.unityVersion -cne '2022.3.22f1') { throw "Wrong fixture provenance: $($Record.tag)" }
    if ($manifest.tools.Count -ne $ToolRecords.Count) { throw 'Wrong helper count.' }
    foreach ($tool in $ToolRecords) {
        $entry = @($manifest.tools | Where-Object { $_.path -ceq $tool.path })
        if ($entry.Count -ne 1 -or $entry[0].sha256 -cne $tool.sha256) { throw "Wrong helper provenance: $($tool.path)" }
    }
    $expectedKinds = if ($Record.tag -eq '1.4.1') { @('embedded-preserve', 'embedded-rebuild') } else { @('embedded-preserve', 'embedded-rebuild', 'profile') }
    if (Compare-Object @($manifest.fixtures.kind | Sort-Object) @($expectedKinds | Sort-Object)) { throw "Wrong fixture kinds: $($Record.tag)" }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $manifest.files) {
        if ($file.path -cne [IO.Path]::GetFileName($file.path) -or -not $seen.Add($file.path)) { throw 'Invalid manifest path or duplicate.' }
        if ((Get-Sha (Join-Path $TagRoot $file.path)) -cne $file.sha256) { throw "Payload hash mismatch: $($file.path)" }
    }
    $actual = @(Get-ChildItem -LiteralPath $TagRoot -File | ForEach-Object Name | Sort-Object)
    $expected = @(@($manifest.files.path) + @('manifest.json', 'manifest.json.meta') | Sort-Object)
    if (Compare-Object $actual $expected) { throw "Unexpected corpus files: $($Record.tag)" }
}

$runRoot = Join-Path ([IO.Path]::GetFullPath($EvidenceParent)) ([Guid]::NewGuid().ToString('N'))
Assert-Child $runRoot $EvidenceParent
if (Test-Path -LiteralPath $runRoot) { throw 'Evidence directory collision.' }
$project = Join-Path $runRoot 'UnityProject'
foreach ($directory in @('Assets\Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $directory) -Force | Out-Null
}
$toolPaths = @('Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs',
    'Tools~/HistoricalFixtures/LaterReleases/LaterReleaseMeshCapture.cs',
    'Tools~/HistoricalFixtures/LaterReleases/LaterReleaseFixtureGenerator.cs',
    'Tools~/HistoricalFixtures/LaterReleases/Generate-LaterReleaseFixtures.ps1')
$toolRecords = @($toolPaths | ForEach-Object {
    $sourcePath = Join-Path $repository $_
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $project ('Assets\Editor\' + [IO.Path]::GetFileName($_)))
    [ordered]@{ path = $_; sha256 = Get-Sha $sourcePath }
})
@{ tools = $toolRecords } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'Assets\Editor\tool-input.json') -Encoding utf8NoBOM
@{ dependencies = [ordered]@{ 'com.unity.burst' = '1.8.12'; 'com.unity.collections' = '1.2.4'; 'com.unity.mathematics' = '1.2.6'; 'com.unity.modules.animation' = '1.0.0' } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Encoding utf8NoBOM
"m_EditorVersion: 2022.3.22f1`nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)" | Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Encoding utf8NoBOM
$run = [ordered]@{ schemaVersion = 1; runRoot = $runRoot; repository = $repository; inventorySha256 = Get-Sha $inventoryPath; tools = $toolRecords; requestedTags = $Tags; complete = $false; releases = @() }
Write-Host "Later fixture evidence: $runRoot"
try {
    foreach ($tag in $Tags) {
        $record = $published | Where-Object { $_.tag -ceq $tag }
        $commit = Git-Text @('rev-parse', "$tag^{commit}")
        if ($commit -cne $record.commit) { throw "Published tag moved: $tag" }
        $package = Git-Text @('show', "$commit`:package.json") | ConvertFrom-Json
        if ($package.version -cne $record.packageVersion) { throw "Package version mismatch: $tag" }
        $runtime = Join-Path $project 'Assets\HistoricalRuntime'
        Remove-OwnedTree $runtime (Join-Path $project 'Assets')
        New-Item -ItemType Directory -Path $runtime | Out-Null
        # A new file layout may otherwise leave Bee invoking removed compiler inputs.
        Remove-OwnedTree (Join-Path $project 'Library\Bee') (Join-Path $project 'Library')
        Remove-OwnedTree (Join-Path $project 'Library\ScriptAssemblies') (Join-Path $project 'Library')
        $archive = Join-Path $runRoot ("runtime-$tag.tar")
        & git -C $repository archive --format=tar --output=$archive $commit Runtime
        if ($LASTEXITCODE -ne 0) { throw "Runtime archive failed: $tag" }
        & tar -xf $archive -C $runtime
        if ($LASTEXITCODE -ne 0) { throw "Runtime extraction failed: $tag" }
        $log = Join-Path $runRoot ("unity-$tag.log")
        $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
            '-executeMethod', 'LaterReleaseFixtureGenerator.Generate', '-fixtureTag', $tag,
            '-fixtureCommit', $commit, '-fixturePackageVersion', $record.packageVersion, '-logFile', ('"' + $log + '"'))
        $started = Get-Date
        $peak = 0.0
        $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            while (-not $process.WaitForExit(1000)) {
                $process.Refresh()
                $peak = [Math]::Max($peak, $process.PrivateMemorySize64 / 1MB)
                if ($peak -gt $MaxPrivateMb -or ((Get-Date) - $started).TotalSeconds -gt $TimeoutSeconds) { throw "Owned Unity exceeded time/memory limit: $tag" }
            }
        } finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        }
        $run.releases += [ordered]@{ tag = $tag; commit = $commit; exitCode = $process.ExitCode; log = $log; peakPrivateMb = $peak; elapsedSeconds = ((Get-Date) - $started).TotalSeconds; runtimeArchiveSha256 = Get-Sha $archive }
        if ($process.ExitCode -ne 0) { throw "Unity failed for $tag; inspect $log" }
        if (-not (Select-String -LiteralPath $log -SimpleMatch "LATER_RELEASE_FIXTURES_SUCCEEDED tag=$tag commit=$commit" -Quiet)) { throw "Unity omitted success marker: $tag" }
        Assert-Corpus (Join-Path $project "Assets\Generated\LaterReleases\$tag") $record $toolRecords
        Write-Host "Verified $tag ($($run.releases.Count)/$($Tags.Count))"
    }
    foreach ($tool in $toolRecords) {
        if ((Get-Sha (Join-Path $repository $tool.path)) -cne $tool.sha256) { throw 'Helper changed during generation.' }
    }
    $run.complete = $true
} finally {
    $run | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'run.json') -Encoding utf8NoBOM
    Write-Host "Preserved isolated generation evidence at $runRoot"
}
