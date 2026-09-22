using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHuntGrinder.Core.Hunts;

// Every territory is visited once: the current one first, then the rest region by region, since a teleport inside a
// region costs less and keeps the hops short. Inside a territory the stops run nearest-next from where the character
// stands, or from the aetheryte it will land at; a mark listed on two bills stays back to back so one trip serves both,
// and FATE-bound marks go last because they may have to wait for their FATE.
internal static class RoutePlanner
{
    private static readonly Vector3 unknownAnchor = new(float.NaN);

    public static HuntStop[] Plan(IReadOnlyList<HuntBill> bills)
    {
        var stops = Collect(bills, supported: true);
        if (stops.Count == 0)
        {
            return [];
        }

        var currentTerritory = Svc.ClientState.TerritoryType;
        var origin = Svc.Objects.LocalPlayer?.Position;
        var territories = OrderTerritories(stops, currentTerritory);
        var route = new List<HuntStop>(stops.Count);
        for (var territoryIndex = 0; territoryIndex < territories.Count; territoryIndex++)
        {
            var territoryId = territories[territoryIndex];
            AppendTerritory(route, stops, territoryId, territoryId == currentTerritory ? origin : null);
        }

        return [.. route];
    }

    public static HuntStop[] Huntable(IReadOnlyList<HuntBill> bills) => [.. Collect(bills, supported: true)];

    // Marks that still need kills but can be neither searched for nor waited on: no spawn points and no FATE.
    public static HuntStop[] Unsupported(IReadOnlyList<HuntBill> bills) => [.. Collect(bills, supported: false)];

    public static bool CanHunt(in HuntTarget target)
        => MarkSpawns.IsSupported(target.TargetRowId)
        || HuntSpawns.Covers(target.NameId, target.TerritoryId)
        || (target.TerritoryId != 0 && MarkFates.FateIdOf(target.TargetRowId) != 0);

    // The zone a search for the target runs in, with the points its datasets know there: the zone those points are in,
    // else the bill's own, else the one the hunt mark registry lists the target in.
    public static uint SearchTerritoryOf(in HuntTarget target, out ReadOnlySpan<Vector3> points)
    {
        if (MarkSpawns.TryGet(target.TargetRowId, out var spawnTerritoryId, out points))
        {
            return spawnTerritoryId;
        }

        return target.TerritoryId != 0 ? target.TerritoryId : HuntMarkRegistry.SpawnTerritoryOf(target.NameId);
    }

