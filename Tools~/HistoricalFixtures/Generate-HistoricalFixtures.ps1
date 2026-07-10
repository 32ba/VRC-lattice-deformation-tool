[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
    [string[]]$Tags = @(
        '0.0.1', '0.0.2', '0.0.3', '0.0.4', '0.0.5', '0.0.6',
        '1.0.0', '1.0.1', '1.1.0', '1.2.0', '1.2.1', '1.3.0',
        '1.3.1', '1.4.0'
    ),
    [switch]$NoPublish,
    [switch]$KeepTemp
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:RequiredTags = @(
    '0.0.1', '0.0.2', '0.0.3', '0.0.4', '0.0.5', '0.0.6',
    '1.0.0', '1.0.1', '1.1.0', '1.2.0', '1.2.1', '1.3.0',
    '1.3.1', '1.4.0'
)
$script:LegacyBrushTags = @('1.2.1', '1.3.0', '1.3.1', '1.4.0')
$script:GeneratorManifestPath = 'Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs'
$script:RunnerManifestPath = 'Tools~/HistoricalFixtures/Generate-HistoricalFixtures.ps1'
$script:UnityVersion = '2022.3.22f1'
$script:MetaGuidScheme = 'sha256-v1:tag/relative-asset-path'
$script:MetaGuidNamespace = 'net.32ba.lattice-deformation-tool/historical-fixture-meta-guid/v1'
$script:PrefabFileIdScheme = 'sha256-v1:tag/relative-prefab/class/ordinal'
$script:PrefabFileIdNamespace = 'net.32ba.lattice-deformation-tool/historical-fixture-prefab-file-id/v1'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPrefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidateFull.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escaped its allowed parent. Candidate=$candidateFull Parent=$parentFull"
    }
}

function Assert-OrdinaryDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Container)) {
        throw "$Description is not a directory: $LiteralPath"
    }
    $item = Get-Item -LiteralPath $LiteralPath -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a reparse point: $LiteralPath"
    }
}

function Remove-VerifiedTree {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$AllowedParent,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-ChildPath -Candidate $LiteralPath -Parent $AllowedParent -Description $Description
    if (Test-Path -LiteralPath $LiteralPath) {
        Assert-OrdinaryDirectory -LiteralPath $LiteralPath -Description $Description
        Remove-Item -LiteralPath $LiteralPath -Recurse -Force
    }
}

function Remove-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$AllowedParent,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-ChildPath -Candidate $LiteralPath -Parent $AllowedParent -Description $Description
    if (Test-Path -LiteralPath $LiteralPath) {
        if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
            throw "$Description is not a file: $LiteralPath"
        }
        Remove-Item -LiteralPath $LiteralPath -Force
    }
}

function Move-VerifiedDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$AllowedParent,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-ChildPath -Candidate $Source -Parent $AllowedParent -Description "$Description source"
    Assert-ChildPath -Candidate $Destination -Parent $AllowedParent -Description "$Description destination"
    Assert-OrdinaryDirectory -LiteralPath $Source -Description "$Description source"
    if (Test-Path -LiteralPath $Destination) {
        throw "$Description destination already exists: $Destination"
    }
    Move-Item -LiteralPath $Source -Destination $Destination
}

function Get-LowerSha256 {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Cannot hash a missing file: $LiteralPath"
    }
    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DeterministicMetaGuid {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$RelativeAssetPath
    )

    $identity = $script:MetaGuidNamespace + "`n" + $Tag + "`n" + $RelativeAssetPath.Replace('\', '/')
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($identity))
        return -join ($hash[0..15] | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-DeterministicMetaGuid {
    param(
        [Parameter(Mandatory = $true)][string]$MetaPath,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$RelativeAssetPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$SeenGuids,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $MetaPath -PathType Leaf)) {
        throw "$Description deterministic meta is missing: $MetaPath"
    }
    $matches = [regex]::Matches(
        (Get-Content -LiteralPath $MetaPath -Raw),
        '(?m)^guid: ([0-9a-fA-F]{32})\r?$')
    if ($matches.Count -ne 1) {
        throw "$Description meta must contain exactly one GUID: $MetaPath"
    }
    $actual = $matches[0].Groups[1].Value
    $expected = Get-DeterministicMetaGuid -Tag $Tag -RelativeAssetPath $RelativeAssetPath
    if ($actual -cne $expected) {
        throw "$Description deterministic meta GUID mismatch. Expected=$expected Actual=$actual Path=$MetaPath"
    }
    if (-not $SeenGuids.Add($actual)) {
        throw "$Description deterministic meta GUID is duplicated: $actual"
    }
}

