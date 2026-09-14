using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int DailyMarkSearchBudgetMs = 600_000;
    private const int EliteMarkSearchBudgetMs = 1_200_000;
    // Several copies of a daily mark roam at once and respawn quickly, so a second lap finds fresh ones.
    private const int DailyMarkSearchLaps = 2;
    // An elite mark is a single roamer; circling its points a few times is how it turns up.
    private const int EliteMarkSearchLaps = 4;
    // A sub-area point is only its map label, often away from where the mobs roam, so one stop there is rarely enough.
    private const int AreaSearchLaps = 4;
    // Spawn points are approximate, and a mark near one is in view long before the point itself.
    private const float MarkSweepArriveMeters = 20f;
    // Anywhere inside the sub-area will do; its label is not where the mobs stand.
    private const float AreaSweepArriveMeters = 45f;
    private const int MarkScanIntervalMs = 250;
    private const int MarkPointSettleMs = 1_500;
    // A sub-area's mobs are spread over it on respawn timers, so the character waits long enough for one to wander into view.
    private const int AreaPointSettleMs = 8_000;
    // A sub-area label marks where its name is printed, about 140 y from where its mobs were reported on the median, so
    // the sweep also circles each label at close to that distance.
    private const float AreaRingRadiusMeters = 120f;
    private const int AreaRingPoints = 6;
    private const int MaxMarkSightingsPerPoint = 6;
    private const int MaxMarkKnockouts = 3;
    private const int MaxUncountedMarkKills = 3;
    // Spawn reports carry no height; a probe above every terrain in the game finds the highest floor under the point.
    private const float MarkHeightProbeY = 1024f;
    private const float MarkHeightSearchHalfExtentMeters = 5f;
    private const float MarkHeightFallbackHalfExtentMeters = 10f;
    private const float MarkHeightFallbackVerticalMeters = 300f;
    // Height hints are stored in 10 yalm steps, so the floor search starts one step above the hint.
    private const float MarkHeightHintLiftMeters = 10f;
    private const int MaxMarkPointsOnStack = 64;

    private enum MarkLeg { Arrived, Sighted, Failed }

    private HuntPhase markPhase = HuntPhase.Idle;

    internal HuntPhase MarkPhase
    {
        get => markPhase;
        private set
        {
            if (markPhase == value)
            {
                return;
            }

            markPhase = value;
            OnMarkPhaseChanged(value);
        }
    }

    // Lets a run loop surface the hunt's phase the moment it changes instead of polling for it.
    private protected virtual void OnMarkPhaseChanged(HuntPhase phase)
    {
    }

    protected async Task<MarkOutcome> HuntMark(HuntBill bill, HuntTarget target)
    {
        Diag($"Hunt: {target.Name} for {bill.Name} at {target.Killed}/{target.Needed} (target row {target.TargetRowId}, name {target.NameId}, territory {target.TerritoryId}, {ConditionTag()})");
        var progress = ReadBillProgress(bill, target, force: true, out var status);
        if (progress.Done)
        {
            Diag($"Hunt: {target.Name} is already done on {bill.Name}");
            return MarkOutcome.Killed;
        }

        if (!progress.Tracked)
        {
            Warn($"Hunt: {bill.Name} reads {status}, so kills on {target.Name} cannot count");
            return MarkOutcome.NotHeld;
        }

        if (!CombatAnswering())
        {
            return MarkOutcome.CombatUnavailable;
        }

        var hunt = CreateMarkHunt(bill, target, status);
        if (hunt is null)
        {
            Warn($"Hunt: no spawn points and no FATE are known for {target.Name} in {target.ZoneName}; skipping it");
            return MarkOutcome.Unsupported;
        }

        return await RunHunt(hunt);
    }

    // A Hunting Log target or custom mob is hunted like a daily mark: its spawn points in one territory, never inside a
    // FATE, and only copies no other party has claimed.
    protected async Task<MarkOutcome> HuntQuarry(HuntObjective objective)
    {
        var name = ObjectiveProgress.Name(objective);
        var sourceName = ObjectiveProgress.SourceName(objective);
        Diag($"Hunt: {name} for {sourceName} at {objective.Killed}/{objective.Needed} ({objective.Source} key {objective.SourceKey}, name {objective.NameId}, territory {objective.TerritoryId}, {ConditionTag()})");
        var progress = ReadQuarryProgress(objective, force: true);
        if (progress.Done)
        {
            Diag($"Hunt: {name} is already done for {sourceName}");
            return MarkOutcome.Killed;
        }

        if (!progress.Tracked)
        {
            Warn($"Hunt: {sourceName} reads {ObjectiveProgress.Describe(objective)}, so kills on {name} cannot count");
            return MarkOutcome.NotHeld;
        }

        if (!CombatAnswering())
        {
            return MarkOutcome.CombatUnavailable;
        }

        var hunt = CreateQuarryHunt(objective, name, sourceName);
        if (hunt is null)
        {
            var where = objective.TerritoryId != 0 ? $" in {TerritoryNames.Of(objective.TerritoryId)}" : string.Empty;
            Warn($"Hunt: no spawn points are known for {name}{where}; skipping it");
            return MarkOutcome.Unsupported;
        }

        return await RunHunt(hunt);
    }

    private bool CombatAnswering()
    {
        if (BossModIPC.Instance.IsAvailable)
        {
            return true;
        }

        Warn("Hunt: the combat plugin is not answering, so marks cannot be fought");
        return false;
    }

    private async Task<MarkOutcome> RunHunt(MarkHuntContext hunt)
    {
        EnsureHuntCombatPreset();
        var startedAt = Environment.TickCount64;
        var outcome = MarkOutcome.Cancelled;
        try
        {
            outcome = await RunMarkHunt(hunt);
            return outcome;
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            MarkPhase = HuntPhase.Idle;
            var final = ReadMarkProgress(hunt, force: true);
            Diag($"Hunt: {hunt.Name} ended {outcome} at {final.Killed}/{final.Needed} after {(Environment.TickCount64 - startedAt) / TimeUnits.MillisecondsPerSecond}s");
        }
    }

    private async Task<MarkOutcome> RunMarkHunt(MarkHuntContext hunt)
    {
        while (true)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return MarkOutcome.Cancelled;
            }

            var outcome = IsMarkKnockedOut()
                ? MarkOutcome.Died
                : hunt.FateId != 0 ? await HuntFateMark(hunt) : await SearchForMark(hunt);
            if (outcome != MarkOutcome.Died)
            {
                return outcome;
            }

            hunt.Knockouts++;
            if (hunt.Knockouts >= MaxMarkKnockouts)
            {
                Warn($"Hunt: knocked out {hunt.Knockouts} times hunting {hunt.Name}; giving it up");
                return MarkOutcome.Died;
            }

            Diag($"Hunt: knocked out {hunt.Knockouts}/{MaxMarkKnockouts} hunting {hunt.Name}; recovering and heading back");
            if (!await RecoverFromMarkKnockout())
            {
                return CancelToken.IsCancellationRequested ? MarkOutcome.Cancelled : MarkOutcome.Died;
            }
        }
    }

    private static MarkHuntContext? CreateMarkHunt(HuntBill bill, HuntTarget target, BillStatus startStatus)
    {
        var hasFate = MarkFates.TryGet(target.TargetRowId, out var fate);
        var hasSpawns = MarkSpawns.TryGet(target.TargetRowId, out var spawnTerritoryId, out var points);
        if (!hasSpawns && (!hasFate || target.TerritoryId == 0))
        {
            return null;
        }

        return MarkHuntContext.ForBill(bill, target, startStatus, hasSpawns ? spawnTerritoryId : target.TerritoryId, fate, points.ToArray());
    }

    private static MarkHuntContext? CreateQuarryHunt(in HuntObjective objective, string name, string sourceName)
    {
        var territoryId = ObjectivePlanner.TerritoryFor(objective);
        if (territoryId == 0 || !MobSpawns.TryGetSearchable(objective.NameId, territoryId, out var points))
        {
            return null;
        }

        var areaPoints = points[0].Kind == SpawnKind.Area;
        var positions = areaPoints ? AreaSweepPoints(points) : PositionsOf(points);
        return MarkHuntContext.ForQuarry(objective, name, sourceName, territoryId, positions, areaPoints);
    }

    private static Vector3[] PositionsOf(ReadOnlySpan<SpawnPoint> points)
    {
        var positions = new Vector3[points.Length];
        for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
        {
            positions[pointIndex] = points[pointIndex].Position;
        }

        return positions;
    }

    // Each label, then a ring around it; the ring points keep the label's unknown height, so each is snapped on arrival.
    private static Vector3[] AreaSweepPoints(ReadOnlySpan<SpawnPoint> labels)
    {
        var positions = new Vector3[labels.Length * (AreaRingPoints + 1)];
        var next = 0;
        for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            var label = labels[labelIndex].Position;
            positions[next++] = label;
            for (var step = 0; step < AreaRingPoints; step++)
            {
                var angle = MathF.Tau * step / AreaRingPoints;
                positions[next++] = label + new Vector3(MathF.Cos(angle) * AreaRingRadiusMeters, 0f, MathF.Sin(angle) * AreaRingRadiusMeters);
            }
        }

        return positions;
    }

    private async Task<MarkOutcome> SearchForMark(MarkHuntContext hunt)
    {
        if (!await EnterMarkTerritory(hunt))
        {
            return CancelToken.IsCancellationRequested ? MarkOutcome.Cancelled : MarkOutcome.Unreachable;
        }

        var points = ResolveMarkPoints(hunt);
        if (points.Length == 0)
        {
            Warn($"Hunt: none of {hunt.Name}'s spawn points has a floor under it in {hunt.ZoneName}");
            return MarkOutcome.Unreachable;
        }

        hunt.StartClock(hunt.SearchBudgetMs, MarkOutcome.NotFound);
        var laps = hunt.SearchLaps;
        var order = new int[points.Length];
        for (var lap = 1; lap <= laps; lap++)
        {
            OrderMarkPoints(points, order, Svc.Objects.LocalPlayer?.Position ?? points[0]);
            var reachedBefore = hunt.PointsReached;
            for (var orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                var scope = $"hunt-lap{lap}-point{orderIndex + 1}/{order.Length}";
                if (await SearchMarkPoint(hunt, points[order[orderIndex]], scope) is { } stop)
                {
                    return stop;
                }
            }

            if (hunt.PointsReached == reachedBefore)
            {
                Warn($"Hunt: could not reach any of {hunt.Name}'s {points.Length} spawn point(s) in {hunt.ZoneName}");
                return MarkOutcome.Unreachable;
            }

            var progress = ReadMarkProgress(hunt, force: true);
            Diag($"Hunt: lap {lap}/{laps} over {hunt.Name}'s {points.Length} {(hunt.AreaPoints ? "sub-area" : "spawn")} point(s) done at {progress.Killed}/{progress.Needed}");
        }

        return MarkOutcome.NotFound;
    }

    private async Task<MarkOutcome?> SearchMarkPoint(MarkHuntContext hunt, Vector3 point, string scope)
    {
        for (var sighting = 0; sighting <= MaxMarkSightingsPerPoint; sighting++)
        {
            if (await FightVisibleMarks(hunt) is { } fought)
            {
                return fought;
            }

            if (Svc.Condition[ConditionFlag.InCombat])
            {
                await ClearMarkAggro(scope);
            }

            if (CheckMarkState(hunt) is { } stop)
            {
                return stop;
            }

            var leg = await SweepToMarkPoint(hunt, point, scope);
            if (leg == MarkLeg.Sighted)
            {
                continue;
            }

            if (leg == MarkLeg.Failed)
            {
                return null;
            }

            hunt.PointsReached++;
            await DwellAtMarkPoint(hunt);
            return await FightVisibleMarks(hunt);
        }

        return null;
    }

    // A quarry's dwell ends as soon as one comes into view; a mark keeps its short fixed settle.
    private async Task DwellAtMarkPoint(MarkHuntContext hunt)
    {
        if (!hunt.IsQuarry)
        {
            await DelayMs(MarkPointSettleMs);
            return;
        }

        Status = hunt.SearchLabel;
        var deadline = Environment.TickCount64 + hunt.PointSettleMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (SightMark(hunt, out _))
            {
                return;
            }

            await DelayMs(MarkScanIntervalMs);
        }
    }

    // Only the direct leg can stop at a sighting. Full travel cannot, so it takes over only once the leg stalls, for its
    // recovery ladder that always makes progress.
    private async Task<MarkLeg> SweepToMarkPoint(MarkHuntContext hunt, Vector3 point, string scope)
    {
        var arriveWithin = hunt.ArriveMeters;
        if (WithinReach(point, arriveWithin))
        {
            return MarkLeg.Arrived;
        }

        MarkPhase = HuntPhase.Searching;
        var ride = TerritoryAllowsMount(hunt.TerritoryId) && FreeToMount() && (Svc.Condition[ConditionFlag.Mounted] || DistanceTo(point) > MountMinMeters);
        var sighted = false;
        var nextScanAt = 0L;

        bool StopCondition()
        {
            Status = hunt.SearchLabel;
            if (WithinReach(point, arriveWithin))
            {
                return true;
            }

            var now = Environment.TickCount64;
            if (now < nextScanAt)
            {
                return false;
            }

            nextScanAt = now + MarkScanIntervalMs;
            sighted = SightMark(hunt, out _);
            return sighted;
        }

        Diag($"{scope}: {(ride ? "riding" : "walking")} {DistanceTo(point):F0}m to {FormatPosition(point)}");
        var operation = new MoveOp(move => move.MoveInZone(point, MovementFor(ride, arriveWithin), StopCondition));
        await RunCancellable(operation, TravelBudgetMs(point), scope, StuckDetector.MoveStallAbort(scope));
        if (sighted)
        {
            Diag($"{scope}: {hunt.Name} in view; stopping to fight it");
            return MarkLeg.Sighted;
        }

        if (WithinReach(point, arriveWithin))
        {
            return MarkLeg.Arrived;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return MarkLeg.Failed;
        }

        if (operation.Fault is { } fault)
        {
            Diag($"{scope}: the direct leg faulted: {fault.Message}");
        }

        Diag($"{scope}: the direct leg ended {DistanceTo(point):F0}m short; using full travel");
        return await TravelTo(hunt.TerritoryId, point, arriveWithin) ? MarkLeg.Arrived : MarkLeg.Failed;
    }

    private async Task<bool> EnterMarkTerritory(MarkHuntContext hunt)
    {
        if (!await WaitForPlayerReady())
        {
            return false;
        }

        if (Svc.ClientState.TerritoryType != hunt.TerritoryId)
        {
            MarkPhase = HuntPhase.Travelling;
            var entry = hunt.SpawnPoints.Length > 0 ? EstimateMarkHeight(hunt.TerritoryId, hunt.SpawnPoints[0]) : Vector3.Zero;
            Diag($"Hunt: teleporting to {hunt.ZoneName} ({hunt.TerritoryId}) for {hunt.Name}");
            var reached = false;
            await RunWithStatusPinned(
                $"Teleporting to {hunt.ZoneName}",
                async () => reached = await TeleportToTerritory(hunt.TerritoryId, entry, "hunt-teleport", TravelTeleportWatchdogMs));
            if (!reached)
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    Warn($"Hunt: could not reach {hunt.ZoneName} (still in territory {Svc.ClientState.TerritoryType})");
                }

                return false;
            }
        }

        await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
        return !CancelToken.IsCancellationRequested;
    }

    private Vector3[] ResolveMarkPoints(MarkHuntContext hunt)
    {
        if (hunt.ResolvedPoints is { } cached)
        {
            return cached;
        }

        var raw = hunt.SpawnPoints;
        var resolved = new List<Vector3>(raw.Length);
        var snapped = 0;
        for (var pointIndex = 0; pointIndex < raw.Length; pointIndex++)
        {
            var point = raw[pointIndex];
            if (!float.IsNaN(point.Y) && !hunt.SnapsHintedHeights)
            {
                resolved.Add(point);
                continue;
            }

            if (SnapMarkHeight(point) is { } floor)
            {
                resolved.Add(floor);
                snapped++;
            }
        }

        if (snapped > 0 || resolved.Count < raw.Length)
        {
            Diag($"Hunt: {snapped} of {hunt.Name}'s spawn point(s) snapped to the floor, {raw.Length - resolved.Count} dropped with no floor under them");
        }

        hunt.ResolvedPoints = [.. resolved];
        return hunt.ResolvedPoints;
    }

    // Only picks the aetheryte to land at; the real height is snapped once the zone's mesh is loaded.
    private static Vector3 EstimateMarkHeight(uint territoryId, Vector3 point)
    {
        if (!float.IsNaN(point.Y))
        {
            return point;
        }

        var flat = point with { Y = 0f };
        return ZoneAetherytes.TryFindNearest(territoryId, flat, out var aetheryte) ? point with { Y = aetheryte.Position.Y } : flat;
    }

    // A height hint picks the floor nearest it, so a spawn under a bridge or below a ledge is not lifted to the top layer.
    private static Vector3? SnapMarkHeight(Vector3 point)
    {
        if (float.IsNaN(point.Y))
        {
            return SnapUnknownHeight(point);
        }

        var navmesh = NavmeshIPC.Instance;
        var lifted = point with { Y = point.Y + MarkHeightHintLiftMeters };
        return navmesh.PointOnFloor(lifted, allowUnlandable: false, MarkHeightSearchHalfExtentMeters)
            ?? navmesh.PointOnFloor(lifted, allowUnlandable: true, MarkHeightSearchHalfExtentMeters)
            ?? SnapUnknownHeight(point);
    }

    private static Vector3? SnapUnknownHeight(Vector3 point)
    {
        var navmesh = NavmeshIPC.Instance;
        var probe = point with { Y = MarkHeightProbeY };
        var floor = navmesh.PointOnFloor(probe, allowUnlandable: false, MarkHeightSearchHalfExtentMeters)
            ?? navmesh.PointOnFloor(probe, allowUnlandable: true, MarkHeightSearchHalfExtentMeters);
        if (floor is not null)
        {
            return floor;
        }

        var height = Svc.Objects.LocalPlayer?.Position.Y ?? 0f;
        return navmesh.NearestPointReachable(point with { Y = height }, MarkHeightFallbackHalfExtentMeters, MarkHeightFallbackVerticalMeters);
    }

    private static void OrderMarkPoints(Vector3[] points, int[] order, Vector3 from)
    {
        Span<bool> used = points.Length <= MaxMarkPointsOnStack ? stackalloc bool[points.Length] : new bool[points.Length];
        var position = from;
        for (var slot = 0; slot < points.Length; slot++)
        {
            var nearest = -1;
            var bestDistance = float.PositiveInfinity;
            for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                if (used[pointIndex])
                {
                    continue;
                }

                var distance = GroundDistance.SquaredBetween(position, points[pointIndex]);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                nearest = pointIndex;
            }

            used[nearest] = true;
            order[slot] = nearest;
            position = points[nearest];
        }
    }

    private MarkOutcome? CheckMarkState(MarkHuntContext hunt)
    {
        if (CancelToken.IsCancellationRequested)
        {
            return MarkOutcome.Cancelled;
        }

        if (IsMarkKnockedOut())
        {
            return MarkOutcome.Died;
        }

        var progress = ReadMarkProgress(hunt, force: false);
        if (progress.Done)
        {
            return MarkOutcome.Killed;
        }

        if (!progress.Tracked)
        {
            return MarkOutcome.NotHeld;
        }

        if (hunt.UncountedKills >= MaxUncountedMarkKills)
        {
            Warn($"Hunt: {hunt.UncountedKills} kills in a row on {hunt.Name} did not count on {hunt.SourceName}; giving it up");
            return MarkOutcome.KillsNotCounted;
        }

        return Environment.TickCount64 >= hunt.SearchDeadline ? hunt.ExpiredOutcome : null;
    }

    private static MarkProgress ReadBillProgress(HuntBill bill, HuntTarget target, bool force, out BillStatus status)
    {
        MarkBillReader.Refresh(force);
        status = MarkBillReader.Status(bill.MarkIndex);
        var complete = status == BillStatus.Done;
        var held = status is BillStatus.Held or BillStatus.Stale;
        return MarkBillReader.TryFindTarget(bill.MarkIndex, target.TargetRowId, out var listed)
            ? new MarkProgress(complete, held, true, listed.Killed, listed.Needed)
            : new MarkProgress(complete, held, false, 0, target.Needed);
    }

    // A quarry stays listed while its source tracks it, and a finished log reads every count full, so Done needs no
    // special case.
    private static MarkProgress ReadQuarryProgress(in HuntObjective objective, bool force)
    {
        ObjectiveProgress.Refresh(objective.Source, force);
        return new MarkProgress(false, ObjectiveProgress.IsTracked(objective), true, ObjectiveProgress.Killed(objective), ObjectiveProgress.Needed(objective));
    }

    // A held bill leaves the held set when its last mark falls. One held from an earlier reset then reads as posted again,
    // with no marks listed, so that flip is the kill that finished it.
    private static MarkProgress ReadMarkProgress(MarkHuntContext hunt, bool force)
    {
        if (hunt.IsQuarry)
        {
            return ReadQuarryProgress(hunt.Objective, force);
        }

        var progress = ReadBillProgress(hunt.Bill, hunt.Target, force, out var status);
        var finished = hunt.StartStatus is BillStatus.Held or BillStatus.Stale && status == BillStatus.Available;
        return finished ? new MarkProgress(true, false, false, hunt.Needed, hunt.Needed) : progress;
    }

    // For log lines only.
    private static string DescribeProgress(MarkHuntContext hunt)
        => hunt.IsQuarry ? ObjectiveProgress.Describe(hunt.Objective) : MarkBillReader.Status(hunt.Bill.MarkIndex).ToString();

    private bool SightMark(MarkHuntContext hunt, out MarkSighting sighting)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            sighting = default;
            return false;
        }

        var found = MarkFinder.TryFindNearest(hunt.NameId, hunt.FateId, hunt.HonorsClaims, player.Position, hunt.Ignored, out sighting, out var claimed);
        if (claimed > 0 && !hunt.ClaimSkipLogged)
        {
            hunt.ClaimSkipLogged = true;
            Diag($"Hunt: passing over {claimed} {hunt.Name} another player's party has claimed; kills on them would not count");
        }

        return found;
    }

    private protected static bool IsMarkKnockedOut() => Svc.Condition[ConditionFlag.Unconscious];

    // Complete also covers a source that closed with this kill, since the game may clear its counts when it does.
    private readonly record struct MarkProgress(bool Complete, bool Held, bool Listed, int Killed, int Needed)
    {
        public bool Tracked => Complete || (Listed && Held);

        public bool Done => Complete || (Listed && Killed >= Needed);
    }

    private sealed class MarkHuntContext
    {
        // Enough to stop re-picking the few copies that could not be reached or finished.
        private const int MaxIgnoredInstances = 8;

        private readonly ulong[] ignored = new ulong[MaxIgnoredInstances];
        private int ignoredCount;
        private int ignoredNext;

        private MarkHuntContext(string name, uint nameId, string sourceName, int needed, uint territoryId, MarkFate fate, Vector3[] spawnPoints, bool elite, bool areaPoints)
        {
            Name = name;
            NameId = nameId;
            SourceName = sourceName;
            Needed = needed;
            TerritoryId = territoryId;
            Fate = fate;
            SpawnPoints = spawnPoints;
            Elite = elite;
            AreaPoints = areaPoints;
            ZoneName = TerritoryNames.Of(territoryId);
            SearchLabel = $"Searching for {name} in {ZoneName}";
            ApproachLabel = $"Closing in on {name}";
            FightLabel = $"Fighting {name}";
        }

        public static MarkHuntContext ForBill(HuntBill bill, HuntTarget target, BillStatus startStatus, uint territoryId, MarkFate fate, Vector3[] spawnPoints)
            => new(target.Name, target.NameId, bill.Name, target.Needed, territoryId, fate, spawnPoints, bill.Cadence == BillCadence.Weekly, areaPoints: false)
            {
                Bill = bill,
                Target = target,
                StartStatus = startStatus,
            };

        public static MarkHuntContext ForQuarry(in HuntObjective objective, string name, string sourceName, uint territoryId, Vector3[] spawnPoints, bool areaPoints)
            => new(name, objective.NameId, sourceName, objective.Needed, territoryId, default, spawnPoints, elite: false, areaPoints)
            {
                Objective = objective,
                IsQuarry = true,
            };

        public string Name { get; }

        public uint NameId { get; }

        public string SourceName { get; }

        public int Needed { get; }

        public HuntBill Bill { get; private init; }

        public HuntTarget Target { get; private init; }

        public BillStatus StartStatus { get; private init; }

        public HuntObjective Objective { get; private init; }

        public bool IsQuarry { get; private init; }

        public uint TerritoryId { get; }

        public MarkFate Fate { get; }

        public uint FateId => Fate.FateId;

        public Vector3[] SpawnPoints { get; }

        public Vector3[]? ResolvedPoints { get; set; }

        public bool Elite { get; }

        public bool AreaPoints { get; }

        // FATE mobs and hunt Notorious Monsters, which every elite mark is, credit everyone who fights them; only an
        // ordinary mob belongs to the party that pulled it.
        public bool HonorsClaims => FateId == 0 && !Elite;

        // The game keeps no counter for a custom mob, so the kill ledger has to see it fall.
        public bool WatchesKills => IsQuarry && Objective.Source == ObjectiveSource.Custom;

        // Bill spawns carry measured heights; the position dataset's are coarse hints.
        public bool SnapsHintedHeights => IsQuarry;

        public float ArriveMeters => AreaPoints ? AreaSweepArriveMeters : MarkSweepArriveMeters;

        public int PointSettleMs => AreaPoints ? AreaPointSettleMs : MarkPointSettleMs;

        public int SearchBudgetMs => Elite ? EliteMarkSearchBudgetMs : DailyMarkSearchBudgetMs;

        public int SearchLaps => Elite ? EliteMarkSearchLaps : AreaPoints ? AreaSearchLaps : DailyMarkSearchLaps;

        public bool ClaimSkipLogged { get; set; }

        public string ZoneName { get; }

        public string SearchLabel { get; }

        public string ApproachLabel { get; }

        public string FightLabel { get; }

        public long SearchDeadline { get; private set; }

        public MarkOutcome ExpiredOutcome { get; private set; } = MarkOutcome.NotFound;

        public int PointsReached { get; set; }

        public int UncountedKills { get; set; }

        public int Knockouts { get; set; }

        public long NextSyncAttemptAt { get; set; }

        public ReadOnlySpan<ulong> Ignored => ignored.AsSpan(0, ignoredCount);

        public void StartClock(int budgetMs, MarkOutcome expiredOutcome)
        {
            SearchDeadline = Environment.TickCount64 + budgetMs;
            ExpiredOutcome = expiredOutcome;
        }

        public void Ignore(ulong gameObjectId)
        {
            ignored[ignoredNext] = gameObjectId;
            ignoredNext = (ignoredNext + 1) % MaxIgnoredInstances;
            ignoredCount = Math.Min(ignoredCount + 1, MaxIgnoredInstances);
        }
    }
}
