param(
    [string]$TestResultsPath,
    [int]$MinimumTotal = 1,
    [string]$RequiredCategory,
    [int]$RequiredCategoryCount = 0
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TestResultsPath)) {
    throw "TestResultsPath is required."
}

if (-not (Test-Path $TestResultsPath)) {
    throw "Test results file was not found: $TestResultsPath"
}

[xml]$xml = Get-Content -LiteralPath $TestResultsPath -Raw
$root = $xml.DocumentElement
if ($root -eq $null -or $root.Name -ne "test-run") {
    throw "Unexpected test results XML format: $TestResultsPath"
}

function Get-IntAttribute {
    param(
        [System.Xml.XmlElement]$Element,
        [string]$Name
    )

    $attr = $Element.Attributes[$Name]
    if ($attr -eq $null) {
        return 0
    }

    [int]$value = 0
    if (-not [int]::TryParse($attr.Value, [ref]$value)) {
        throw "Invalid integer attribute '$Name' in test results: $($attr.Value)"
    }

    return $value
}

$result = $root.GetAttribute("result")
$total = Get-IntAttribute $root "total"
$passed = Get-IntAttribute $root "passed"
$failed = Get-IntAttribute $root "failed"
$skipped = Get-IntAttribute $root "skipped"
$inconclusive = Get-IntAttribute $root "inconclusive"

Write-Host "Test results: result=$result total=$total passed=$passed failed=$failed skipped=$skipped inconclusive=$inconclusive"

if ($total -lt $MinimumTotal) {
    throw "Test run total $total is below required minimum $MinimumTotal."
}

if (-not [string]::IsNullOrWhiteSpace($RequiredCategory)) {
    if ($RequiredCategoryCount -le 0) {
        throw "RequiredCategoryCount must be greater than zero when RequiredCategory is specified."
    }

    $categoryTests = @(
        $xml.SelectNodes("//test-case") | Where-Object {
            $testCase = $_
            @(
                $testCase.SelectNodes("properties/property") | Where-Object {
                    $_.GetAttribute("name") -eq "Category" -and
                    $_.GetAttribute("value") -eq $RequiredCategory
                }
            ).Count -gt 0
        }
    )

    Write-Host "Required category '$RequiredCategory': found=$($categoryTests.Count) expected=$RequiredCategoryCount"

    if ($categoryTests.Count -ne $RequiredCategoryCount) {
        throw "Required category '$RequiredCategory' contained $($categoryTests.Count) tests; expected exactly $RequiredCategoryCount."
    }

    $categoryFailures = @(
        $categoryTests | Where-Object { $_.GetAttribute("result") -ne "Passed" }
    )
    if ($categoryFailures.Count -ne 0) {
        $failureSummary = $categoryFailures | ForEach-Object {
            "$($_.GetAttribute('fullname'))=$($_.GetAttribute('result'))"
        }
        throw "Required category '$RequiredCategory' did not fully pass: $($failureSummary -join ', ')"
    }
}

if ($result -ne "Passed" -or $failed -ne 0 -or $skipped -ne 0 -or $inconclusive -ne 0 -or $passed -ne $total) {
    throw "Test run did not fully pass. See $TestResultsPath"
}