function Get-DeterministicPrefabFileId {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$RelativePrefabPath,
        [Parameter(Mandatory = $true)][string]$ClassId,
        [Parameter(Mandatory = $true)][int]$Ordinal
    )

    $identity = $script:PrefabFileIdNamespace + "`n" + $Tag + "`n" +
        $RelativePrefabPath.Replace('\', '/') + "`n" + $ClassId + "`n" +
        $Ordinal.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($identity))
        [UInt64]$value = 0
        for ($index = 0; $index -lt 8; $index++) {
            $value = [UInt64](($value * 256) + $hash[$index])
        }
        $value = ($value -band [UInt64]0x3FFFFFFFFFFFFFFF) -bor [UInt64]0x4000000000000000
        return $value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-DeterministicPrefabFileIds {
    param(
        [Parameter(Mandatory = $true)][string]$PrefabPath,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$RelativePrefabPath,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $PrefabPath -PathType Leaf)) {
        throw "$Description deterministic prefab is missing: $PrefabPath"
    }
    $anchors = [regex]::Matches(
        (Get-Content -LiteralPath $PrefabPath -Raw),
        '(?m)^--- !u!(\d+) &(\d+)\r?$')
    if ($anchors.Count -eq 0) {
        throw "$Description prefab contains no local object anchors: $PrefabPath"
    }
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    for ($ordinal = 0; $ordinal -lt $anchors.Count; $ordinal++) {
        $classId = $anchors[$ordinal].Groups[1].Value
        $actual = $anchors[$ordinal].Groups[2].Value
        $expected = Get-DeterministicPrefabFileId -Tag $Tag -RelativePrefabPath $RelativePrefabPath -ClassId $classId -Ordinal $ordinal
        if ($actual -cne $expected) {
            throw "$Description deterministic prefab fileID mismatch at anchor $ordinal. Expected=$expected Actual=$actual Path=$PrefabPath"
        }
        if (-not $seen.Add($actual)) {
            throw "$Description deterministic prefab fileID is duplicated: $actual"
        }
    }
}

function Invoke-GitText {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $output = & git -C $script:RepositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git failed: git -C `"$script:RepositoryRoot`" $($Arguments -join ' ')"
    }
    return ($output -join "`n").Trim()
}

function Assert-ExactStringSet {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $actualUnique = @($Actual | Sort-Object -Unique)
    $expectedUnique = @($Expected | Sort-Object -Unique)
    $difference = @(Compare-Object -ReferenceObject $expectedUnique -DifferenceObject $actualUnique)
    if ($Actual.Count -ne $actualUnique.Count -or
        $Expected.Count -ne $expectedUnique.Count -or
        $difference.Count -ne 0) {
        throw "$Description differs. Expected=[$($expectedUnique -join ', ')] Actual=[$($actualUnique -join ', ')]"
    }
}

function Assert-ManifestValue {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Property,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $member = $Manifest.PSObject.Properties[$Property]
    if ($null -eq $member -or [string]$member.Value -cne $Expected) {
        $actual = if ($null -eq $member) { '<missing>' } else { [string]$member.Value }
        throw "$Description $Property mismatch. Expected=$Expected Actual=$actual"
    }
}

