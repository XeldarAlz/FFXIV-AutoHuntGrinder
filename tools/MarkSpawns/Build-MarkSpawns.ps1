#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $LandingPointsPath,
    [string] $SpawnReportsPath,
    [string] $SheetDirectory,
    [switch] $Download,
    [string] $OutputPath = (Join-Path $PSScriptRoot '../../AutoHuntGrinder/Core/Hunts/Data/MarkSpawnTable.g.cs'),
    [double] $DuplicateRadius = 15.0,
    [ValidateRange(1, 255)]
    [int] $MaxPointsPerTarget = 12
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invariant = [Globalization.CultureInfo]::InvariantCulture
$sources = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'sources.json') | ConvertFrom-Json
$temporaryRoot = [IO.Path]::GetTempPath()
$expansionNames = @('ARR', 'HW', 'SB', 'ShB', 'EW', 'DT')
# A landing point and a spawn report this close are taken as the same spot when fitting the map to world conversion.
$sameSpotRadius = 30.0

function Save-Source([string] $url, [string] $path) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $path
    return $path
}

# Import-Csv reads a first line starting with '#' as a comment, so the sheet header is passed in explicitly.
function Read-SheetRows([string] $name) {
    $path = Join-Path $SheetDirectory "$name.csv"
    $columns = (Get-Content -Path $path -TotalCount 1 -Encoding utf8).Split(',')
    return Import-Csv -Path $path -Header $columns -Encoding utf8
}

function Read-Sheet([string] $name) {
    $rows = @{}
    foreach ($row in Read-SheetRows $name) {
        $rows[$row.'#'] = $row
    }

    return $rows
}

function Read-Json([string] $path) {
    return Get-Content -Raw -Path $path -Encoding utf8 | ConvertFrom-Json -AsHashtable
}

function ConvertTo-WorldCoordinate([double] $mapCoordinate, [double] $sizeFactor, [double] $offset) {
    return ($mapCoordinate - 1.0 - 2048.0 / $sizeFactor) / 0.02 - $offset
}

function Get-PlanarDistance([double[]] $first, [double[]] $second) {
    $deltaX = $first[0] - $second[0]
    $deltaZ = $first[2] - $second[2]
    return [Math]::Sqrt($deltaX * $deltaX + $deltaZ * $deltaZ)
}

function Get-LandingPoint([int] $targetId, $target, [int] $territoryId) {
    $landing = $landingPoints["$targetId"]
    if ($null -eq $landing -or [int]$landing.Map -ne $territoryId -or [int]$landing.BNpcNameKey -ne [int]$target.Name) {
        return $null
    }

    $location = $landing.Location
    return , [double[]]@($location.X, $location.Y, $location.Z)
}

function Get-ReportedPoints($target, $map, [bool] $fateBound) {
    $points = [Collections.Generic.List[double[]]]::new()
    $reported = $spawnReports[$target.Name]
    $sizeFactor = [double]$map.SizeFactor
    if ($null -eq $reported -or $sizeFactor -eq 0) {
        return , $points
    }

    $mapId = [int]$map.'#'
    $offsetX = [double]$map.OffsetX
    $offsetY = [double]$map.OffsetY
    foreach ($position in $reported.positions) {
        if ([int]$position.map -ne $mapId -or ([int]$position.fate -ne 0 -and -not $fateBound)) {
            continue
        }

        $worldX = ConvertTo-WorldCoordinate $position.x $sizeFactor $offsetX
        $worldZ = ConvertTo-WorldCoordinate $position.y $sizeFactor $offsetY
        $points.Add([double[]]@($worldX, [double]::NaN, $worldZ))
    }

    return , $points
}

function Group-ReportedPoints([Collections.Generic.List[double[]]] $reported) {
    $clusters = [Collections.Generic.List[object]]::new()
    foreach ($point in $reported) {
        $owner = $null
        foreach ($cluster in $clusters) {
            if ((Get-PlanarDistance $cluster.Seed $point) -lt $DuplicateRadius) {
                $owner = $cluster
                break
            }
        }

        if ($null -eq $owner) {
            $owner = [pscustomobject]@{ Seed = $point; SumX = 0.0; SumZ = 0.0; Reports = 0 }
            $clusters.Add($owner)
        }

        $owner.SumX += $point[0]
        $owner.SumZ += $point[2]
        $owner.Reports++
    }

    $centers = [Collections.Generic.List[double[]]]::new()
    foreach ($cluster in ($clusters | Sort-Object -Property Reports -Descending -Stable)) {
        $centers.Add([double[]]@(($cluster.SumX / $cluster.Reports), [double]::NaN, ($cluster.SumZ / $cluster.Reports)))
    }

    return , $centers
}

