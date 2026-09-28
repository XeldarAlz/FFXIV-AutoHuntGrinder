using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// Each pass takes the posted bills and hunts the planned route, and the next pass plans again: a finished old bill frees
// the fresh one on the board, and a mark that stayed hidden gets another look. A pass that picks up nothing and credits no
// kill ends the run, so a mark that cannot be found does not keep it going. A FATE mark whose FATE is not up does not hold
// up the route: the run looks in on its zone between other marks and waits there only once nothing else is left.
internal sealed class AutoHunt(IReadOnlyList<HuntBill> bills, AutoHuntSession session, HuntProgress progress) : AutoCommon
{
    private const int MaxHuntPasses = 5;
    private const int BillsReadableWaitMs = 30_000;

    private readonly IReadOnlyList<HuntBill> bills = bills;
    private readonly AutoHuntSession session = session;
    private readonly HuntProgress progress = progress;
    // A board that could not be reached, or a bill left there at the abandon prompt, would cost the same trip every pass.
    private readonly HashSet<byte> billsLeftOnBoard = [];
    // A twin stop shares the mark's spawn points or FATE, so a miss on one bill is a miss on the other until the next pass.
    private readonly HashSet<(uint NameId, uint TerritoryId)> missedThisPass = [];
    // Keyed by FATE, so a mark listed on two bills shares one wait.
    private readonly Dictionary<uint, MarkFateWait> fateWaits = [];
    private readonly List<HuntStop> deferredFates = [];
    private readonly List<HuntStop> upcoming = [];

    private readonly record struct Leftovers(int Marks, int Kills, int PickUps, int NoBoard, int Unhuntable, int Unreadable)
    {
        public bool NothingToHunt => Marks == 0 && PickUps == 0 && Unreadable == 0;
    }

    protected override async Task Execute()
    {
        try
        {
            await Hunt();
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            ReleaseCombatMovement("run");
        }
    }

    private protected override void OnMarkPhaseChanged(HuntPhase phase)
    {
        if (phase != HuntPhase.Idle)
        {
            ReportPhase(phase);
        }
    }

    private async Task Hunt()
    {
        Status = "Reading your bills";
        if (!await WaitUntilTimed(BillsReadable, BillsReadableWaitMs, "hunt-bills-readable"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn("Run: the character never loaded, so the bills could not be read");
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The character did not finish loading, so the hunt stops.");
            }

            return;
        }

        if (!BossModIPC.Instance.IsAvailable)
        {
            Warn("Run: the combat plugin is not answering; not starting");
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The combat plugin is not answering, so no mark can be fought. Check the Plugins page.");
            return;
        }

        await HoldCombatMovementAndSettle("run");
        while (session.HuntPassesCompleted < MaxHuntPasses)
        {
            var pass = session.HuntPassesCompleted + 1;
            if (!await EnsureStanding())
            {
                return;
            }

            var killsBefore = session.MarksKilled;
            var pickedUp = await PickUpPostedBills();
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            ReportUnhuntable();
            var route = PlanRoute(pass);
            if (route.Count == 0)
            {
                break;
            }

            if (!await HuntRoute(route, pass))
            {
                return;
            }

            session.HuntPassesCompleted++;
            if (pickedUp == 0 && session.MarksKilled == killsBefore)
            {
                Diag($"Run: pass {pass} picked up no bill and credited no kill; not planning another");
                break;
            }
        }

        Finish();
    }

    private async Task<int> PickUpPostedBills()
    {
        var posted = PostedBills();
        if (posted.Count == 0)
        {
            return 0;
        }

        ReportNoMark();
        ReportPhase(HuntPhase.PickingUp);
        var pickedUp = await PickUpBills(posted);
        if (!CancelToken.IsCancellationRequested)
        {
            NoteBillsLeftOnBoard(posted);
        }

        return pickedUp;
    }

    private List<HuntBill> PostedBills()
    {
        MarkBillReader.Refresh(force: true);
        var posted = new List<HuntBill>(bills.Count);
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var bill = bills[billIndex];
            if (MarkBillReader.Status(bill.MarkIndex) == BillStatus.Available && !billsLeftOnBoard.Contains(bill.MarkIndex))
            {
                posted.Add(bill);
            }
        }