function Assert-GeneratedCorpus {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object[]]$ReleaseRecords,
        [Parameter(Mandatory = $true)][string]$GeneratorSha,
        [Parameter(Mandatory = $true)][string]$RunnerSha,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-OrdinaryDirectory -LiteralPath $Root -Description $Description
    $seenMetaGuids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $expectedTags = @($ReleaseRecords | ForEach-Object { [string]$_.Tag })
    $actualDirectories = @(Get-ChildItem -LiteralPath $Root -Directory -Force | ForEach-Object { $_.Name })
    Assert-ExactStringSet -Actual $actualDirectories -Expected $expectedTags -Description "$Description tag directories"
    $actualRootFiles = @(Get-ChildItem -LiteralPath $Root -File -Force | ForEach-Object { $_.Name })
    $expectedRootFiles = @($expectedTags | ForEach-Object { $_ + '.meta' })
    Assert-ExactStringSet -Actual $actualRootFiles -Expected $expectedRootFiles -Description "$Description tag metas"

    foreach ($release in $ReleaseRecords) {
        $tag = [string]$release.Tag
        $tagRoot = Join-Path $Root $tag
        $tagMeta = $tagRoot + '.meta'
        Assert-OrdinaryDirectory -LiteralPath $tagRoot -Description "$Description/$tag"
        if (-not (Test-Path -LiteralPath $tagMeta -PathType Leaf)) {
            throw "$Description/$tag directory meta is missing: $tagMeta"
        }
        Assert-DeterministicMetaGuid -MetaPath $tagMeta -Tag $tag -RelativeAssetPath '.' -SeenGuids $seenMetaGuids -Description "$Description/$tag directory"

        $manifestPath = Join-Path $tagRoot 'manifest.json'
        $manifestMetaPath = $manifestPath + '.meta'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $manifestMetaPath -PathType Leaf)) {
            throw "$Description/$tag manifest or manifest meta is missing."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-ManifestValue $manifest 'tag' $tag "$Description/$tag"
        Assert-ManifestValue $manifest 'commitSha' ([string]$release.CommitSha) "$Description/$tag"
        Assert-ManifestValue $manifest 'packageVersion' ([string]$release.PackageVersion) "$Description/$tag"
        Assert-ManifestValue $manifest 'unityVersion' $script:UnityVersion "$Description/$tag"
        Assert-ManifestValue $manifest 'generator' $script:GeneratorManifestPath "$Description/$tag"
        Assert-ManifestValue $manifest 'generatorSha256' $GeneratorSha "$Description/$tag"
        Assert-ManifestValue $manifest 'runner' $script:RunnerManifestPath "$Description/$tag"
        Assert-ManifestValue $manifest 'runnerSha256' $RunnerSha "$Description/$tag"
        Assert-ManifestValue $manifest 'metaGuidScheme' $script:MetaGuidScheme "$Description/$tag"
        Assert-ManifestValue $manifest 'prefabFileIdScheme' $script:PrefabFileIdScheme "$Description/$tag"
        Assert-ManifestValue $manifest 'generationMode' 'unity-batchmode-tag-checkout' "$Description/$tag"
        Assert-ManifestValue $manifest 'goldenOutputSource' 'historical-runtime-deform' "$Description/$tag"

        $expectedFixtureNames = @('lattice')
        if ($tag -eq '0.0.1') {
            $expectedFixtureNames += 'lattice-world'
        }
        if ($tag -in $script:LegacyBrushTags) {
            $expectedFixtureNames += 'lattice-remove-active-last', 'legacy-brush'
        }
        $manifestFixtures = @($manifest.fixtures)
        $actualFixtureNames = @($manifestFixtures | ForEach-Object { [string]$_.kind })
        Assert-ExactStringSet -Actual $actualFixtureNames -Expected $expectedFixtureNames -Description "$Description/$tag fixture kinds"

        $requiredFiles = @('source.asset', 'source.asset.meta', 'fixture.prefab', 'fixture.prefab.meta', 'expected.json', 'expected.json.meta')
        foreach ($fixture in $manifestFixtures) {
            $kind = [string]$fixture.kind
            $expectedPrefab = switch ($kind) {
                'lattice' { 'fixture.prefab' }
                'lattice-world' { 'fixture-world.prefab' }
                'lattice-remove-active-last' { 'fixture-remove-active-last.prefab' }
                'legacy-brush' { 'legacy-brush.prefab' }
                default { throw "$Description/$tag has an unknown fixture kind: $kind" }
            }
            $expectedJson = switch ($kind) {
                'lattice' { 'expected.json' }
                'lattice-world' { 'expected-world.json' }
                'lattice-remove-active-last' { 'expected-remove-active-last.json' }
                'legacy-brush' { 'legacy-brush-expected.json' }
            }
            $expectedGoldenSource = if ($kind -eq 'legacy-brush') {
                'BrushDeformer.Deform(false)'
            }
            else {
                'LatticeDeformer.Deform(false)'
            }
            Assert-ManifestValue $fixture 'prefab' $expectedPrefab "$Description/$tag/$kind"
            Assert-ManifestValue $fixture 'expected' $expectedJson "$Description/$tag/$kind"
            Assert-ManifestValue $fixture 'source' 'source.asset' "$Description/$tag/$kind"
            Assert-ManifestValue $fixture 'goldenOutputSource' $expectedGoldenSource "$Description/$tag/$kind"
            $requiredFiles += $expectedPrefab, ($expectedPrefab + '.meta'), $expectedJson, ($expectedJson + '.meta')
        }
        $requiredFiles = @($requiredFiles | Sort-Object -Unique)

        $manifestFiles = @($manifest.files)
        $listedFiles = @($manifestFiles | ForEach-Object { [string]$_.path })
        Assert-ExactStringSet -Actual $listedFiles -Expected $requiredFiles -Description "$Description/$tag manifest files"
        foreach ($file in $manifestFiles) {
            $relativePath = [string]$file.path
            if ([System.IO.Path]::GetFileName($relativePath) -cne $relativePath) {
                throw "$Description/$tag manifest file escaped its tag directory: $relativePath"
            }
            $absolutePath = Join-Path $tagRoot $relativePath
            $actualSha = Get-LowerSha256 -LiteralPath $absolutePath
            $expectedSha = [string]$file.sha256
            if ($actualSha -cne $expectedSha) {
                throw "$Description/$tag SHA-256 mismatch for $relativePath. Expected=$expectedSha Actual=$actualSha"
            }
        }

        $expectedTagFiles = @($requiredFiles + @('manifest.json', 'manifest.json.meta'))
        $actualTagFiles = @(Get-ChildItem -LiteralPath $tagRoot -File -Force | ForEach-Object { $_.Name })
        Assert-ExactStringSet -Actual $actualTagFiles -Expected $expectedTagFiles -Description "$Description/$tag on-disk files"
        foreach ($metaName in @($actualTagFiles | Where-Object { $_.EndsWith('.meta', [System.StringComparison]::Ordinal) })) {
            $relativeAssetPath = $metaName.Substring(0, $metaName.Length - '.meta'.Length)
            Assert-DeterministicMetaGuid -MetaPath (Join-Path $tagRoot $metaName) -Tag $tag -RelativeAssetPath $relativeAssetPath -SeenGuids $seenMetaGuids -Description "$Description/$tag/$relativeAssetPath"
        }
        foreach ($prefabName in @($actualTagFiles | Where-Object { $_.EndsWith('.prefab', [System.StringComparison]::Ordinal) })) {
            Assert-DeterministicPrefabFileIds -PrefabPath (Join-Path $tagRoot $prefabName) -Tag $tag -RelativePrefabPath $prefabName -Description "$Description/$tag/$prefabName"
        }
        if (@(Get-ChildItem -LiteralPath $tagRoot -Directory -Force).Count -ne 0) {
            throw "$Description/$tag must not contain nested directories."
        }
    }
}