function Select-DistinctPoints([Collections.Generic.List[double[]]] $candidates) {
    $kept = [Collections.Generic.List[double[]]]::new()
    foreach ($candidate in $candidates) {
        if ($kept.Count -ge $MaxPointsPerTarget) {
            break
        }

        $isDuplicate = $false
        foreach ($point in $kept) {
            if ((Get-PlanarDistance $point $candidate) -lt $DuplicateRadius) {
                $isDuplicate = $true
                break
            }
        }

        if (-not $isDuplicate) {
            $kept.Add($candidate)
        }
    }

    return , $kept
}

function Measure-NearestReport([double[]] $landing, [Collections.Generic.List[double[]]] $reported) {
    $nearest = $reported[0]
    $nearestDistance = [double]::MaxValue
    foreach ($point in $reported) {
        $distance = Get-PlanarDistance $landing $point
        if ($distance -lt $nearestDistance) {
            $nearest = $point
            $nearestDistance = $distance
        }
    }

    return , [double[]]@($nearestDistance, $landing[0], $landing[2], $nearest[0], $nearest[2])
}

function Measure-LinearFit([Collections.Generic.List[double]] $predicted, [Collections.Generic.List[double]] $actual) {
    $sumPredicted = 0.0
    $sumActual = 0.0
    for ($index = 0; $index -lt $predicted.Count; $index++) {
        $sumPredicted += $predicted[$index]
        $sumActual += $actual[$index]
    }

    $meanPredicted = $sumPredicted / $predicted.Count
    $meanActual = $sumActual / $actual.Count
    $covariance = 0.0
    $variance = 0.0
    for ($index = 0; $index -lt $predicted.Count; $index++) {
        $covariance += ($predicted[$index] - $meanPredicted) * ($actual[$index] - $meanActual)
        $variance += ($predicted[$index] - $meanPredicted) * ($predicted[$index] - $meanPredicted)
    }

    $slope = $covariance / $variance
    return , [double[]]@($slope, ($meanActual - $slope * $meanPredicted))
}

function Get-Percentile([double[]] $sorted, [double] $fraction) {
    return $sorted[[int][Math]::Floor(($sorted.Length - 1) * $fraction)]
}

function Format-Number([double] $value, [string] $format) {
    return $value.ToString($format, $invariant)
}

function Format-Float([double] $value) {
    if ([double]::IsNaN($value)) {
        return 'float.NaN'
    }

    $rounded = [Math]::Round($value, 2)
    if ($rounded -eq 0) {
        $rounded = 0.0
    }

    return (Format-Number $rounded '0.00') + 'f'
}

function Format-SpanProperty([string] $elementType, [string] $name, [Collections.Generic.List[string]] $values, [int] $valuesPerLine) {
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append("    public static ReadOnlySpan<$elementType> $name =>`n    [`n")
    for ($index = 0; $index -lt $values.Count; $index += $valuesPerLine) {
        $count = [Math]::Min($valuesPerLine, $values.Count - $index)
        [void]$builder.Append('        ').Append([string]::Join(', ', $values.GetRange($index, $count))).Append(",`n")
    }

    [void]$builder.Append('    ];')
    return $builder.ToString()
}

function Assert-Fits([int] $value, [int] $maximum, [string] $what) {
    if ($value -gt $maximum) {
        throw "$what $value does not fit the table's element type (maximum $maximum)."
    }
}

if ($Download) {
    $cache = Join-Path $temporaryRoot 'AutoHuntGrinder-MarkSpawns'
    $SheetDirectory = Join-Path $cache 'sheets'
    New-Item -ItemType Directory -Force -Path $SheetDirectory | Out-Null
    $LandingPointsPath = Save-Source $sources.landingPoints.url (Join-Path $cache 'AllHunts.json')
    $SpawnReportsPath = Save-Source $sources.spawnReports.url (Join-Path $cache 'monsters.json')
    foreach ($name in $sources.sheets.names) {
        Save-Source "$($sources.sheets.url)/$name.csv" (Join-Path $SheetDirectory "$name.csv") | Out-Null
    }
}

$localRoot = Join-Path $temporaryRoot $sources.localRoot
if (-not $LandingPointsPath) {
    $LandingPointsPath = Join-Path $localRoot $sources.landingPoints.localPath
}

if (-not $SpawnReportsPath) {
    $SpawnReportsPath = Join-Path $localRoot $sources.spawnReports.localPath
}