    private static List<HuntStop> Collect(IReadOnlyList<HuntBill> bills, bool supported)
    {
        MarkBillReader.Refresh(force: true);
        var stops = new List<HuntStop>();
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var bill = bills[billIndex];
            var status = MarkBillReader.Status(bill.MarkIndex);
            if (status is not (BillStatus.Held or BillStatus.Stale))
            {
                continue;
            }

            var targets = MarkBillReader.Targets(bill.MarkIndex);
            for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                var target = targets[targetIndex];
                if (target.Done)
                {
                    continue;
                }

                if (CanHunt(target) != supported)
                {
                    continue;
                }

                var fateId = MarkFates.FateIdOf(target.TargetRowId);
                stops.Add(new HuntStop(bill, target, SearchTerritoryOf(target, out _), fateId));
            }
        }

        return stops;
    }

    private static List<uint> OrderTerritories(List<HuntStop> stops, uint currentTerritory)
    {
        var remaining = new List<uint>();
        for (var stopIndex = 0; stopIndex < stops.Count; stopIndex++)
        {
            if (!remaining.Contains(stops[stopIndex].TerritoryId))
            {
                remaining.Add(stops[stopIndex].TerritoryId);
            }
        }

        var ordered = new List<uint>(remaining.Count);
        if (remaining.Remove(currentTerritory))
        {
            ordered.Add(currentTerritory);
        }

        var region = RegionOf(currentTerritory);
        while (remaining.Count > 0)
        {
            var next = 0;
            for (var candidateIndex = 0; candidateIndex < remaining.Count; candidateIndex++)
            {
                if (RegionOf(remaining[candidateIndex]) == region)
                {
                    next = candidateIndex;
                    break;
                }
            }

            var territoryId = remaining[next];
            remaining.RemoveAt(next);
            ordered.Add(territoryId);
            region = RegionOf(territoryId);
        }

        return ordered;
    }

    private static void AppendTerritory(List<HuntStop> route, List<HuntStop> stops, uint territoryId, Vector3? origin)
    {
        var pending = new List<HuntStop>();
        var anchors = new List<Vector3>();
        for (var stopIndex = 0; stopIndex < stops.Count; stopIndex++)
        {
            if (stops[stopIndex].TerritoryId != territoryId)
            {
                continue;
            }

            pending.Add(stops[stopIndex]);
            anchors.Add(AnchorOf(stops[stopIndex].Target));
        }

        var start = origin ?? LandingNear(territoryId, anchors);
        var used = new bool[pending.Count];
        var position = AppendNearestNext(route, pending, anchors, used, start, fateBound: false);
        AppendNearestNext(route, pending, anchors, used, position, fateBound: true);
    }

    private static Vector3 AppendNearestNext(List<HuntStop> route, List<HuntStop> pending, List<Vector3> anchors, bool[] used, Vector3 start, bool fateBound)
    {
        var position = start;
        uint previousNameId = 0;
        for (var placed = 0; placed < pending.Count; placed++)
        {
            var pick = PickSameMark(pending, used, previousNameId, fateBound);
            if (pick < 0)
            {
                pick = PickNearest(pending, anchors, used, position, fateBound);
            }

            if (pick < 0)
            {
                break;
            }

            used[pick] = true;
            route.Add(pending[pick]);
            previousNameId = pending[pick].Target.NameId;
            if (IsKnown(anchors[pick]))
            {
                position = anchors[pick];
            }
        }

        return position;
    }

    private static int PickSameMark(List<HuntStop> pending, bool[] used, uint nameId, bool fateBound)
    {
        if (nameId == 0)
        {
            return -1;
        }

        for (var stopIndex = 0; stopIndex < pending.Count; stopIndex++)
        {
            if (!used[stopIndex] && pending[stopIndex].IsFateBound == fateBound && pending[stopIndex].Target.NameId == nameId)
            {
                return stopIndex;
            }
        }

        return -1;
    }

    private static int PickNearest(List<HuntStop> pending, List<Vector3> anchors, bool[] used, Vector3 position, bool fateBound)
    {
        var pick = -1;
        var bestDistance = float.PositiveInfinity;
        for (var stopIndex = 0; stopIndex < pending.Count; stopIndex++)
        {
            if (used[stopIndex] || pending[stopIndex].IsFateBound != fateBound)
            {
                continue;
            }

            var distance = IsKnown(anchors[stopIndex]) && IsKnown(position)
                ? GroundDistance.SquaredBetween(position, anchors[stopIndex])
                : float.MaxValue;
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            pick = stopIndex;
        }

        return pick;
    }

    // The aetheryte nearest any stop is where the teleport in lands, so the walk starts there.
    private static Vector3 LandingNear(uint territoryId, List<Vector3> anchors)
    {
        var landing = unknownAnchor;
        var bestDistance = float.PositiveInfinity;
        for (var anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            var anchor = anchors[anchorIndex];
            if (!IsKnown(anchor))
            {
                continue;
            }

            var probe = float.IsNaN(anchor.Y) ? anchor with { Y = 0f } : anchor;
            if (!ZoneAetherytes.TryFindNearest(territoryId, probe, out var aetheryte))
            {
                continue;
            }

            var distance = GroundDistance.SquaredBetween(aetheryte.Position, anchor);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            landing = aetheryte.Position;
        }

        return landing;
    }

    private static Vector3 AnchorOf(HuntTarget target)
    {
        if (MarkSpawns.TryGet(target.TargetRowId, out _, out var points) && points.Length > 0)
        {
            return points[0];
        }

        return HuntSpawns.TryGetAnchor(target.NameId, target.TerritoryId, out var anchor) ? anchor : unknownAnchor;
    }

    // Only X and Z order the route; an unknown height still leaves a usable anchor.
    private static bool IsKnown(Vector3 anchor) => !float.IsNaN(anchor.X) && !float.IsNaN(anchor.Z);

    private static uint RegionOf(uint territoryId)
        => Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceNameRegion.RowId ?? 0;
}