if ($null -eq $PSCommandPath) {
    throw 'The historical fixture runner must be executed as a script file, not dot-sourced.'
}
$script:RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$generatorPath = Join-Path $PSScriptRoot 'HistoricalFixtureGenerator.cs'
$runnerPath = [System.IO.Path]::GetFullPath($PSCommandPath)
$expectedRunnerPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Generate-HistoricalFixtures.ps1'))
if (-not $runnerPath.Equals($expectedRunnerPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected runner source path. Expected=$expectedRunnerPath Actual=$runnerPath"
}
$fixtureRoot = Join-Path $script:RepositoryRoot 'Tests\Editor\Fixtures\HistoricalReleases'
$fixtureParent = Split-Path -Parent $fixtureRoot
$fixtureMeta = $fixtureRoot + '.meta'
$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$operationId = [Guid]::NewGuid().ToString('N')
$tempRoot = Join-Path $systemTemp ("vrc-lattice-historical-fixtures-" + $operationId)
$projectRoot = Join-Path $tempRoot 'UnityProject'
$repositoryStagingRoot = Join-Path $fixtureParent ('.HistoricalReleases.staging-' + $operationId + '~')
$repositoryBackupRoot = Join-Path $fixtureParent ('.HistoricalReleases.backup-' + $operationId + '~')
$ownsTemp = $false
$stagingCopied = $false
$backupCreated = $false
$newCorpusInstalled = $false

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity 2022.3.22f1 was not found: $UnityPath"
}
if (-not (Test-Path -LiteralPath $generatorPath -PathType Leaf)) {
    throw "Generator source is missing: $generatorPath"
}
$lastTagIndex = -1
foreach ($tag in $Tags) {
    $tagIndex = [Array]::IndexOf($script:RequiredTags, $tag)
    if ($tagIndex -lt 0) {
        throw "Unsupported historical fixture tag: $tag"
    }
    if ($tagIndex -le $lastTagIndex) {
        throw "Tags must be unique and follow published release order: $($Tags -join ', ')"
    }
    $lastTagIndex = $tagIndex
}
if (-not $NoPublish) {
    Assert-OrdinaryDirectory -LiteralPath $fixtureRoot -Description 'Existing repository fixture corpus'
    if (-not (Test-Path -LiteralPath $fixtureMeta -PathType Leaf)) {
        throw "Existing HistoricalReleases.meta is missing: $fixtureMeta"
    }
    Assert-ExactStringSet -Actual @($Tags) -Expected $script:RequiredTags -Description 'Published tag set'
}
elseif ($Tags.Count -eq 0) {
    throw 'At least one tag is required for a staging-only generation.'
}

