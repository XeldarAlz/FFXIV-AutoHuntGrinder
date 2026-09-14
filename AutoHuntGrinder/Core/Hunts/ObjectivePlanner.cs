using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHuntGrinder.Core.Hunts;

internal static class ObjectivePlanner
{
    private static readonly Vector3 unknownAnchor = new(float.NaN);

    // A pinned territory stands as it is; otherwise the current territory when a search can find the mob there, else the
    // busiest territory that can. 0 when none can.
    public static uint TerritoryFor(in HuntObjective objective)
    {
        if (objective.TerritoryId != 0)
        {
            return objective.TerritoryId;
        }

        uint currentTerritory = Svc.ClientState.TerritoryType;
        return MobSpawns.TryGetSearchable(objective.NameId, currentTerritory, out _)
            ? currentTerritory
            : MobSpawns.FirstSearchableTerritory(objective.NameId);
    }

    public static bool CanHunt(in HuntObjective objective)
    {
        var territoryId = TerritoryFor(objective);
        return territoryId != 0 && MobSpawns.TryGetSearchable(objective.NameId, territoryId, out _);
    }

    public static HuntObjective[] Plan(IReadOnlyList<HuntObjective> objectives)
    {
        if (objectives.Count == 0)
        {
            return [];
        }

        var resolved = new List<HuntObjective>(objectives.Count);
        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            var objective = objectives[objectiveIndex];
            resolved.Add(objective with { TerritoryId = TerritoryFor(objective) });
        }

        uint currentTerritory = Svc.ClientState.TerritoryType;
        var origin = Svc.Objects.LocalPlayer?.Position;
        var territories = OrderTerritories(resolved, currentTerritory);
        var plan = new List<HuntObjective>(resolved.Count);
        for (var territoryIndex = 0; territoryIndex < territories.Count; territoryIndex++)
        {
            var territoryId = territories[territoryIndex];
            AppendTerritory(plan, resolved, territoryId, territoryId == currentTerritory ? origin : null);
        }

        return [.. plan];
    }

    // The current territory comes first; a teleport inside a region costs less, so the rest follow region by region.
    private static List<uint> OrderTerritories(List<HuntObjective> objectives, uint currentTerritory)
    {
        var remaining = new List<uint>();
        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            if (!remaining.Contains(objectives[objectiveIndex].TerritoryId))
            {
                remaining.Add(objectives[objectiveIndex].TerritoryId);
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

    // Starts from where the character stands, or from the first objective's spawns when it lands in the territory fresh.
    private static void AppendTerritory(List<HuntObjective> plan, List<HuntObjective> objectives, uint territoryId, Vector3? origin)
    {
        var pending = new List<HuntObjective>();
        var anchors = new List<Vector3>();
        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            if (objectives[objectiveIndex].TerritoryId != territoryId)
            {
                continue;
            }

            pending.Add(objectives[objectiveIndex]);
            anchors.Add(AnchorOf(objectives[objectiveIndex]));
        }

        var position = origin ?? FirstKnown(anchors);
        var used = new bool[pending.Count];
        for (var placed = 0; placed < pending.Count; placed++)
        {
            var pick = PickNearest(anchors, used, position);
            used[pick] = true;
            plan.Add(pending[pick]);
            if (IsKnown(anchors[pick]))
            {
                position = anchors[pick];
            }
        }
    }

    // Always picks an unused objective; one with no known anchor sorts after every one that has one.
    private static int PickNearest(List<Vector3> anchors, bool[] used, Vector3 position)
    {
        var pick = -1;
        var bestDistance = float.PositiveInfinity;
        for (var anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            if (used[anchorIndex])
            {
                continue;
            }

            var distance = IsKnown(anchors[anchorIndex]) && IsKnown(position)
                ? GroundDistance.SquaredBetween(position, anchors[anchorIndex])
                : float.MaxValue;
            if (pick >= 0 && distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            pick = anchorIndex;
        }

        return pick;
    }

    private static Vector3 FirstKnown(List<Vector3> anchors)
    {
        for (var anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            if (IsKnown(anchors[anchorIndex]))
            {
                return anchors[anchorIndex];
            }
        }

        return unknownAnchor;
    }

    private static Vector3 AnchorOf(in HuntObjective objective)
        => MobSpawns.TryGetSearchable(objective.NameId, objective.TerritoryId, out var points) ? points[0].Position : unknownAnchor;

    // Only X and Z order the plan; an unknown height still leaves a usable anchor.
    private static bool IsKnown(Vector3 anchor) => !float.IsNaN(anchor.X) && !float.IsNaN(anchor.Z);

    private static uint RegionOf(uint territoryId)
        => Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceNameRegion.RowId ?? 0;
}
