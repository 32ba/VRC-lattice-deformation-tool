param(
    [string]$ResultsPath,
    [double]$MinimumLineCoverage = 100.0,
    [string[]]$TargetAssemblies = @()
)

$ErrorActionPreference = "Stop"
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
    throw "ResultsPath is required."
}

if (-not (Test-Path $ResultsPath)) {
    throw "Coverage results path was not found: $ResultsPath"
}

$TargetAssemblies = @(
    $TargetAssemblies |
        ForEach-Object { "$_".Split(",", [System.StringSplitOptions]::RemoveEmptyEntries) } |
        ForEach-Object { $_.Trim().Trim("'").Trim('"') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

$summaryXml = Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter "Summary.xml" |
    Where-Object { $_.FullName -match "[/\\]Report[/\\]Summary\.xml$" } |
    Select-Object -First 1

if ($summaryXml) {
    $xmlFiles = @($summaryXml)
} else {
    $xmlFiles = Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter "*.xml" |
        Where-Object { $_.Name -notmatch "(?i)test[-_]?results" }
}

if (-not $xmlFiles) {
    throw "No coverage XML files were found under $ResultsPath"
}

function Get-OpenCoverLineCoverage {
    param(
        [System.IO.FileInfo[]]$Files,
        [string[]]$Assemblies
    )

    $points = @{}
    $classPoints = @{}
    foreach ($file in $Files) {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        if ($xml.DocumentElement.Name -ne "CoverageSession") {
            continue
        }

        foreach ($module in $xml.CoverageSession.Modules.Module) {
            $assemblyName = [string]$module.ModuleName
            if ($Assemblies.Count -gt 0 -and $Assemblies -notcontains $assemblyName) {
                continue
            }

            foreach ($class in $module.Classes.Class) {
                $className = [string]$class.FullName
                foreach ($method in $class.Methods.Method) {
                    foreach ($point in $method.SequencePoints.SequencePoint) {
                        $line = [int]$point.sl
                        $fileId = [string]$point.fileid
                        $key = "$assemblyName|$fileId|$line"
                        if (-not $points.ContainsKey($key)) {
                            $points[$key] = [pscustomobject]@{
                                Assembly = $assemblyName
                                Covered = $false
                            }
                        }
                        if ([int]$point.vc -gt 0) {
                            $points[$key].Covered = $true
                        }

                        $classKey = "$assemblyName|$className|$fileId|$line"
                        if (-not $classPoints.ContainsKey($classKey)) {
                            $classPoints[$classKey] = [pscustomobject]@{
                                Assembly = $assemblyName
                                Class = $className
                                Covered = $false
                            }
                        }
                        if ([int]$point.vc -gt 0) {
                            $classPoints[$classKey].Covered = $true
                        }
                    }
                }
            }
        }
    }

    if ($points.Count -eq 0) {
        return $null
    }

    $assemblyCoverage = @{}
    foreach ($group in ($points.Values | Group-Object Assembly)) {
        $covered = @($group.Group | Where-Object Covered).Count
        $coverable = $group.Count
        $assemblyCoverage[$group.Name] = [pscustomobject]@{
            Percent = 100.0 * $covered / $coverable
            Covered = $covered
            Coverable = $coverable
        }
    }

    $totalCovered = @($points.Values | Where-Object Covered).Count
    [pscustomobject]@{
        Percent = 100.0 * $totalCovered / $points.Count
        Covered = $totalCovered
        Coverable = $points.Count
        AssemblyCoverage = $assemblyCoverage
        Source = "OpenCover aggregate"
    }
}

if (-not $summaryXml) {
    $openCoverFiles = @(
        Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter "*.xml" |
            Where-Object { $_.FullName -match "[/\\].*-opencov[/\\]" }
    )
    $aggregate = Get-OpenCoverLineCoverage -Files $openCoverFiles -Assemblies $TargetAssemblies
    if ($aggregate -eq $null) {
        throw "Coverage Report/Summary.xml was not found and no OpenCover sequence points were found under $ResultsPath"
    }

    Write-Host ("Line coverage: {0:N2}% ({1}, {2}/{3})" -f $aggregate.Percent, $aggregate.Source, $aggregate.Covered, $aggregate.Coverable)
    $failures = New-Object System.Collections.Generic.List[string]
    if ($aggregate.Percent -lt $MinimumLineCoverage) {
        $failures.Add(("Line coverage {0:N2}% is below required {1:N2}%." -f $aggregate.Percent, $MinimumLineCoverage))
    }

    foreach ($target in $TargetAssemblies) {
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }

        if (-not $aggregate.AssemblyCoverage.ContainsKey($target)) {
            $failures.Add("No line coverage value was found for target assembly '$target' under $ResultsPath")
            continue
        }

        $entry = $aggregate.AssemblyCoverage[$target]
        Write-Host ("Target assembly {0}: {1:N2}% ({2}/{3}, {4})" -f $target, $entry.Percent, $entry.Covered, $entry.Coverable, $aggregate.Source)
        if ($entry.Percent -lt $MinimumLineCoverage) {
            $failures.Add(("Target assembly {0} line coverage {1:N2}% is below required {2:N2}%." -f $target, $entry.Percent, $MinimumLineCoverage))
        }
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join [Environment]::NewLine)
    }

    return
}