$actualTags = @(Invoke-GitText tag --sort=version:refname) -split "`n"
foreach ($tag in $Tags) {
    if ($tag -notin $actualTags) {
        throw "Required release tag does not exist: $tag"
    }
}

$generatorSha = Get-LowerSha256 -LiteralPath $generatorPath
$runnerSha = Get-LowerSha256 -LiteralPath $runnerPath
$fixtureMetaShaBefore = if (-not $NoPublish) { Get-LowerSha256 -LiteralPath $fixtureMeta } else { $null }
$releaseRecords = @()

try {
    Assert-ChildPath -Candidate $tempRoot -Parent $systemTemp -Description 'Temporary project root'
    Assert-ChildPath -Candidate $repositoryStagingRoot -Parent $fixtureParent -Description 'Repository staging root'
    Assert-ChildPath -Candidate $repositoryBackupRoot -Parent $fixtureParent -Description 'Repository backup root'
    if ((Test-Path -LiteralPath $repositoryStagingRoot) -or
        (Test-Path -LiteralPath $repositoryBackupRoot)) {
        throw 'Unexpected fixture staging/backup collision.'
    }

    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Assets\Editor') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Packages') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'ProjectSettings') -Force | Out-Null
    $ownsTemp = $true

    Copy-Item -LiteralPath $generatorPath -Destination (Join-Path $projectRoot 'Assets\Editor\HistoricalFixtureGenerator.cs')
    Copy-Item -LiteralPath $runnerPath -Destination (Join-Path $projectRoot 'Assets\Editor\Generate-HistoricalFixtures.ps1')

    @'
{
  "dependencies": {
    "com.unity.burst": "1.8.12",
    "com.unity.collections": "1.2.4",
    "com.unity.mathematics": "1.2.6"
  }
}
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'Packages\manifest.json') -Encoding utf8NoBOM

    @'
