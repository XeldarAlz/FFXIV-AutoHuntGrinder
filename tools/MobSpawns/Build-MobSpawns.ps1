#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SpawnReportsPath,
    [string] $SheetDirectory,
    [switch] $Download,
    [string] $OutputPath = (Join-Path $PSScriptRoot '../../AutoHuntGrinder/Core/Spawns/Data/MobSpawnTable.g.cs'),
    [double] $ClusterRadius = 25.0,
    [ValidateRange(1, 255)]
    [int] $MaxPointsPerZone = 16
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cache = Join-Path ([IO.Path]::GetTempPath()) 'AutoHuntGrinder-MobSpawns'
$spawnReportsFile = 'monsters.json'
if (-not $SpawnReportsPath) {
    $SpawnReportsPath = Join-Path $cache $spawnReportsFile
}

if (-not $SheetDirectory) {
    $SheetDirectory = Join-Path $cache 'sheets'
}

$invariant = [Globalization.CultureInfo]::InvariantCulture
$noticesPath = Join-Path $PSScriptRoot '../../THIRD-PARTY-NOTICES.md'
$sheetSource = 'https://raw.githubusercontent.com/xivapi/ffxiv-datamining/a67c23b00fe8cb254855d06b59845958b55d28f3/csv/en'
$sheetNames = @('BNpcName', 'Map', 'MapMarker', 'MonsterNote', 'MonsterNoteTarget', 'TerritoryType')
$openWorldUse = 1
$targetsPerLogEntry = 4
$zonesPerLogTarget = 3
$pointKind = 0
$areaKind = 1
# Sort rank of a zone whose only reports come from FATEs: after every zone a search can use.
$fateOnlyRank = 2
# Reported heights are map z with one decimal and a map z unit is 100 yalms, so heights are stored in 10 yalm steps.
$heightStep = 10
$unknownHeight = [int][sbyte]::MinValue

function Save-Source([string] $url, [string] $path) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $path
}

