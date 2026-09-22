#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SpawnPointsPath,
    [string] $SheetDirectory,
    [switch] $Download,
    [string] $OutputPath = (Join-Path $PSScriptRoot '../../AutoHuntGrinder/Core/Marks/Data/HuntSpawnTable.g.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cache = Join-Path ([IO.Path]::GetTempPath()) 'AutoHuntGrinder-HuntSpawns'
$spawnPointsFile = 'SpawnPointData.json'
if (-not $SpawnPointsPath) {
    $SpawnPointsPath = Join-Path $cache $spawnPointsFile
}

if (-not $SheetDirectory) {
    $SheetDirectory = Join-Path $cache 'sheets'
}

$invariant = [Globalization.CultureInfo]::InvariantCulture
$noticesPath = Join-Path $PSScriptRoot '../../THIRD-PARTY-NOTICES.md'
$sheetSource = 'https://raw.githubusercontent.com/xivapi/ffxiv-datamining/a67c23b00fe8cb254855d06b59845958b55d28f3/csv/en'
$sheetNames = @('Map', 'TerritoryType')
$openWorldUse = 1
# One bit per rank, as HuntMarkRegistry.RankBit lays them out: 1 shifted by the HuntMarkRank value (B 1, A 2, S 3).
$rankBits = [ordered]@{ B = 2; A = 4; S = 8 }

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

function Format-Number([double] $value, [string] $format) {
    return $value.ToString($format, $invariant)
}

function Format-Float([double] $value) {
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
    New-Item -ItemType Directory -Force -Path $SheetDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $SpawnPointsPath) | Out-Null
    Save-Source (Get-PinnedDatasetUrl $spawnPointsFile) $SpawnPointsPath
    foreach ($name in $sheetNames) {
        Save-Source "$sheetSource/$name.csv" (Join-Path $SheetDirectory "$name.csv")
    }
}

foreach ($inputPath in @($SpawnPointsPath, $SheetDirectory)) {
    if (-not (Test-Path $inputPath)) {
        throw "Input not found: $inputPath. Run with -Download to fetch the pinned sources."
    }
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$maps = Read-Sheet 'Map'
$territories = Read-Sheet 'TerritoryType'
$zones = Read-Json $SpawnPointsPath

$entries = [Collections.Generic.SortedDictionary[int, object]]::new()
$unflagged = 0
foreach ($zone in $zones) {
    $territoryId = [int]$zone.MapID
    $territory = $territories["$territoryId"]
    if ($null -eq $territory) {
        throw "Territory $territoryId ($($zone.MapName)) is not in the TerritoryType sheet."
    }

    if ([int]$territory.TerritoryIntendedUse -ne $openWorldUse) {
        throw "Territory $territoryId ($($zone.MapName)) is not an open-world zone."
    }

    if ($entries.ContainsKey($territoryId)) {
        throw "Territory $territoryId ($($zone.MapName)) is listed twice."
    }

    $map = $maps[$territory.Map]
    $sizeFactor = [double]$map.SizeFactor
    $offsetX = [double]$map.OffsetX
    $offsetY = [double]$map.OffsetY
    $points = [Collections.Generic.List[object]]::new()
    foreach ($position in $zone.Positions) {
        $bits = 0
        foreach ($rank in $rankBits.Keys) {
            if ($position[$rank]) {
                $bits = $bits -bor $rankBits[$rank]
            }
        }

        if ($bits -eq 0) {
            $unflagged++
            continue
        }

        $points.Add([pscustomobject]@{
                X    = ConvertFrom-MapCoordinate ([double]$position.X) $sizeFactor $offsetX
                Z    = ConvertFrom-MapCoordinate ([double]$position.Y) $sizeFactor $offsetY
                Bits = $bits
            })
    }

    $entries[$territoryId] = [pscustomobject]@{ Name = $zone.MapName; Points = $points }
}

$territoryIds = [Collections.Generic.List[string]]::new()
$pointStarts = [Collections.Generic.List[string]]::new()
$pointCounts = [Collections.Generic.List[string]]::new()
$pointRankBits = [Collections.Generic.List[string]]::new()
$coordinates = [Collections.Generic.List[string]]::new()
$pointTotal = 0
$rankTotals = [ordered]@{ B = 0; A = 0; S = 0 }
foreach ($territoryId in $entries.Keys) {
    $entry = $entries[$territoryId]
    Assert-Fits $territoryId ([uint16]::MaxValue) 'Territory id'
    Assert-Fits $pointTotal ([uint16]::MaxValue) 'Point start'
    Assert-Fits $entry.Points.Count ([byte]::MaxValue) 'Point count'
    $territoryIds.Add((Format-Number $territoryId '0'))
    $pointStarts.Add((Format-Number $pointTotal '0'))
    $pointCounts.Add((Format-Number $entry.Points.Count '0'))
    foreach ($point in $entry.Points) {
        $pointRankBits.Add((Format-Number $point.Bits '0'))
        $coordinates.Add((Format-Float $point.X))
        $coordinates.Add((Format-Float $point.Z))
        foreach ($rank in $rankBits.Keys) {
            if ($point.Bits -band $rankBits[$rank]) {
                $rankTotals[$rank]++
            }
        }
    }

    $pointTotal += $entry.Points.Count
}

$properties = @(
    Format-SpanProperty 'ushort' 'TerritoryIds' $territoryIds 16
    Format-SpanProperty 'ushort' 'PointStarts' $pointStarts 16
    Format-SpanProperty 'byte' 'PointCounts' $pointCounts 24
    Format-SpanProperty 'byte' 'RankBits' $pointRankBits 24
    Format-SpanProperty 'float' 'Coordinates' $coordinates 2
)

$source = @(
    '// <auto-generated/>'
    '// Written by tools/HuntSpawns/Build-HuntSpawns.ps1 from the dataset credited in THIRD-PARTY-NOTICES.md. Regenerate instead of editing.'
    ''
    'namespace AutoHuntGrinder.Core.Marks.Data;'
    ''
    'internal static class HuntSpawnTable'
    '{'
    ($properties -join "`n`n")
    '}'
    ''
) -join "`n"

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
[IO.File]::WriteAllText($OutputPath, $source, [Text.UTF8Encoding]::new($false))

Write-Host "Wrote $OutputPath"
Write-Host "Zones: $($entries.Count); points: $pointTotal ($($rankTotals['B']) for B ranks, $($rankTotals['A']) for A ranks, $($rankTotals['S']) for S ranks); $unflagged left out with no rank"
foreach ($territoryId in $entries.Keys) {
    $entry = $entries[$territoryId]
    $counts = @{ B = 0; A = 0; S = 0 }
    foreach ($point in $entry.Points) {
        foreach ($rank in $rankBits.Keys) {
            if ($point.Bits -band $rankBits[$rank]) {
                $counts[$rank]++
            }
        }
    }

    Write-Host ('  {0,5} {1,-28} {2,3} points: B {3,3}, A {4,3}, S {5,3}' -f $territoryId, $entry.Name, $entry.Points.Count, $counts['B'], $counts['A'], $counts['S'])
}
