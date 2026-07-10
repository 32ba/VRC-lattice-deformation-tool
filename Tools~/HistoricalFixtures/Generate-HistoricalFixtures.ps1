[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
    [string[]]$Tags = @(
        '0.0.1', '0.0.2', '0.0.3', '0.0.4', '0.0.5', '0.0.6',
        '1.0.0', '1.0.1', '1.1.0', '1.2.0', '1.2.1', '1.3.0',
        '1.3.1', '1.4.0'
    ),
    [switch]$KeepTemp
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $candidateFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escaped its allowed parent. Candidate=$candidateFull Parent=$parentFull"
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
        Remove-Item -LiteralPath $LiteralPath -Recurse -Force
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

$script:RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$generatorPath = Join-Path $PSScriptRoot 'HistoricalFixtureGenerator.cs'
$fixtureRoot = Join-Path $script:RepositoryRoot 'Tests\Editor\Fixtures\HistoricalReleases'
$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = Join-Path $systemTemp ("vrc-lattice-historical-fixtures-" + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $tempRoot 'UnityProject'
$ownsTemp = $false

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity 2022.3.22f1 was not found: $UnityPath"
}
if (-not (Test-Path -LiteralPath $generatorPath -PathType Leaf)) {
    throw "Generator source is missing: $generatorPath"
}

$actualTags = @(Invoke-GitText tag --sort=version:refname) -split "`n"
foreach ($tag in $Tags) {
    if ($tag -notin $actualTags) {
        throw "Required release tag does not exist: $tag"
    }
}

$generatorSha = (Get-FileHash -LiteralPath $generatorPath -Algorithm SHA256).Hash.ToLowerInvariant()

try {
    Assert-ChildPath -Candidate $tempRoot -Parent $systemTemp -Description 'Temporary project root'
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Assets\Editor') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Packages') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'ProjectSettings') -Force | Out-Null
    $ownsTemp = $true

    Copy-Item -LiteralPath $generatorPath -Destination (Join-Path $projectRoot 'Assets\Editor\HistoricalFixtureGenerator.cs')

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

    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

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

        $runtimeRoot = Join-Path $projectRoot 'Assets\HistoricalRuntime'
        Remove-VerifiedTree -LiteralPath $runtimeRoot -AllowedParent (Join-Path $projectRoot 'Assets') -Description 'Historical Runtime replacement'
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

        if ($tag -eq '1.2.1') {
            # 1.2.1 moved Runtime sources under Runtime/MeshDeformer while retaining the
            # same asmdef and script GUIDs. Clear Unity's compiled DAG at that one layout
            # boundary so Bee cannot invoke csc with deleted pre-1.2.1 source paths.
            Remove-VerifiedTree -LiteralPath (Join-Path $projectRoot 'Library\Bee') -AllowedParent (Join-Path $projectRoot 'Library') -Description 'Unity Bee layout-boundary reset'
            Remove-VerifiedTree -LiteralPath (Join-Path $projectRoot 'Library\ScriptAssemblies') -AllowedParent (Join-Path $projectRoot 'Library') -Description 'Unity ScriptAssemblies layout-boundary reset'
        }

        $archivePath = Join-Path $tempRoot ("runtime-" + $tag.Replace('.', '_') + '.tar')
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

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

        $generatedTagRoot = Join-Path $projectRoot "Assets\Generated\HistoricalReleases\$tag"
        $generatedTagMeta = $generatedTagRoot + '.meta'
        if (-not (Test-Path -LiteralPath $generatedTagRoot -PathType Container)) {
            throw "Unity did not create the expected output directory: $generatedTagRoot"
        }
        if (-not (Test-Path -LiteralPath $generatedTagMeta -PathType Leaf)) {
            throw "Unity did not create the expected tag directory meta: $generatedTagMeta"
        }

        $targetTagRoot = Join-Path $fixtureRoot $tag
        $targetTagMeta = $targetTagRoot + '.meta'
        Remove-VerifiedTree -LiteralPath $targetTagRoot -AllowedParent $fixtureRoot -Description 'Repository fixture replacement'
        if (Test-Path -LiteralPath $targetTagMeta) {
            Remove-Item -LiteralPath $targetTagMeta -Force
        }
        Copy-Item -LiteralPath $generatedTagRoot -Destination $targetTagRoot -Recurse
        Copy-Item -LiteralPath $generatedTagMeta -Destination $targetTagMeta

        $manifestPath = Join-Path $targetTagRoot 'manifest.json'
        $manifestMetaPath = $manifestPath + '.meta'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $manifestMetaPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $targetTagMeta -PathType Leaf)) {
            throw "Copied fixture is missing manifest/meta or tag directory meta for $tag."
        }
    }

    Write-Host "Historical fixtures generated successfully at $fixtureRoot"
}
finally {
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