# The dataset commit is pinned once, in THIRD-PARTY-NOTICES.md, so a download always matches what is credited there.
function Get-PinnedDatasetUrl([string] $fileName) {
    $notices = Get-Content -Raw -Path $noticesPath -Encoding utf8
    $pattern = 'Source: https://github\.com/(?<repository>[^,\s]+), file `(?<path>[^`]*/' + [regex]::Escape($fileName) + ')` at commit `(?<commit>[0-9a-f]{40})`'
    $match = [regex]::Match($notices, $pattern)
    if (-not $match.Success) {
        throw "THIRD-PARTY-NOTICES.md pins no source for $fileName."
    }

    return 'https://raw.githubusercontent.com/{0}/{1}/{2}' -f $match.Groups['repository'].Value, $match.Groups['commit'].Value, $match.Groups['path'].Value
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

# Inverse of the client's map coordinate: 0.02 map units per yalm, plus 2048 / SizeFactor, plus 1.
function ConvertFrom-MapCoordinate([double] $mapCoordinate, [double] $sizeFactor, [double] $offset) {
    return 50.0 * ($mapCoordinate - 1.0) - 102400.0 / $sizeFactor - $offset
}

# Map markers are placed on the 2048 texel map texture, whose center is the world origin and whose scale is SizeFactor / 100.
function ConvertFrom-MarkerPixel([double] $pixel, [double] $sizeFactor, [double] $offset) {
    return ($pixel - 1024.0) * 100.0 / $sizeFactor - $offset
}

function New-Point([double] $x, [double] $z, [double] $height) {
    return , [double[]]@($x, $z, $height)
}

function Test-NearAny([Collections.Generic.List[double[]]] $points, [double[]] $candidate) {
    foreach ($point in $points) {
        $deltaX = $point[0] - $candidate[0]
        $deltaZ = $point[1] - $candidate[1]
        if ($deltaX * $deltaX + $deltaZ * $deltaZ -lt $radiusSquared) {
            return $true
        }
    }

    return $false
}

# The lower median is always one of the reported heights, so a cluster spanning two floors keeps a real floor.
function Get-LowerMedian([Collections.Generic.List[double]] $values) {
    $sorted = $values.ToArray()
    [Array]::Sort($sorted)
    return $sorted[[int][Math]::Floor(($sorted.Length - 1) / 2)]
}

function Group-Reports([Collections.Generic.List[double[]]] $reports) {
    $clusters = [Collections.Generic.List[object]]::new()
    foreach ($report in $reports) {
        $owner = $null
        foreach ($cluster in $clusters) {
            $deltaX = $cluster.SeedX - $report[0]
            $deltaZ = $cluster.SeedZ - $report[1]
            if ($deltaX * $deltaX + $deltaZ * $deltaZ -lt $radiusSquared) {
                $owner = $cluster
                break
            }
        }

        if ($null -eq $owner) {
            $owner = [pscustomobject]@{ SeedX = $report[0]; SeedZ = $report[1]; SumX = 0.0; SumZ = 0.0; Heights = [Collections.Generic.List[double]]::new() }
            $clusters.Add($owner)
        }

        $owner.SumX += $report[0]
        $owner.SumZ += $report[1]
        $owner.Heights.Add($report[2])
    }

    $centers = [Collections.Generic.List[double[]]]::new()
    foreach ($cluster in ($clusters | Sort-Object -Property { $_.Heights.Count } -Descending -Stable)) {
        $reportCount = $cluster.Heights.Count
        $centers.Add((New-Point ($cluster.SumX / $reportCount) ($cluster.SumZ / $reportCount) (Get-LowerMedian $cluster.Heights)))
    }

    return , $centers
}

function Select-DistinctPoints([Collections.Generic.List[double[]]] $candidates) {
    $kept = [Collections.Generic.List[double[]]]::new()
    foreach ($candidate in $candidates) {
        if ($kept.Count -ge $MaxPointsPerZone) {
            break
        }

        if (-not (Test-NearAny $kept $candidate)) {
            $kept.Add($candidate)
        }
    }

    return , $kept
}

function Get-Zone([int] $nameId, [int] $territoryId) {
    $key = "$nameId/$territoryId"
    $zone = $zones[$key]
    if ($null -eq $zone) {
        $zone = [pscustomobject]@{
            NameId      = $nameId
            TerritoryId = $territoryId
            Kind        = $pointKind
            FateOnly    = $false
            Regular     = [Collections.Generic.List[double[]]]::new()
            Fate        = [Collections.Generic.List[double[]]]::new()
            Areas       = [Collections.Generic.List[double[]]]::new()
            Locations   = [Collections.Generic.HashSet[int]]::new()
            Points      = [Collections.Generic.List[double[]]]::new()
            Reports     = 0
        }
        $zones[$key] = $zone
    }

    return $zone
}

function Get-AreaPoints([int] $territoryId, [int] $placeNameId) {
    $points = [Collections.Generic.List[double[]]]::new()
    $frame = $mapFrames[[int]$territories["$territoryId"].Map]
    if ($null -eq $frame -or $frame.MarkerRange -eq 0 -or -not $markersByRange.ContainsKey($frame.MarkerRange)) {
        return , $points
    }

    foreach ($marker in $markersByRange[$frame.MarkerRange]) {
        if ([int]$marker.PlaceNameSubtext -ne $placeNameId) {
            continue
        }

        $worldX = ConvertFrom-MarkerPixel $marker.X $frame.SizeFactor $frame.OffsetX
        $worldZ = ConvertFrom-MarkerPixel $marker.Y $frame.SizeFactor $frame.OffsetY
        $points.Add((New-Point $worldX $worldZ $unknownHeight))
    }

    return , $points
}

function Test-AreaZone($zone) {
    return $zone.Kind -eq $areaKind -and $zone.Points.Count -gt 0
}

# A zone known only from FATE reports takes the sub-area in their place, since the hunt never fights a FATE's mobs.
function Add-AreaZone([int] $nameId, [int] $territoryId, [int] $placeNameId) {
    $zone = Get-Zone $nameId $territoryId
    if (-not $zone.Locations.Add($placeNameId)) {
        return Test-AreaZone $zone
    }

    $areaPoints = Get-AreaPoints $territoryId $placeNameId
    if ($areaPoints.Count -eq 0) {
        return Test-AreaZone $zone
    }

    $zone.Kind = $areaKind
    $zone.FateOnly = $false
    $zone.Areas.AddRange($areaPoints)
    $zone.Reports = $zone.Areas.Count
    $zone.Points = Select-DistinctPoints $zone.Areas
    return $true
}

function Get-ZoneRank($zone) {
    if ($zone.FateOnly) {
        return $fateOnlyRank
    }

    return $zone.Kind
}

function Format-Number([double] $value, [string] $format) {
    return $value.ToString($format, $invariant)
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

function Assert-Fits([int] $value, [int] $minimum, [int] $maximum, [string] $what) {
    if ($value -lt $minimum -or $value -gt $maximum) {
        throw "$what $value does not fit the table's element type ($minimum to $maximum)."
    }
}

function Add-Integer([Collections.Generic.List[string]] $values, [int] $value) {
    $values.Add((Format-Number $value '0'))
}

if ($Download) {
    New-Item -ItemType Directory -Force -Path $SheetDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $SpawnReportsPath) | Out-Null
    Save-Source (Get-PinnedDatasetUrl $spawnReportsFile) $SpawnReportsPath
    foreach ($name in $sheetNames) {
        Save-Source "$sheetSource/$name.csv" (Join-Path $SheetDirectory "$name.csv")
    }
}

foreach ($inputPath in @($SpawnReportsPath, $SheetDirectory)) {
    if (-not (Test-Path $inputPath)) {
        throw "Input not found: $inputPath. Run with -Download to fetch the pinned sources."
    }
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$radiusSquared = $ClusterRadius * $ClusterRadius
$names = Read-Sheet 'BNpcName'
$maps = Read-Sheet 'Map'
$territories = Read-Sheet 'TerritoryType'
$targets = Read-Sheet 'MonsterNoteTarget'
$reports = Read-Json $SpawnReportsPath

$mapFrames = @{}
foreach ($map in $maps.Values) {
    $territory = $territories[$map.TerritoryType]
    if ($null -eq $territory -or [int]$territory.TerritoryIntendedUse -ne $openWorldUse -or [int]$map.SizeFactor -eq 0) {
        continue
    }

    $mapFrames[[int]$map.'#'] = [pscustomobject]@{
        TerritoryId = [int]$map.TerritoryType
        SizeFactor  = [double]$map.SizeFactor
        OffsetX     = [double]$map.OffsetX
        OffsetY     = [double]$map.OffsetY
        MarkerRange = [int]$map.MapMarkerRange
    }
}

$zoneTerritories = @{}
$dutyPlaces = [Collections.Generic.HashSet[int]]::new()
foreach ($territory in $territories.Values) {
    $placeNameId = [int]$territory.PlaceName
    if ($placeNameId -eq 0) {
        continue
    }

    if ([int]$territory.ContentFinderCondition -ne 0) {
        [void]$dutyPlaces.Add($placeNameId)
    }

    if ([int]$territory.TerritoryIntendedUse -ne $openWorldUse) {
        continue
    }

    if (-not $zoneTerritories.ContainsKey($placeNameId)) {
        $zoneTerritories[$placeNameId] = [Collections.Generic.List[int]]::new()
    }

    $zoneTerritories[$placeNameId].Add([int]$territory.'#')
}

$markersByRange = @{}
foreach ($marker in Read-SheetRows 'MapMarker') {
    $rangeId = [int]$marker.'#'.Split('.')[0]
    if (-not $markersByRange.ContainsKey($rangeId)) {
        $markersByRange[$rangeId] = [Collections.Generic.List[object]]::new()
    }

    $markersByRange[$rangeId].Add($marker)
}

$zones = @{}
$skippedNames = 0
$openWorldReports = 0
$corruptReports = 0
foreach ($key in $reports.Keys) {
    $name = $names[$key]
    if ($null -eq $name -or $name.Singular.Length -eq 0) {
        $skippedNames++
        continue
    }

    foreach ($position in $reports[$key].positions) {
        $frame = $mapFrames[[int]$position.map]
        if ($null -eq $frame) {
            continue
        }

        $reportedWithoutMonsterData = [int]$position.level -eq 0
        if ($reportedWithoutMonsterData) {
            $corruptReports++
            continue
        }

        $worldX = ConvertFrom-MapCoordinate $position.x $frame.SizeFactor $frame.OffsetX
        $worldZ = ConvertFrom-MapCoordinate $position.y $frame.SizeFactor $frame.OffsetY
        $height = [Math]::Round([double]$position.z * 100.0 / $heightStep, [MidpointRounding]::AwayFromZero)
        $zone = Get-Zone ([int]$key) $frame.TerritoryId
        if ([int]$position.fate -eq 0) {
            $zone.Regular.Add((New-Point $worldX $worldZ $height))
        }
        else {
            $zone.Fate.Add((New-Point $worldX $worldZ $height))
        }

        $openWorldReports++
    }
}

foreach ($zone in $zones.Values) {
    $source = $zone.Regular
    if ($source.Count -eq 0) {
        $source = $zone.Fate
        $zone.FateOnly = $true
    }

    $zone.Reports = $source.Count
    $zone.Points = Select-DistinctPoints (Group-Reports $source)
}

$logTargetIds = [Collections.Generic.SortedSet[int]]::new()
foreach ($note in Read-SheetRows 'MonsterNote') {
    for ($slot = 0; $slot -lt $targetsPerLogEntry; $slot++) {
        $targetId = [int]$note."MonsterNoteTarget[$slot]"
        if ($targetId -ne 0) {
            [void]$logTargetIds.Add($targetId)
        }
    }
}

$withPoints = 0
$withFatePoints = 0
$withArea = 0
$inDuty = 0
$uncovered = [Collections.Generic.List[string]]::new()
foreach ($targetId in $logTargetIds) {
    $target = $targets["$targetId"]
    $nameId = [int]$target.BNpcName
    $hasPoints = $false
    $hasFatePoints = $false
    $hasArea = $false
    $hasDuty = $false
    for ($zoneIndex = 0; $zoneIndex -lt $zonesPerLogTarget; $zoneIndex++) {
        $placeNameId = [int]$target."PlaceNameZone[$zoneIndex]"
        $locationId = [int]$target."PlaceNameLocation[$zoneIndex]"
        if ($placeNameId -eq 0) {
            continue
        }

        if ($dutyPlaces.Contains($locationId)) {
            $hasDuty = $true
            continue
        }

        if (-not $zoneTerritories.ContainsKey($placeNameId)) {
            continue
        }

        foreach ($territoryId in $zoneTerritories[$placeNameId]) {
            $zone = $zones["$nameId/$territoryId"]
            if ($null -ne $zone -and $zone.Kind -eq $pointKind -and $zone.Points.Count -gt 0 -and -not $zone.FateOnly) {
                $hasPoints = $true
                continue
            }

            if ($locationId -ne 0 -and (Add-AreaZone $nameId $territoryId $locationId)) {
                $hasArea = $true
                continue
            }

            if ($null -ne $zone -and $zone.FateOnly -and $zone.Points.Count -gt 0) {
                $hasFatePoints = $true
            }
        }
    }

    if ($hasPoints) {
        $withPoints++
    }
    elseif ($hasArea) {
        $withArea++
    }
    elseif ($hasFatePoints) {
        $withFatePoints++
    }
    elseif ($hasDuty) {
        $inDuty++
    }
    else {
        $uncovered.Add(('target {0} ({1}, BNpcName {2})' -f $targetId, $names["$nameId"].Singular, $nameId))
    }
}

$zonesByName = [Collections.Generic.SortedDictionary[int, Collections.Generic.List[object]]]::new()
foreach ($zone in $zones.Values) {
    if ($zone.Points.Count -eq 0) {
        continue
    }

    if (-not $zonesByName.ContainsKey($zone.NameId)) {
        $zonesByName[$zone.NameId] = [Collections.Generic.List[object]]::new()
    }

    $zonesByName[$zone.NameId].Add($zone)
}

# Zones with reported positions come before sub-area labels and FATE-only zones last, busier zones first within each,
# so a run without a pinned zone takes the first.
$zoneOrder = @(
    @{ Expression = { Get-ZoneRank $_ }; Ascending = $true }
    @{ Expression = { $_.Points.Count }; Descending = $true }
    @{ Expression = 'Reports'; Descending = $true }
    @{ Expression = 'TerritoryId'; Ascending = $true }
)

$nameIds = [Collections.Generic.List[string]]::new()
$nameEntryStarts = [Collections.Generic.List[string]]::new()
$nameEntryCounts = [Collections.Generic.List[string]]::new()
$entryTerritoryIds = [Collections.Generic.List[string]]::new()
$entryPointStarts = [Collections.Generic.List[string]]::new()
$entryPointCounts = [Collections.Generic.List[string]]::new()
$entryKinds = [Collections.Generic.List[string]]::new()
$entryFateOnly = [Collections.Generic.List[string]]::new()
$planarCoordinates = [Collections.Generic.List[string]]::new()
$heights = [Collections.Generic.List[string]]::new()
$entryTotal = 0
$pointTotal = 0
$areaEntries = 0
$areaPoints = 0
$fateOnlyEntries = 0
$areaOnlyNames = 0
$fateOnlyNames = 0
foreach ($nameId in $zonesByName.Keys) {
    $nameZones = @($zonesByName[$nameId] | Sort-Object -Property $zoneOrder)
    Assert-Fits $nameId 0 ([uint16]::MaxValue) 'Name id'
    Assert-Fits $entryTotal 0 ([uint16]::MaxValue) 'Entry start'
    Assert-Fits $nameZones.Count 1 ([byte]::MaxValue) 'Zone count'
    Add-Integer $nameIds $nameId
    Add-Integer $nameEntryStarts $entryTotal
    Add-Integer $nameEntryCounts $nameZones.Count
    if ($nameZones[0].FateOnly) {
        $fateOnlyNames++
    }
    elseif ($nameZones[0].Kind -eq $areaKind) {
        $areaOnlyNames++
    }

    foreach ($zone in $nameZones) {
        Assert-Fits $zone.TerritoryId 0 ([uint16]::MaxValue) 'Territory id'
        Assert-Fits $pointTotal 0 ([uint16]::MaxValue) 'Point start'
        Add-Integer $entryTerritoryIds $zone.TerritoryId
        Add-Integer $entryPointStarts $pointTotal
        Add-Integer $entryPointCounts $zone.Points.Count
        Add-Integer $entryKinds $zone.Kind
        Add-Integer $entryFateOnly ([int]$zone.FateOnly)
        foreach ($point in $zone.Points) {
            $worldX = [int][Math]::Round($point[0], [MidpointRounding]::AwayFromZero)
            $worldZ = [int][Math]::Round($point[1], [MidpointRounding]::AwayFromZero)
            $height = [int]$point[2]
            Assert-Fits $worldX ([int16]::MinValue) ([int16]::MaxValue) 'World X'
            Assert-Fits $worldZ ([int16]::MinValue) ([int16]::MaxValue) 'World Z'
            if ($height -ne $unknownHeight) {
                Assert-Fits $height ($unknownHeight + 1) ([sbyte]::MaxValue) 'Height step'
            }

            Add-Integer $planarCoordinates $worldX
            Add-Integer $planarCoordinates $worldZ
            Add-Integer $heights $height
        }

        $pointTotal += $zone.Points.Count
        if ($zone.Kind -eq $areaKind) {
            $areaEntries++
            $areaPoints += $zone.Points.Count
        }

        if ($zone.FateOnly) {
            $fateOnlyEntries++
        }
    }

    $entryTotal += $nameZones.Count
}

$properties = @(
    "    public const float HeightStep = $heightStep;"
    "    public const sbyte UnknownHeight = $unknownHeight;"
    Format-SpanProperty 'ushort' 'NameIds' $nameIds 16
    Format-SpanProperty 'ushort' 'NameEntryStarts' $nameEntryStarts 16
    Format-SpanProperty 'byte' 'NameEntryCounts' $nameEntryCounts 24
    Format-SpanProperty 'ushort' 'EntryTerritoryIds' $entryTerritoryIds 16
    Format-SpanProperty 'ushort' 'EntryPointStarts' $entryPointStarts 16
    Format-SpanProperty 'byte' 'EntryPointCounts' $entryPointCounts 24
    Format-SpanProperty 'byte' 'EntryKinds' $entryKinds 32
    Format-SpanProperty 'byte' 'EntryFateOnly' $entryFateOnly 32
    Format-SpanProperty 'short' 'PlanarCoordinates' $planarCoordinates 16
    Format-SpanProperty 'sbyte' 'Heights' $heights 24
)

$source = @(
    '// <auto-generated/>'
    '// Written by tools/MobSpawns/Build-MobSpawns.ps1 from the dataset credited in THIRD-PARTY-NOTICES.md and the game''s sheets. Regenerate instead of editing.'
    ''
    'namespace AutoHuntGrinder.Core.Spawns.Data;'
    ''
    'internal static class MobSpawnTable'
    '{'
    ($properties -join "`n`n")
    '}'
    ''
) -join "`n"

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
[IO.File]::WriteAllText($OutputPath, $source, [Text.UTF8Encoding]::new($false))
$outputBytes = (Get-Item $OutputPath).Length

Write-Host "Wrote $OutputPath ($(Format-Number ($outputBytes / 1024.0) '0.0') KiB)"
Write-Host "Position reports: $openWorldReports in open-world zones; $corruptReports level 0 reports dropped as corrupt; $skippedNames dataset names skipped for a missing or empty BNpcName row"
Write-Host "Names: $($nameIds.Count) ($areaOnlyNames with sub-area points only, $fateOnlyNames with FATE positions only)"
Write-Host "Zones: $entryTotal ($fateOnlyEntries from FATE positions only, $areaEntries from sub-area labels)"
Write-Host "Points: $pointTotal ($areaPoints sub-area points)"
Write-Host "Hunting Log targets: $($logTargetIds.Count)"
Write-Host "  with reported points in a listed zone: $withPoints"
Write-Host "  with sub-area points only: $withArea"
Write-Host "  with FATE points only: $withFatePoints"
Write-Host "  in a duty, no points: $inDuty"
Write-Host "  with nothing: $($uncovered.Count)"
foreach ($line in $uncovered) {
    Write-Host "    $line"
}
