param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$Commit,
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
    [int]$Samples = 15,
    [int]$MaxPrivateMb = 6144,
    [int]$TimeoutSeconds = 600,
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProject 'Assets\Editor\EvaluationBenchmark.cs'))) {
    throw 'The isolated project must contain the benchmark harness.'
}
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine.Contains($resolvedProject) }) {
    throw 'The benchmark project already has a running Unity process.'
}
foreach ($value in @($resolvedProject, $OutputPath, $Commit, $Filter)) {
    if ($value.Contains('"')) { throw 'Arguments cannot contain quote characters.' }
}
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $resolvedProject + '"'),
    '-executeMethod', 'EvaluationBenchmark.Export', '-latticeBenchmarkOutput', ('"' + $OutputPath + '"'),
    '-latticeBenchmarkCommit', ('"' + $Commit + '"'), '-latticeBenchmarkSamples', $Samples,
    '-latticeBenchmarkMaxPrivateMb', $MaxPrivateMb, '-logFile', ('"' + $OutputPath + '.log"'))
if ($Filter) { $arguments += @('-latticeBenchmarkFilter', ('"' + $Filter + '"')) }
$started = Get-Date
$peakPrivateMb = 0
$stopReason = $null
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    while (-not $process.WaitForExit(1000)) {
        $process.Refresh()
        $currentPrivateMb = $process.PrivateMemorySize64 / 1MB
        $peakPrivateMb = [math]::Max($peakPrivateMb, $currentPrivateMb)
        if ($currentPrivateMb -gt $MaxPrivateMb) { $stopReason = 'Private memory budget exceeded'; break }
        if (((Get-Date) - $started).TotalSeconds -gt $TimeoutSeconds) { $stopReason = 'Time budget exceeded'; break }
    }
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    [ordered]@{
        project = $resolvedProject; processId = $process.Id; complete = ($null -eq $stopReason -and $process.ExitCode -eq 0)
        stopReason = $stopReason; exitCode = $process.ExitCode; peakPrivateMb = $peakPrivateMb
        maxPrivateMb = $MaxPrivateMb; elapsedSeconds = ((Get-Date) - $started).TotalSeconds
    } | ConvertTo-Json | Set-Content -LiteralPath ($OutputPath + '.supervisor.json') -Encoding utf8
}
if ($stopReason) { throw $stopReason }
if ($process.ExitCode -ne 0) { throw ('Unity exited with code ' + $process.ExitCode) }
