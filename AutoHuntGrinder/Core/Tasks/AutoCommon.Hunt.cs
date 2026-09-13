using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
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
    // Spawn points are approximate, and a mark near one is in view long before the point itself.
    private const float MarkSweepArriveMeters = 20f;
    private const int MarkScanIntervalMs = 250;
    private const int MarkPointSettleMs = 1_500;
    private const int MaxMarkSightingsPerPoint = 6;
    private const int MaxMarkKnockouts = 3;
    private const int MaxUncountedMarkKills = 3;
    // Spawn reports carry no height; a probe above every terrain in the game finds the highest floor under the point.
    private const float MarkHeightProbeY = 1024f;
    private const float MarkHeightSearchHalfExtentMeters = 5f;
    private const float MarkHeightFallbackHalfExtentMeters = 10f;
    private const float MarkHeightFallbackVerticalMeters = 300f;
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
        var progress = ReadMarkProgress(bill, target, force: true);
        if (progress.Done)
        {
            Diag($"Hunt: {target.Name} is already done on {bill.Name}");
            return MarkOutcome.Killed;
        }

        if (!progress.Tracked)
        {
            Warn($"Hunt: {bill.Name} reads {progress.Status}, so kills on {target.Name} cannot count");
            return MarkOutcome.NotHeld;
        }

        if (!BossModIPC.Instance.IsAvailable)
        {
            Warn("Hunt: the combat plugin is not answering, so marks cannot be fought");
            return MarkOutcome.CombatUnavailable;
        }

        var hunt = CreateMarkHunt(bill, target, progress.Status);
        if (hunt is null)
        {
            Warn($"Hunt: no spawn points and no FATE are known for {target.Name} in {target.ZoneName}; skipping it");
            return MarkOutcome.Unsupported;
        }

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
            Diag($"Hunt: {target.Name} ended {outcome} at {final.Killed}/{final.Needed} after {(Environment.TickCount64 - startedAt) / TimeUnits.MillisecondsPerSecond}s");
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
                Warn($"Hunt: knocked out {hunt.Knockouts} times hunting {hunt.Target.Name}; giving it up");
                return MarkOutcome.Died;
            }

            Diag($"Hunt: knocked out {hunt.Knockouts}/{MaxMarkKnockouts} hunting {hunt.Target.Name}; recovering and heading back");
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

        return new MarkHuntContext(bill, target, startStatus, hasSpawns ? spawnTerritoryId : target.TerritoryId, fate, points.ToArray());
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
            Warn($"Hunt: none of {hunt.Target.Name}'s spawn points has a floor under it in {hunt.ZoneName}");
            return MarkOutcome.Unreachable;
        }

        hunt.StartClock(hunt.Elite ? EliteMarkSearchBudgetMs : DailyMarkSearchBudgetMs, MarkOutcome.NotFound);
        var laps = hunt.Elite ? EliteMarkSearchLaps : DailyMarkSearchLaps;
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
                Warn($"Hunt: could not reach any of {hunt.Target.Name}'s {points.Length} spawn point(s) in {hunt.ZoneName}");
                return MarkOutcome.Unreachable;
            }

            var progress = ReadMarkProgress(hunt, force: true);
            Diag($"Hunt: lap {lap}/{laps} over {hunt.Target.Name}'s {points.Length} spawn point(s) done at {progress.Killed}/{progress.Needed}");
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
            await DelayMs(MarkPointSettleMs);
            return await FightVisibleMarks(hunt);
        }

        return null;
    }

    // Only the direct leg can stop at a sighting. Full travel cannot, so it takes over only once the leg stalls, for its
    // recovery ladder that always makes progress.
    private async Task<MarkLeg> SweepToMarkPoint(MarkHuntContext hunt, Vector3 point, string scope)
    {
        if (WithinReach(point, MarkSweepArriveMeters))
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
            if (WithinReach(point, MarkSweepArriveMeters))
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
        var operation = new MoveOp(move => move.MoveInZone(point, MovementFor(ride, MarkSweepArriveMeters), StopCondition));
        await RunCancellable(operation, TravelBudgetMs(point), scope, StuckDetector.MoveStallAbort(scope));
        if (sighted)
        {
            Diag($"{scope}: {hunt.Target.Name} in view; stopping to fight it");
            return MarkLeg.Sighted;
        }

        if (WithinReach(point, MarkSweepArriveMeters))
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
        return await TravelTo(hunt.TerritoryId, point, MarkSweepArriveMeters) ? MarkLeg.Arrived : MarkLeg.Failed;
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
            Diag($"Hunt: teleporting to {hunt.ZoneName} ({hunt.TerritoryId}) for {hunt.Target.Name}");
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
            if (!float.IsNaN(point.Y))
            {
                resolved.Add(point);
                continue;
            }

            if (SnapUnknownHeight(point) is { } floor)
            {
                resolved.Add(floor);
                snapped++;
            }
        }

        if (snapped > 0 || resolved.Count < raw.Length)
        {
            Diag($"Hunt: {snapped} of {hunt.Target.Name}'s spawn point(s) snapped to the floor, {raw.Length - resolved.Count} dropped with no floor under them");
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
            Warn($"Hunt: {hunt.UncountedKills} kills in a row on {hunt.Target.Name} did not count on {hunt.Bill.Name}; giving it up");
            return MarkOutcome.KillsNotCounted;
        }

        return Environment.TickCount64 >= hunt.SearchDeadline ? hunt.ExpiredOutcome : null;
    }

    private static MarkProgress ReadMarkProgress(HuntBill bill, HuntTarget target, bool force)
    {
        MarkBillReader.Refresh(force);
        var status = MarkBillReader.Status(bill.MarkIndex);
        return MarkBillReader.TryFindTarget(bill.MarkIndex, target.TargetRowId, out var listed)
            ? new MarkProgress(status, true, listed.Killed, listed.Needed)
            : new MarkProgress(status, false, 0, target.Needed);
    }

    // A held bill leaves the held set when its last mark falls. One held from an earlier reset then reads as posted again,
    // with no marks listed, so that flip is the kill that finished it.
    private static MarkProgress ReadMarkProgress(MarkHuntContext hunt, bool force)
    {
        var progress = ReadMarkProgress(hunt.Bill, hunt.Target, force);
        var finished = hunt.StartStatus is BillStatus.Held or BillStatus.Stale && progress.Status == BillStatus.Available;
        return finished ? new MarkProgress(BillStatus.Done, false, hunt.Target.Needed, hunt.Target.Needed) : progress;
    }

    private bool SightMark(MarkHuntContext hunt, out MarkSighting sighting)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            sighting = default;
            return false;
        }

        var found = MarkFinder.TryFindNearest(hunt.Target.NameId, hunt.FateId, hunt.HonorsClaims, player.Position, hunt.Ignored, out sighting, out var claimed);
        if (claimed > 0 && !hunt.ClaimSkipLogged)
        {
            hunt.ClaimSkipLogged = true;
            Diag($"Hunt: passing over {claimed} {hunt.Target.Name} another player's party has claimed; kills on them would not count");
        }

        return found;
    }

    private protected static bool IsMarkKnockedOut() => Svc.Condition[ConditionFlag.Unconscious];

    // Done also covers a bill that completed with this kill, since the game may clear its counts when it does.
    private readonly record struct MarkProgress(BillStatus Status, bool Listed, int Killed, int Needed)
    {
        public bool Tracked => Status == BillStatus.Done || (Listed && Status is BillStatus.Held or BillStatus.Stale);

        public bool Done => Status == BillStatus.Done || (Listed && Killed >= Needed);
    }

    private sealed class MarkHuntContext
    {
        // Enough to stop re-picking the few copies that could not be reached or finished.
        private const int MaxIgnoredInstances = 8;

        private readonly ulong[] ignored = new ulong[MaxIgnoredInstances];
        private int ignoredCount;
        private int ignoredNext;

        public MarkHuntContext(HuntBill bill, HuntTarget target, BillStatus startStatus, uint territoryId, MarkFate fate, Vector3[] spawnPoints)
        {
            Bill = bill;
            Target = target;
            StartStatus = startStatus;
            TerritoryId = territoryId;
            Fate = fate;
            SpawnPoints = spawnPoints;
            ZoneName = TerritoryNames.Of(territoryId);
            SearchLabel = $"Searching for {target.Name} in {ZoneName}";
            ApproachLabel = $"Closing in on {target.Name}";
            FightLabel = $"Fighting {target.Name}";
        }

        public HuntBill Bill { get; }

        public HuntTarget Target { get; }

        public BillStatus StartStatus { get; }

        public uint TerritoryId { get; }

        public MarkFate Fate { get; }

        public uint FateId => Fate.FateId;

        public Vector3[] SpawnPoints { get; }

        public Vector3[]? ResolvedPoints { get; set; }

        public bool Elite => Bill.Cadence == BillCadence.Weekly;

        // FATE mobs and hunt Notorious Monsters, which every elite mark is, credit everyone who fights them; only an
        // ordinary mob belongs to the party that pulled it.
        public bool HonorsClaims => FateId == 0 && !Elite;

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