function Get-PercentFromNode {
    param([System.Xml.XmlNode]$Node)

    foreach ($name in @("line-rate", "lineRate")) {
        $attr = $Node.Attributes[$name]
        [double]$value = 0.0
        if ($attr -and [double]::TryParse($attr.Value, [System.Globalization.NumberStyles]::Float, $script:invariantCulture, [ref]$value)) {
            return $value * 100.0
        }
    }

    foreach ($name in @("linecoverage", "lineCoverage", "sequenceCoverage", "coverage")) {
        $attr = $Node.Attributes[$name]
        [double]$value = 0.0
        if ($attr -and [double]::TryParse($attr.Value.TrimEnd("%"), [System.Globalization.NumberStyles]::Float, $script:invariantCulture, [ref]$value)) {
            return $value
        }
    }

    foreach ($name in @("Linecoverage", "linecoverage", "LineCoverage", "lineCoverage", "SequenceCoverage", "sequenceCoverage", "Coverage", "coverage")) {
        $child = $Node.SelectSingleNode($name)
        [double]$value = 0.0
        if ($child -and [double]::TryParse($child.InnerText.TrimEnd("%"), [System.Globalization.NumberStyles]::Float, $script:invariantCulture, [ref]$value)) {
            return $value
        }
    }

    return $null
}

function Get-NodeNames {
    param([System.Xml.XmlNode]$Node)

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($name in @("name", "fullname", "fullName", "moduleName", "assembly", "assemblyName", "package", "filename", "file")) {
        $attr = $Node.Attributes[$name]
        if ($attr -and -not [string]::IsNullOrWhiteSpace($attr.Value)) {
            $names.Add($attr.Value)
        }
    }

    return $names
}

$best = $null
$sourceFile = $null
$targetCoverage = @{}
foreach ($file in $xmlFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    $isReportSummary = $file.Name -ieq "Summary.xml" -and $xml.DocumentElement.Name -eq "CoverageReport"
    if ($isReportSummary) {
        $candidates = @($xml.SelectSingleNode("/CoverageReport/Summary"))
        $targetCandidates = @($xml.SelectNodes("/CoverageReport/Coverage/Assembly")) +
            @($xml.SelectNodes("/CoverageReport/Assemblies/Assembly"))
    } else {
        $candidates = @($xml.DocumentElement) + @($xml.SelectNodes("//*[@line-rate or @lineRate or @linecoverage or @lineCoverage or @sequenceCoverage or @coverage or Linecoverage or linecoverage or LineCoverage or lineCoverage or SequenceCoverage or sequenceCoverage]"))
        $targetCandidates = $candidates
    }

    foreach ($node in $candidates) {
        if ($node -eq $null) {
            continue
        }

        $percent = Get-PercentFromNode $node
        if ($percent -eq $null) {
            continue
        }

        if ($best -eq $null -or $percent -lt $best) {
            $best = $percent
            $sourceFile = $file.FullName
        }

    }

    if ($TargetAssemblies.Count -gt 0) {
        foreach ($node in $targetCandidates) {
            if ($node -eq $null) {
                continue
            }

            $percent = Get-PercentFromNode $node
            if ($percent -eq $null) {
                continue
            }

            $nodeNames = Get-NodeNames $node
            foreach ($target in $TargetAssemblies) {
                if ([string]::IsNullOrWhiteSpace($target)) {
                    continue
                }

                $matchesTarget = $false
                foreach ($nodeName in $nodeNames) {
                    if ($nodeName -eq $target) {
                        $matchesTarget = $true
                        break
                    }

                    $looksLikeFilePath = $nodeName.Contains("/") -or $nodeName.Contains("\") -or $nodeName.EndsWith(".dll", [System.StringComparison]::OrdinalIgnoreCase)
                    if ($looksLikeFilePath) {
                        $leafName = [System.IO.Path]::GetFileNameWithoutExtension($nodeName)
                        if ($leafName -eq $target) {
                            $matchesTarget = $true
                            break
                        }
                    }
                }

                if (-not $matchesTarget) {
                    continue
                }

                if (-not $targetCoverage.ContainsKey($target) -or $percent -lt $targetCoverage[$target].Percent) {
                    $targetCoverage[$target] = [pscustomobject]@{
                        Percent = $percent
                        Source = $file.FullName
                    }
                }
            }
        }
    }
}

if ($best -eq $null) {
    throw "No line coverage value was found in coverage XML files under $ResultsPath"
}

Write-Host ("Line coverage: {0:N2}% ({1})" -f $best, $sourceFile)

$failures = New-Object System.Collections.Generic.List[string]
if ($best -lt $MinimumLineCoverage) {
    $failures.Add(("Line coverage {0:N2}% is below required {1:N2}%." -f $best, $MinimumLineCoverage))
}

foreach ($target in $TargetAssemblies) {
    if ([string]::IsNullOrWhiteSpace($target)) {
        continue
    }

    if (-not $targetCoverage.ContainsKey($target)) {
        $failures.Add("No line coverage value was found for target assembly '$target' under $ResultsPath")
        continue
    }

    $entry = $targetCoverage[$target]
    Write-Host ("Target assembly {0}: {1:N2}% ({2})" -f $target, $entry.Percent, $entry.Source)
    if ($entry.Percent -lt $MinimumLineCoverage) {
        $failures.Add(("Target assembly {0} line coverage {1:N2}% is below required {2:N2}%." -f $target, $entry.Percent, $MinimumLineCoverage))
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