m_EditorVersion: 2022.3.22f1
m_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8NoBOM

    foreach ($tag in $Tags) {
        $commitSha = Invoke-GitText rev-parse "$tag^{commit}"
        if ($commitSha -notmatch '^[0-9a-f]{40}$') {
            throw "Tag did not peel to a commit SHA: $tag -> $commitSha"
        }

        $packageJson = Invoke-GitText show "$commitSha`:package.json" | ConvertFrom-Json
        $packageVersion = [string]$packageJson.version
        if ($packageVersion -ne $tag) {
            throw "Tag/package version mismatch: tag=$tag package=$packageVersion commit=$commitSha"
        }
        $releaseRecords += [pscustomobject]@{
            Tag = $tag
            CommitSha = $commitSha
            PackageVersion = $packageVersion
        }

        $runtimeRoot = Join-Path $projectRoot 'Assets\HistoricalRuntime'
        Remove-VerifiedTree -LiteralPath $runtimeRoot -AllowedParent (Join-Path $projectRoot 'Assets') -Description 'Historical Runtime replacement'
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

        if ($tag -eq '1.2.1') {
            # Clear Unity's compiled DAG at the Runtime layout boundary so Bee cannot
            # invoke csc with source paths removed by the tag replacement.
            Remove-VerifiedTree -LiteralPath (Join-Path $projectRoot 'Library\Bee') -AllowedParent (Join-Path $projectRoot 'Library') -Description 'Unity Bee layout-boundary reset'
            Remove-VerifiedTree -LiteralPath (Join-Path $projectRoot 'Library\ScriptAssemblies') -AllowedParent (Join-Path $projectRoot 'Library') -Description 'Unity ScriptAssemblies layout-boundary reset'
        }

        $archivePath = Join-Path $tempRoot ("runtime-" + $tag.Replace('.', '_') + '.tar')
        Remove-VerifiedFile -LiteralPath $archivePath -AllowedParent $tempRoot -Description 'Historical Runtime archive replacement'
        & git -C $script:RepositoryRoot archive --format=tar --output=$archivePath $commitSha Runtime
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            throw "Could not archive Runtime for $tag ($commitSha)."
        }

        & tar -xf $archivePath -C $runtimeRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Could not extract Runtime archive for $tag."
        }

        $logPath = Join-Path $tempRoot ("unity-" + $tag.Replace('.', '_') + '.log')
        $unityArguments = @(
            '-batchmode',
            '-nographics',
            '-quit',
            '-projectPath', $projectRoot,
            '-executeMethod', 'HistoricalFixtureGenerator.Generate',
            '-fixtureTag', $tag,
            '-fixtureCommit', $commitSha,
            '-fixturePackageVersion', $packageVersion,
            '-fixtureGeneratorSha', $generatorSha,
            '-fixtureRunnerSha', $runnerSha,
            '-logFile', $logPath
        )

        Write-Host "Generating historical fixture tag=$tag commit=$commitSha"
        $process = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) {
            $tail = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 120 } else { @('<missing log>') }
            throw "Unity generation failed for $tag with exit code $($process.ExitCode).`n$($tail -join "`n")"
        }

        $successMarker = "HISTORICAL_FIXTURE_GENERATION_SUCCEEDED tag=$tag commit=$commitSha"
        if (-not (Select-String -LiteralPath $logPath -SimpleMatch $successMarker -Quiet)) {
            throw "Unity exited without the fixture success marker for $tag. Log: $logPath"
        }
    }

    $generatedCorpusRoot = Join-Path $projectRoot 'Assets\Generated\HistoricalReleases'
    Assert-GeneratedCorpus -Root $generatedCorpusRoot -ReleaseRecords $releaseRecords -GeneratorSha $generatorSha -RunnerSha $runnerSha -Description 'Verified Unity staging corpus'
    if ((Get-LowerSha256 -LiteralPath $generatorPath) -cne $generatorSha -or
        (Get-LowerSha256 -LiteralPath $runnerPath) -cne $runnerSha) {
        throw 'Generator or runner source changed while fixtures were being generated; refusing to publish stale provenance.'
    }

    if ($NoPublish) {
        Write-Host "Historical fixture staging validation succeeded for tags: $($Tags -join ', ')"
        return
    }

    # Only after every tag has generated and validated do we create a repository-side
    # staging sibling. The live corpus and its existing .meta remain untouched so far.
    New-Item -ItemType Directory -Path $repositoryStagingRoot | Out-Null
    $stagingCopied = $true
    foreach ($item in Get-ChildItem -LiteralPath $generatedCorpusRoot -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $repositoryStagingRoot -Recurse
    }
    Assert-GeneratedCorpus -Root $repositoryStagingRoot -ReleaseRecords $releaseRecords -GeneratorSha $generatorSha -RunnerSha $runnerSha -Description 'Verified repository staging corpus'
    if ((Get-LowerSha256 -LiteralPath $fixtureMeta) -cne $fixtureMetaShaBefore) {
        throw 'HistoricalReleases.meta changed before the corpus swap.'
    }

    try {
        Move-VerifiedDirectory -Source $fixtureRoot -Destination $repositoryBackupRoot -AllowedParent $fixtureParent -Description 'Historical corpus backup'
        $backupCreated = $true
        Move-VerifiedDirectory -Source $repositoryStagingRoot -Destination $fixtureRoot -AllowedParent $fixtureParent -Description 'Historical corpus install'
        $stagingCopied = $false
        $newCorpusInstalled = $true
        Assert-GeneratedCorpus -Root $fixtureRoot -ReleaseRecords $releaseRecords -GeneratorSha $generatorSha -RunnerSha $runnerSha -Description 'Installed repository corpus'
        if ((Get-LowerSha256 -LiteralPath $fixtureMeta) -cne $fixtureMetaShaBefore) {
            throw 'HistoricalReleases.meta changed during the corpus swap.'
        }
    }
    catch {
        $installFailure = $_
        try {
            if ($backupCreated -and (Test-Path -LiteralPath $fixtureRoot)) {
                Remove-VerifiedTree -LiteralPath $fixtureRoot -AllowedParent $fixtureParent -Description 'Failed historical corpus rollback removal'
                $newCorpusInstalled = $false
            }
            if ($backupCreated -and (Test-Path -LiteralPath $repositoryBackupRoot)) {
                Move-VerifiedDirectory -Source $repositoryBackupRoot -Destination $fixtureRoot -AllowedParent $fixtureParent -Description 'Historical corpus rollback restore'
                $backupCreated = $false
            }
            if ((Get-LowerSha256 -LiteralPath $fixtureMeta) -cne $fixtureMetaShaBefore) {
                throw 'HistoricalReleases.meta changed while rolling back the corpus.'
            }
        }
        catch {
            throw "Corpus installation failed and rollback also failed. Install error: $installFailure Rollback error: $_ Backup=$repositoryBackupRoot"
        }
        throw $installFailure
    }

    Remove-VerifiedTree -LiteralPath $repositoryBackupRoot -AllowedParent $fixtureParent -Description 'Verified historical corpus backup cleanup'
    $backupCreated = $false
    Write-Host "Historical fixtures generated and atomically installed at $fixtureRoot"
}
finally {
    if ($stagingCopied -and (Test-Path -LiteralPath $repositoryStagingRoot)) {
        Remove-VerifiedTree -LiteralPath $repositoryStagingRoot -AllowedParent $fixtureParent -Description 'Repository staging cleanup'
        $stagingCopied = $false
    }
    if ($backupCreated -and (Test-Path -LiteralPath $repositoryBackupRoot)) {
        Write-Warning "A historical fixture backup remains for manual recovery: $repositoryBackupRoot"
    }
    if ($ownsTemp -and -not $KeepTemp) {
        Assert-ChildPath -Candidate $tempRoot -Parent $systemTemp -Description 'Temporary cleanup root'
        if (-not ([System.IO.Path]::GetFileName($tempRoot)).StartsWith('vrc-lattice-historical-fixtures-', [System.StringComparison]::Ordinal)) {
            throw "Refusing to clean an unexpected temporary directory: $tempRoot"
        }
        Remove-VerifiedTree -LiteralPath $tempRoot -AllowedParent $systemTemp -Description 'Temporary Unity project cleanup'
    }
    elseif ($ownsTemp) {
        Write-Host "Temporary Unity project retained at $tempRoot"
    }
}