if (-not $SheetDirectory) {
    $SheetDirectory = Join-Path $localRoot $sources.sheets.localPath
}

foreach ($inputPath in @($LandingPointsPath, $SpawnReportsPath, $SheetDirectory)) {
    if (-not (Test-Path $inputPath)) {
        throw "Input not found: $inputPath. Run with -Download to fetch the pinned sources."
    }
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$targets = Read-Sheet 'MobHuntTarget'
$maps = Read-Sheet 'Map'
$territories = Read-Sheet 'TerritoryType'
$names = Read-Sheet 'BNpcName'
$landingPoints = Read-Json $LandingPointsPath
$spawnReports = Read-Json $SpawnReportsPath

$ordersByRow = @{}
foreach ($order in Read-SheetRows 'MobHuntOrder') {
    $rowId = $order.'#'.Split('.')[0]
    if (-not $ordersByRow.ContainsKey($rowId)) {
        $ordersByRow[$rowId] = [Collections.Generic.List[object]]::new()
    }

    $ordersByRow[$rowId].Add($order)
}

$billKinds = [Collections.Generic.SortedDictionary[int, string]]::new()
foreach ($orderType in (Read-Sheet 'MobHuntOrderType').Values) {
    $amount = [int]$orderType.OrderAmount
    if ($amount -eq 0 -or [int]$orderType.EventItem -eq 0) {
        continue
    }

    $kind = if ([int]$orderType.Type -eq 2) { 'elite' } else { 'daily' }
    $start = [int]$orderType.OrderStart
    for ($rowId = $start; $rowId -lt $start + $amount; $rowId++) {
        if (-not $ordersByRow.ContainsKey("$rowId")) {
            continue
        }

        foreach ($order in $ordersByRow["$rowId"]) {
            $targetId = [int]$order.Target
            if ($targetId -ne 0) {
                $billKinds[$targetId] = $kind
            }
        }
    }
}

$entries = [Collections.Generic.List[object]]::new()
$conversionPairs = [Collections.Generic.List[double[]]]::new()
$landingTargets = 0
$reportTargets = 0
foreach ($targetId in $billKinds.Keys) {
    $kind = $billKinds[$targetId]
    $target = $targets["$targetId"]
    $map = $maps[$target.Map]
    $territoryId = [int]$map.TerritoryType
    $candidates = [Collections.Generic.List[double[]]]::new()

    $landing = Get-LandingPoint $targetId $target $territoryId
    if ($null -ne $landing) {
        $candidates.Add($landing)
        $landingTargets++
    }

    $reported = Get-ReportedPoints $target $map ([int]$target.FATE -ne 0)
    if ($null -ne $landing -and $reported.Count -gt 0) {
        $conversionPairs.Add((Measure-NearestReport $landing $reported))
    }

    if ($reported.Count -gt 0 -and ($kind -eq 'elite' -or $null -eq $landing)) {
        $candidates.AddRange((Group-ReportedPoints $reported))
        $reportTargets++
    }

    $exVersion = [int]$territories["$territoryId"].ExVersion
    $entries.Add([pscustomobject]@{
            TargetId    = $targetId
            Kind        = $kind
            TerritoryId = $territoryId
            Expansion   = if ($exVersion -lt $expansionNames.Length) { $expansionNames[$exVersion] } else { "ExVersion $exVersion" }
            Name        = $names[$target.Name].Singular
            Points      = Select-DistinctPoints $candidates
        })
}

$supported = @($entries | Where-Object { $_.Points.Count -gt 0 })
$targetRowIds = [Collections.Generic.List[string]]::new()
$territoryIds = [Collections.Generic.List[string]]::new()
$pointStarts = [Collections.Generic.List[string]]::new()
$pointCounts = [Collections.Generic.List[string]]::new()
$coordinates = [Collections.Generic.List[string]]::new()
$pointTotal = 0
$heightlessPoints = 0
foreach ($entry in $supported) {
    Assert-Fits $entry.TargetId ([uint16]::MaxValue) 'Target row id'
    Assert-Fits $entry.TerritoryId ([uint16]::MaxValue) 'Territory id'
    Assert-Fits $pointTotal ([uint16]::MaxValue) 'Point start'
    $targetRowIds.Add((Format-Number $entry.TargetId '0'))
    $territoryIds.Add((Format-Number $entry.TerritoryId '0'))
    $pointStarts.Add((Format-Number $pointTotal '0'))
    $pointCounts.Add((Format-Number $entry.Points.Count '0'))
    foreach ($point in $entry.Points) {
        $coordinates.Add((Format-Float $point[0]))
        $coordinates.Add((Format-Float $point[1]))
        $coordinates.Add((Format-Float $point[2]))
        if ([double]::IsNaN($point[1])) {
            $heightlessPoints++
        }
    }

    $pointTotal += $entry.Points.Count
}

$properties = @(
    Format-SpanProperty 'ushort' 'TargetRowIds' $targetRowIds 16
    Format-SpanProperty 'ushort' 'TerritoryIds' $territoryIds 16
    Format-SpanProperty 'ushort' 'PointStarts' $pointStarts 16
    Format-SpanProperty 'byte' 'PointCounts' $pointCounts 24
    Format-SpanProperty 'float' 'Coordinates' $coordinates 3
)

$source = @(
    '// <auto-generated/>'
    '// Written by tools/MarkSpawns/Build-MarkSpawns.ps1 from the datasets credited in THIRD-PARTY-NOTICES.md. Regenerate instead of editing.'
    ''
    'namespace AutoHuntGrinder.Core.Hunts.Data;'
    ''
    'internal static class MarkSpawnTable'
    '{'
    ($properties -join "`n`n")
    '}'
    ''
) -join "`n"

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
[IO.File]::WriteAllText($OutputPath, $source, [Text.UTF8Encoding]::new($false))

$distances = [double[]]@($conversionPairs | ForEach-Object { $_[0] } | Sort-Object)
$predictedX = [Collections.Generic.List[double]]::new()
$actualX = [Collections.Generic.List[double]]::new()
$predictedZ = [Collections.Generic.List[double]]::new()
$actualZ = [Collections.Generic.List[double]]::new()
foreach ($pair in $conversionPairs) {
    if ($pair[0] -ge $sameSpotRadius) {
        continue
    }

    $actualX.Add($pair[1])
    $actualZ.Add($pair[2])
    $predictedX.Add($pair[3])
    $predictedZ.Add($pair[4])
}

$fitX = Measure-LinearFit $predictedX $actualX
$fitZ = Measure-LinearFit $predictedZ $actualZ
$unsupported = @($entries | Where-Object { $_.Points.Count -eq 0 })
$dailyTotal = @($entries | Where-Object { $_.Kind -eq 'daily' }).Count
$eliteTotal = @($entries | Where-Object { $_.Kind -eq 'elite' }).Count
$dailyCovered = @($supported | Where-Object { $_.Kind -eq 'daily' }).Count
$eliteCovered = @($supported | Where-Object { $_.Kind -eq 'elite' }).Count

Write-Host "Wrote $OutputPath"
Write-Host "Daily targets covered: $dailyCovered of $dailyTotal"
Write-Host "Elite targets covered: $eliteCovered of $eliteTotal"
Write-Host "Unsupported targets: $($unsupported.Count)"
Write-Host "Points: $pointTotal on $($supported.Count) targets, $heightlessPoints without height; $landingTargets targets use a landing point, $reportTargets use spawn reports"
Write-Host ('Map to world check on {0} targets in both datasets: nearest report median {1} y, p90 {2} y; same-spot fit X slope {3} intercept {4} y, Z slope {5} intercept {6} y over {7} pairs' -f
    $distances.Length,
    (Format-Number (Get-Percentile $distances 0.5) '0.0'),
    (Format-Number (Get-Percentile $distances 0.9) '0.0'),
    (Format-Number $fitX[0] '0.0000'),
    (Format-Number $fitX[1] '0.00'),
    (Format-Number $fitZ[0] '0.0000'),
    (Format-Number $fitZ[1] '0.00'),
    $predictedX.Count)
Write-Host 'Coverage by expansion (daily, elite):'
foreach ($expansion in ($entries | Select-Object -ExpandProperty Expansion -Unique)) {
    $inExpansion = @($entries | Where-Object { $_.Expansion -eq $expansion })
    $daily = @($inExpansion | Where-Object { $_.Kind -eq 'daily' })
    $elite = @($inExpansion | Where-Object { $_.Kind -eq 'elite' })
    Write-Host ('  {0,-4} {1,3} of {2,3}   {3,2} of {4,2}' -f $expansion,
        @($daily | Where-Object { $_.Points.Count -gt 0 }).Count, $daily.Count,
        @($elite | Where-Object { $_.Points.Count -gt 0 }).Count, $elite.Count)
}

foreach ($entry in $unsupported) {
    Write-Host ('  unsupported: target {0} ({1} {2}) {3}' -f $entry.TargetId, $entry.Expansion, $entry.Kind, $entry.Name)
}
