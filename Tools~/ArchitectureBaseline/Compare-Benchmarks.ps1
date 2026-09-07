param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CandidatePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [double]$TimingTolerancePercent = 10
)
$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
function Get-Percentile($Samples, [double]$Quantile) {
    $values = @($Samples.milliseconds | Sort-Object)
    $values[[math]::Max(0, [int][math]::Ceiling($values.Count * $Quantile) - 1)]
}
foreach ($document in @($baseline, $candidate)) {
    if (-not $document.complete -or $document.error -or -not $document.gcProfilerSupported -or
        $document.allocationCalibrationBytes -lt 16384) { throw 'Incomplete or uncalibrated measurement.' }
    foreach ($scenario in $document.scenarios) {
        if ($scenario.unchanged.Count -ne $document.sampleCount -or $scenario.edited.Count -ne $document.sampleCount) {
            throw 'Missing measurement samples.'
        }
        foreach ($sample in @($scenario.firstEvaluation) + $scenario.unchanged + $scenario.edited) {
            if (-not $sample.allocationSamplesComplete -or $sample.milliseconds -le 0 -or
                [double]::IsNaN($sample.milliseconds) -or [double]::IsInfinity($sample.milliseconds) -or
                $sample.allocatedBytes -lt 0) { throw 'Invalid measurement sample.' }
        }
        foreach ($phase in @('unchanged', 'edited')) {
            foreach ($percentile in @(50, 95)) {
                $declared = $scenario.($phase + 'P' + $percentile)
                $computed = Get-Percentile $scenario.$phase ($percentile / 100.0)
                if ($null -eq $declared -or [double]::IsNaN($declared) -or [double]::IsInfinity($declared) -or
                    [math]::Abs($declared - $computed) -gt 1e-6) { throw 'Summary differs from its samples.' }
            }
        }
    }
}
foreach ($field in @('unityVersion', 'processor', 'processorCount', 'operatingSystem', 'sampleCount', 'scenarioFilter')) {
    if ($baseline.$field -ne $candidate.$field) { throw ('Measurement environment differs: ' + $field) }
}
if (($baseline.scenarios.name | ConvertTo-Json -Compress) -ne ($candidate.scenarios.name | ConvertTo-Json -Compress)) {
    throw 'Scenario lists differ.'
}
$rows = foreach ($after in $candidate.scenarios) {
    $before = $baseline.scenarios | Where-Object name -EQ $after.name
    foreach ($field in @('vertices', 'groups', 'generatedBlendShape', 'recalculateNormals', 'recalculateTangents', 'recalculateBounds')) {
        if ($before.$field -ne $after.$field) { throw ('Scenario input differs: ' + $after.name + ' ' + $field) }
    }
    foreach ($phase in @('unchanged', 'edited')) {
        $previous = $before.$phase
        $current = $after.$phase
        $previousP95 = $before.($phase + 'P95')
        $currentP95 = $after.($phase + 'P95')
        $change = 100 * ($currentP95 / $previousP95 - 1)
        $beforeGc = ($previous.allocatedBytes | Measure-Object -Maximum).Maximum
        $afterGc = ($current.allocatedBytes | Measure-Object -Maximum).Maximum
        [ordered]@{
            scenario = $after.name; phase = $phase
            baselineP50Ms = $before.($phase + 'P50'); candidateP50Ms = $after.($phase + 'P50')
            baselineP95Ms = $previousP95; candidateP95Ms = $currentP95; p95ChangePercent = $change
            baselineMaxMs = ($previous.milliseconds | Measure-Object -Maximum).Maximum
            candidateMaxMs = ($current.milliseconds | Measure-Object -Maximum).Maximum
            baselineMaxGcBytes = $beforeGc; candidateMaxGcBytes = $afterGc
            timingRegression = $change -gt $TimingTolerancePercent
            allocationIncrease = $afterGc -gt $beforeGc
            candidateMeshCountDeltaAfterDispose = $after.meshCountDeltaAfterDispose
        }
    }
}
$report = [ordered]@{
    schemaVersion = 1; baselineCommit = $baseline.commit; candidateCommit = $candidate.commit
    baselineSha256 = (Get-FileHash -LiteralPath $BaselinePath -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateSha256 = (Get-FileHash -LiteralPath $CandidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    timingTolerancePercent = $TimingTolerancePercent
    timingRegressionCount = @($rows | Where-Object { $_.timingRegression }).Count
    allocationIncreaseCount = @($rows | Where-Object { $_.allocationIncrease }).Count
    retainedMeshScenarioCount = @($candidate.scenarios | Where-Object meshCountDeltaAfterDispose -NE 0).Count
    limitations = ('Headless synchronous evaluation; ' + $baseline.sampleCount + ' samples have limited statistical power; p95 uses nearest rank. No Scene View, input latency, or total native allocation claim.')
    comparisons = $rows
}
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $OutputPath -Encoding utf8
[pscustomobject]$report | Select-Object timingRegressionCount,allocationIncreaseCount,retainedMeshScenarioCount | ConvertTo-Json
