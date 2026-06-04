param(
    [string]$ResultsPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
    throw "ResultsPath is required."
}

if (-not (Test-Path -LiteralPath $ResultsPath)) {
    throw "Coverage results path was not found: $ResultsPath"
}

$coverageXmlFiles = Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter "*.xml" |
    Where-Object { $_.Name -notmatch "(?i)test[-_]?results" }

if (-not $coverageXmlFiles) {
    throw "No coverage XML files were found under $ResultsPath"
}

$htmlFiles = Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter "*.html" |
    Where-Object { $_.Name -match "(?i)^(index|summary|coverage).*\.html$" }

if (-not $htmlFiles) {
    throw "No generated coverage HTML report was found under $ResultsPath"
}

Write-Host "Coverage XML artifacts:"
$coverageXmlFiles | ForEach-Object { Write-Host "  $($_.FullName)" }

Write-Host "Coverage HTML artifacts:"
$htmlFiles | ForEach-Object { Write-Host "  $($_.FullName)" }
