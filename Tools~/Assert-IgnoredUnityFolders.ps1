param(
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Resolve-Path (Join-Path $PSScriptRoot "..")
} else {
    $PackagePath = Resolve-Path $PackagePath
}

$PackagePath = [System.IO.Path]::GetFullPath($PackagePath)

$ignoredFolders = Get-ChildItem -LiteralPath $PackagePath -Directory -Force |
    Where-Object { $_.Name.EndsWith("~") -or $_.Name.StartsWith(".") }

$violations = New-Object System.Collections.Generic.List[string]
foreach ($folder in $ignoredFolders) {
    $siblingMeta = "$($folder.FullName).meta"
    if (Test-Path -LiteralPath $siblingMeta) {
        $violations.Add($siblingMeta)
    }

    Get-ChildItem -LiteralPath $folder.FullName -Recurse -Force -Filter "*.meta" -ErrorAction SilentlyContinue |
        ForEach-Object { $violations.Add($_.FullName) }
}

if ($violations.Count -gt 0) {
    throw "Ignored Unity folders must not contain generated .meta files: $($violations -join ', ')"
}

Write-Host "Ignored Unity folder meta check passed: $($ignoredFolders.Name -join ', ')"