        return posted;
    }

    private void NoteBillsLeftOnBoard(List<HuntBill> posted)
    {
        MarkBillReader.Refresh(force: true);
        for (var billIndex = 0; billIndex < posted.Count; billIndex++)
        {
            var bill = posted[billIndex];
            if (MarkBillReader.Status(bill.MarkIndex) != BillStatus.Available)
            {
                continue;
            }

            billsLeftOnBoard.Add(bill.MarkIndex);
            Diag($"Run: {bill.Name} is still not picked up; later passes leave it on the board");
        }
    }

    private List<HuntStop> PlanRoute(int pass)
    {
        Status = "Planning the route";
        var planned = RoutePlanner.Plan(bills);
        var givenUp = session.GivenUpNameIds;
        var route = new List<HuntStop>(planned.Length);
        for (var stopIndex = 0; stopIndex < planned.Length; stopIndex++)
        {
            if (!givenUp.Contains(planned[stopIndex].Target.NameId))
            {
                route.Add(planned[stopIndex]);
            }
        }

        Diag($"Run: pass {pass} plans {route.Count} mark(s), leaving out {planned.Length - route.Count} given up earlier");
        for (var stopIndex = 0; stopIndex < route.Count; stopIndex++)
        {
            var stop = route[stopIndex];
            Diag($"Route {stopIndex + 1}/{route.Count}: {stop.Target.Name} for {stop.Bill.Name}, {stop.Target.Killed}/{stop.Target.Needed} in {stop.Target.ZoneName} (territory {stop.TerritoryId}, FATE {stop.FateId})");
        }

        ReportRoute(route);
        return route;
    }

    private async Task<bool> HuntRoute(List<HuntStop> route, int pass)
    {
        missedThisPass.Clear();
        fateWaits.Clear();
        deferredFates.Clear();
        var settings = FateWaitSettings.Read();
        var visit = 0;
        while (TakeNextStop(route, out var stop))
        {
            visit++;
            ReportUpcoming(stop, route);
            if (SkipStop(stop))
            {
                continue;
            }

            ReportPhase(HuntPhase.Upkeep);
            if (await RunUpkeep())
            {
                Diag("Run: upkeep ran; the next mark starts from wherever it left the character");
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var fateWait = PlanFateVisit(stop, route, settings);
            Diag($"Run: pass {pass}, stop {visit}: {stop.Target.Name} for {stop.Bill.Name}{DescribeVisit(fateWait)}; {route.Count} mark(s) ahead, {deferredFates.Count} FATE mark(s) to look in on");
            ReportMark(stop);
            ReportPhase(HuntPhase.Travelling);
            var killsBefore = session.MarksKilled;
            var outcome = await HuntMark(stop.Bill, stop.Target, fateWait);
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            session.Sample();
            CountKills(session.MarksKilled - killsBefore);
            ReportNoMark();
            if (!await EnsureStanding())
            {
                return false;
            }

            if (outcome == MarkOutcome.FateNotUp && fateWait is not null)
            {
                DeferFate(stop, fateWait, settings);
                continue;
            }

            if (!Settle(stop, outcome))
            {
                return false;
            }
        }

        ReportRouteDone();
        return true;
    }

    // A FATE that is up where the character stands comes first, since it may be gone by the time the route comes back.
    // While other marks are left, a FATE mark waiting on its FATE is looked in on once it is due, when the route leaves
    // the zone anyway or the FATE is in this zone. Once only FATE marks are left, the run waits in each zone in turn,
    // the one looked at longest ago first.
    private bool TakeNextStop(List<HuntStop> ahead, out HuntStop stop)
    {
        var territoryId = Svc.ClientState.TerritoryType;
        if (TakeFateUpHere(deferredFates, territoryId, out stop) || TakeFateUpHere(ahead, territoryId, out stop))
        {
            return true;
        }

        if (HasSearchStop(ahead))
        {
            var leavingZone = ahead[0].TerritoryId != territoryId;
            return TakeDueFate(territoryId, leavingZone, out stop) || TakeAt(ahead, 0, out stop);
        }

        return ahead.Count > 0 ? TakeAt(ahead, 0, out stop) : TakeLongestUnseenFate(out stop);
    }

    private static bool TakeFateUpHere(List<HuntStop> stops, uint territoryId, out HuntStop stop)
    {
        for (var stopIndex = 0; stopIndex < stops.Count; stopIndex++)
        {
            var candidate = stops[stopIndex];
            if (candidate.IsFateBound && candidate.TerritoryId == territoryId && IsMarkFateUp(candidate.FateId))
            {
                return TakeAt(stops, stopIndex, out stop);
            }
        }

        stop = default;
        return false;
    }

    private bool TakeDueFate(uint territoryId, bool leavingZone, out HuntStop stop)
    {
        var now = Environment.TickCount64;
        var pick = -1;
        var earliest = long.MaxValue;
        for (var stopIndex = 0; stopIndex < deferredFates.Count; stopIndex++)
        {
            var candidate = deferredFates[stopIndex];
            if (!leavingZone && candidate.TerritoryId != territoryId)
            {
                continue;
            }

            var lookAt = fateWaits[candidate.FateId].NextLookAt;
            if (lookAt > now || lookAt >= earliest)
            {
                continue;
            }

            earliest = lookAt;
            pick = stopIndex;
        }

        return TakePicked(pick, out stop);
    }

    private bool TakeLongestUnseenFate(out HuntStop stop)
    {
        var pick = -1;
        var earliest = long.MaxValue;
        for (var stopIndex = 0; stopIndex < deferredFates.Count; stopIndex++)
        {
            var lookAt = fateWaits[deferredFates[stopIndex].FateId].NextLookAt;
            if (lookAt >= earliest)
            {
                continue;
            }

            earliest = lookAt;
            pick = stopIndex;
        }

        return TakePicked(pick, out stop);
    }

    private bool TakePicked(int pick, out HuntStop stop)
    {
        if (pick < 0)
        {
            stop = default;
            return false;
        }

        return TakeAt(deferredFates, pick, out stop);
    }

    private static bool TakeAt(List<HuntStop> stops, int stopIndex, out HuntStop stop)
    {
        stop = stops[stopIndex];
        stops.RemoveAt(stopIndex);
        return true;
    }

    private static bool HasSearchStop(List<HuntStop> stops)
    {
        for (var stopIndex = 0; stopIndex < stops.Count; stopIndex++)
        {
            if (!stops[stopIndex].IsFateBound)
            {
                return true;
            }
        }

        return false;
    }

    // With other marks still ahead a visit only looks in; with none left it waits a while before moving to the next FATE.
    private MarkFateWait? PlanFateVisit(in HuntStop stop, List<HuntStop> ahead, in FateWaitSettings settings)
    {
        if (!stop.IsFateBound)
        {
            return null;
        }

        if (!fateWaits.TryGetValue(stop.FateId, out var wait))
        {
            wait = new MarkFateWait(settings.BudgetMs, settings.HuntsOthers);
            fateWaits.Add(stop.FateId, wait);
        }

        if (!wait.Rotates)
        {
            wait.PlanWaitOut();
        }
        else if (HasSearchStop(ahead))
        {
            wait.PlanLookIn();
        }
        else
        {
            wait.PlanDwell(settings.VisitMs);
        }

        return wait;
    }

    private void DeferFate(in HuntStop stop, MarkFateWait wait, in FateWaitSettings settings)
    {
        wait.NextLookAt = Environment.TickCount64 + settings.RecheckMs;
        deferredFates.Add(stop);
        Diag($"Run: the FATE for {stop.Target.Name} is not up; {wait.RemainingMs / TimeUnits.MillisecondsPerSecond}s of its wait left, looking in again in {settings.RecheckMs / TimeUnits.MillisecondsPerMinute} min at the earliest");
    }

    private static string DescribeVisit(MarkFateWait? wait)
    {
        if (wait is null)
        {
            return string.Empty;
        }

        var kind = wait.Dwells ? "waiting" : "looking in";
        return $", {kind} up to {Math.Min(wait.VisitMs, wait.RemainingMs) / TimeUnits.MillisecondsPerSecond}s of {wait.RemainingMs / TimeUnits.MillisecondsPerSecond}s left for its FATE";
    }

    private bool SkipStop(in HuntStop stop)
    {
        if (session.GivenUpNameIds.Contains(stop.Target.NameId))
        {
            Diag($"Run: skipping {stop.Target.Name} for {stop.Bill.Name}; it was given up earlier in this run");
            return true;
        }

        if (!missedThisPass.Contains((stop.Target.NameId, stop.TerritoryId)))
        {
            return false;
        }

        Diag($"Run: skipping {stop.Target.Name} for {stop.Bill.Name}; it was just missed for another bill, and the next pass looks again");
        return true;
    }

    private bool Settle(in HuntStop stop, MarkOutcome outcome)
    {
        var name = stop.Target.Name;
        switch (outcome)
        {
            case MarkOutcome.Killed:
                Diag($"Run: {name} is done on {stop.Bill.Name}");
                return true;
            case MarkOutcome.Cancelled:
                return false;
            case MarkOutcome.CombatUnavailable:
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The combat plugin stopped answering, so the hunt stops.");
                return false;
            case MarkOutcome.Died:
                GiveUp(stop, outcome);
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Knocked out too often hunting {name}; skipping it for the rest of this run.");
                return true;
            case MarkOutcome.KillsNotCounted:
                GiveUp(stop, outcome);
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Kills on {name} did not count on {stop.Bill.Name}; skipping it for the rest of this run.");
                return true;
            case MarkOutcome.Unsupported:
                GiveUp(stop, outcome);
                return true;
            case MarkOutcome.NotFound or MarkOutcome.Unreachable or MarkOutcome.FateMissed:
                missedThisPass.Add((stop.Target.NameId, stop.TerritoryId));
                Diag($"Run: {name} ended {outcome}; moving on, and the next pass may try it again");
                return true;
            default:
                Diag($"Run: {name} ended {outcome}; moving on");
                return true;
        }
    }

    // Keyed by the mark itself, so a mark that beat the character on one bill is not fought again for another.
    private void GiveUp(in HuntStop stop, MarkOutcome outcome)
    {
        session.GivenUpNameIds.Add(stop.Target.NameId);
        Diag($"Run: giving up on {stop.Target.Name} for the rest of this run ({outcome})");
    }

    private void CountKills(int kills)
    {
        for (var kill = 0; kill < kills; kill++)
        {
            NoteMarkKilled();
        }
    }

    private void Finish()
    {
        var left = CountLeftovers();
        Diag($"Run: finished with {left.Marks} mark(s) and {left.Kills} kill(s) left, {left.PickUps} bill(s) still posted, {left.NoBoard} with no board, {left.Unhuntable} mark(s) without spawn data, {left.Unreadable} bill(s) unreadable");
        if (left.NothingToHunt)
        {
            session.CompletedByStopCondition = true;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} {CompletionMessage(left)}");
            return;
        }

        Svc.Chat.Print(left.Unreadable > 0
            ? $"{AhgConstants.LogPrefix} Hunt ended: the bills could not be read. The log has the details."
            : $"{AhgConstants.LogPrefix} Hunt ended with {left.Kills} kill(s) left on {left.Marks} mark(s){(left.PickUps > 0 ? $" and {left.PickUps} bill(s) still on the board" : string.Empty)}. The log has the details.");
    }

    // Marks with no spawn data and bills no board offers are beyond the run, so they do not hold back the after-run action.
    private Leftovers CountLeftovers()
    {
        var huntable = RoutePlanner.Huntable(bills);
        var kills = 0;
        for (var stopIndex = 0; stopIndex < huntable.Length; stopIndex++)
        {
            kills += huntable[stopIndex].Target.Remaining;
        }

        var pickUps = 0;
        var noBoard = 0;
        var unreadable = 0;
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var markIndex = bills[billIndex].MarkIndex;
            switch (MarkBillReader.Status(markIndex))
            {
                case BillStatus.Locked:
                    unreadable++;
                    break;
                case BillStatus.Available when HuntBoards.TryChoose(markIndex, out _):
                    pickUps++;
                    break;
                case BillStatus.Available:
                    noBoard++;
                    break;
            }
        }

        return new Leftovers(huntable.Length, kills, pickUps, noBoard, RoutePlanner.Unsupported(bills).Length, unreadable);
    }

    private static string CompletionMessage(in Leftovers left)
    {
        if (left.Unhuntable == 0 && left.NoBoard == 0)
        {
            return "Hunt complete: every bill you picked is done.";
        }

        var parts = new List<string>(2);
        if (left.Unhuntable > 0)
        {
            parts.Add($"{left.Unhuntable} mark(s) with no known spawn points");
        }

        if (left.NoBoard > 0)
        {
            parts.Add($"{left.NoBoard} bill(s) no hunt board offers you");
        }

        return $"Hunt complete as far as it can go. Left for you: {string.Join(", ", parts)}.";
    }

    private void ReportUnhuntable()
    {
        if (session.UnhuntableReported)
        {
            return;
        }

        var stops = RoutePlanner.Unsupported(bills);
        if (stops.Length == 0)
        {
            return;
        }

        session.UnhuntableReported = true;
        var names = new List<string>(stops.Length);
        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var target = stops[stopIndex].Target;
            Diag($"Run: {target.Name} on {stops[stopIndex].Bill.Name} has no spawn points and no FATE; the run skips it");
            if (!names.Contains(target.Name))
            {
                names.Add(target.Name);
            }
        }

        Svc.Chat.Print($"{AhgConstants.LogPrefix} No spawn points are known for {string.Join(", ", names)}, so the run leaves {(names.Count == 1 ? "it" : "them")} to you.");
    }

    // A Stop or Pause cancels the task before its last lines run, and those lines must not overwrite what the controller set.
    private void ReportPhase(HuntPhase phase)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            progress.SetPhase(phase);
        }
    }

    private void ReportMark(in HuntStop stop)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            progress.SetMark(stop.Bill, stop.Target);
        }
    }

    private void ReportNoMark()
    {
        if (!CancelToken.IsCancellationRequested)
        {
            progress.ClearMark();
        }
    }

    private void ReportRoute(List<HuntStop> route)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            progress.SetRoute(route);
        }
    }

    // The window lists the mark being hunted, the route ahead, then the FATE marks waiting on their FATEs.
    private void ReportUpcoming(in HuntStop current, List<HuntStop> ahead)
    {
        upcoming.Clear();
        upcoming.Add(current);
        upcoming.AddRange(ahead);
        upcoming.AddRange(deferredFates);
        ReportRoute(upcoming);
    }

    private void ReportRouteDone()
    {
        if (!CancelToken.IsCancellationRequested)
        {
            progress.ClearRoute();
        }
    }

    private static bool BillsReadable() => Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer is not null;
}
