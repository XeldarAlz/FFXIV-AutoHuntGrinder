using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// Each pass takes the posted bills and hunts the planned route, and the next pass plans again: a finished old bill frees
// the fresh one on the board, and a mark that stayed hidden gets another look. A pass that picks up nothing and credits no
// kill ends the run, so a mark that cannot be found does not keep it going.
internal sealed class AutoHunt(IReadOnlyList<HuntBill> bills, AutoHuntSession session, HuntProgress progress) : AutoCommon
{
    private const int MaxHuntPasses = 5;
    private const int BillsReadableWaitMs = 30_000;

    private readonly IReadOnlyList<HuntBill> bills = bills;
    private readonly AutoHuntSession session = session;
    private readonly HuntProgress progress = progress;
    private readonly HashSet<uint> givenUpNameIds = [];

    private bool unhuntableReported;

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
            }

            return;
        }

        if (!BossModIPC.Instance.IsAvailable)
        {
            Warn("Run: the combat plugin is not answering; not starting");
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The combat plugin is not answering, so no mark can be fought. Check the Plugins page.");
            return;
        }

        for (var pass = 1; pass <= MaxHuntPasses; pass++)
        {
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
        MarkBillReader.Refresh(force: true);
        if (!AnyBillPosted())
        {
            return 0;
        }

        ReportNoMark();
        ReportPhase(HuntPhase.PickingUp);
        return await PickUpBills(bills);
    }

    private List<HuntStop> PlanRoute(int pass)
    {
        Status = "Planning the route";
        var planned = RoutePlanner.Plan(bills);
        var route = new List<HuntStop>(planned.Length);
        for (var stopIndex = 0; stopIndex < planned.Length; stopIndex++)
        {
            if (!givenUpNameIds.Contains(planned[stopIndex].Target.NameId))
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

        return route;
    }

    // False when the run has to stop: a Stop or Pause, or the combat plugin gone quiet.
    private async Task<bool> HuntRoute(List<HuntStop> route, int pass)
    {
        for (var stopIndex = 0; stopIndex < route.Count; stopIndex++)
        {
            var stop = route[stopIndex];
            ReportPhase(HuntPhase.Upkeep);
            if (await RunUpkeep())
            {
                Diag("Run: upkeep ran; the next mark starts from wherever it left the character");
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            Diag($"Run: pass {pass}, mark {stopIndex + 1}/{route.Count}: {stop.Target.Name} for {stop.Bill.Name}");
            ReportMark(stop);
            ReportPhase(HuntPhase.Travelling);
            var killsBefore = session.MarksKilled;
            var outcome = await HuntMark(stop.Bill, stop.Target);
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            session.Sample();
            CountKills(session.MarksKilled - killsBefore);
            ReportNoMark();
            if (Svc.Condition[ConditionFlag.Unconscious])
            {
                await RecoverFromMarkKnockout();
            }

            if (!Settle(stop, outcome))
            {
                return false;
            }
        }

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
            default:
                Diag($"Run: {name} ended {outcome}; moving on, and a later pass may try it again");
                return true;
        }
    }

    // Keyed by the mark itself, so a mark that beat the character on one bill is not fought again for another.
    private void GiveUp(in HuntStop stop, MarkOutcome outcome)
    {
        givenUpNameIds.Add(stop.Target.NameId);
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
        MarkBillReader.Refresh(force: true);
        var marks = 0;
        var kills = 0;
        var pickUps = 0;
        var noBoard = 0;
        var unhuntable = 0;
        var unreadable = 0;
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var markIndex = bills[billIndex].MarkIndex;
            switch (MarkBillReader.Status(markIndex))
            {
                case BillStatus.Locked:
                    unreadable++;
                    break;
                case BillStatus.Available:
                    if (HuntBoards.TryChoose(markIndex, out _))
                    {
                        pickUps++;
                    }
                    else
                    {
                        noBoard++;
                    }

                    break;
                case BillStatus.Held or BillStatus.Stale:
                    var targets = MarkBillReader.Targets(markIndex);
                    for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                    {
                        var target = targets[targetIndex];
                        if (target.Done)
                        {
                            continue;
                        }

                        if (!RoutePlanner.CanHunt(target))
                        {
                            unhuntable++;
                            continue;
                        }

                        marks++;
                        kills += target.Remaining;
                    }

                    break;
            }
        }

        return new Leftovers(marks, kills, pickUps, noBoard, unhuntable, unreadable);
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
        if (unhuntableReported)
        {
            return;
        }

        var stops = RoutePlanner.Unsupported(bills);
        if (stops.Length == 0)
        {
            return;
        }

        unhuntableReported = true;
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

    private bool AnyBillPosted()
    {
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            if (MarkBillReader.Status(bills[billIndex].MarkIndex) == BillStatus.Available)
            {
                return true;
            }
        }

        return false;
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

    private static bool BillsReadable() => Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer is not null;
}
