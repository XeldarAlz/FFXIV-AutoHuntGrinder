using AutoHuntGrinder;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Tasks;
using ECommons.DalamudServices;

var marks = new[] { new HuntMark(1, HuntMarkRank.A, 100, ExpansionKind.ARR), new HuntMark(2, HuntMarkRank.S, 101, ExpansionKind.HW) };
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}
void Reset()
{
    AutoRankFarm.Release();
    Svc.Objects.LocalPlayer = new Player();
    Svc.Chat.Errors.Clear();
    var ipc = LifestreamIPC.Instance;
    ipc.Available = true;
    ipc.Busy = false;
    ipc.Accept = true;
    ipc.Aborts = 0;
    ipc.Requests.Clear();
    AutoObjectiveHunt.Attempts.Clear();
    AutoObjectiveHunt.Hunt = _ => MarkOutcome.NotFound;
    AutoObjectiveHunt.Wait = condition => Task.FromResult(condition());
}
AutoRankFarm TaskFor(RankFarmState state, out AutoHuntSession session)
{
    session = new AutoHuntSession(state);
    return new AutoRankFarm(session, new HuntProgress());
}

Check(RankFarmState.Matches(marks[0], true, false, new HashSet<ExpansionKind> { ExpansionKind.ARR }), "A rank selection");
Check(!RankFarmState.Matches(marks[1], true, false, new HashSet<ExpansionKind> { ExpansionKind.HW }), "S ranks require selection");
Check(!RankFarmState.Matches(marks[0], true, true, new HashSet<ExpansionKind> { ExpansionKind.HW }), "unselected expansion excluded");
Check(!RankFarmState.Matches(marks[0] with { Rank = HuntMarkRank.B }, true, true, new HashSet<ExpansionKind> { ExpansionKind.ARR }), "B ranks excluded");

Reset();
var state = new RankFarmState(marks, [10, 20]);
var originalKeys = state.Pending().Select(objective => objective.SourceKey).ToArray();
state.Credit(999, 1);
state.Credit(10, 999);
Check(state.TotalKills == 0, "wrong-world and unrelated kills ignored");
state.CompleteAttempt(1, false);
Check(state.Pending().Single().SourceKey == originalKeys[1], "resume keeps stable objective keys after a miss");
state.AdvanceWorld();
Check(state.Pending().Length == 2 && !state.Pending().Any(objective => originalKeys.Contains(objective.SourceKey)), "new world gets fresh attempts and distinct give-up keys");

Reset();
state = new RankFarmState(marks, [10, 20]);
var run = TaskFor(state, out var session);
AutoObjectiveHunt.Hunt = objective =>
{
    Plugin.Kills.Credit(objective.NameId);
    Plugin.Kills.Credit(objective.NameId);
    return MarkOutcome.Killed;
};
AutoObjectiveHunt.Wait = condition =>
{
    Check(!condition(), "travel cannot complete before destination world");
    Svc.Objects.LocalPlayer!.CurrentWorld = new WorldRow(20);
    Check(!condition(), "travel cannot complete while Lifestream busy");
    LifestreamIPC.Instance.Busy = false;
    return Task.FromResult(condition());
};
await run.Run();
Check(state.Finished && state.TotalKills == 4 && session.CompletedByStopCondition, "one finite circuit credits each mark once on each world");
Check(LifestreamIPC.Instance.Requests.SequenceEqual(new uint[] { 20 }) && LifestreamIPC.Instance.Aborts == 0, "only remaining world visited; successful travel not aborted");

Reset();
state = new RankFarmState(marks, [10]);
run = TaskFor(state, out session);
await run.Run();
Check(state.Finished && state.TotalKills == 0 && AutoObjectiveHunt.Attempts.Count == 2, "absent marks do not repeat or count as kills");
Check(LifestreamIPC.Instance.Requests.Count == 0, "current-world-only mode does not call Lifestream");

Reset();
state = new RankFarmState(marks, [10]);
run = TaskFor(state, out session);
AutoObjectiveHunt.Hunt = objective => objective.NameId == 1 ? MarkOutcome.Unreachable : MarkOutcome.NotFound;
await run.Run();
Check(state.Finished && state.FailedTargets == 1 && !session.CompletedByStopCondition, "unreachable marks do not permit after-run actions");

Reset();
state = new RankFarmState(marks, [10]);
run = TaskFor(state, out session);
AutoObjectiveHunt.Hunt = objective =>
{
    if (objective.NameId == 2) run.Cancellation.Cancel();
    return MarkOutcome.NotFound;
};
await run.Run();
Check(!state.Finished && state.Pending().Single().NameId == 2, "pause retains completed attempts and leaves interrupted mark pending");
Plugin.Kills.Credit(2);
Check(state.TotalKills == 0, "paused run no longer receives kill credit");
AutoObjectiveHunt.Hunt = _ => MarkOutcome.NotFound;
await TaskFor(state, out _).Run();
Check(AutoObjectiveHunt.Attempts.Count(attempt => attempt.Name == 1) == 1 && state.Finished, "resume does not repeat completed attempts");

foreach (var failure in new[] { "busy", "missing", "declined", "timeout", "cancel" })
{
    Reset();
    state = new RankFarmState(marks, [20]);
    run = TaskFor(state, out session);
    var ipc = LifestreamIPC.Instance;
    ipc.Busy = failure == "busy";
    ipc.Available = failure != "missing";
    ipc.Accept = failure != "declined";
    AutoObjectiveHunt.Wait = _ =>
    {
        if (failure == "cancel") { run.Cancellation.Cancel(); AutoRankFarm.Release(); }
        return Task.FromResult(false);
    };
    await run.Run();
    Check(!state.Finished && !session.CompletedByStopCondition && AutoObjectiveHunt.Attempts.Count == 0, $"{failure}: no hunting or completion on failed travel");
    Check(ipc.Aborts == (failure is "timeout" or "cancel" ? 1 : 0), $"{failure}: only travel owned by the run is aborted");
}

Reset();
state = new RankFarmState(marks, [10]);
run = TaskFor(state, out session);
AutoObjectiveHunt.Hunt = _ =>
{
    Svc.Objects.LocalPlayer!.CurrentWorld = new WorldRow(999);
    return MarkOutcome.NotFound;
};
await run.Run();
Check(!state.Finished && state.Pending().Length == 2 && Svc.Chat.Errors.Count == 1, "unexpected world change stops the sweep without recording attempts there");

Reset();
var oldTravelWait = new TaskCompletionSource<bool>();
AutoObjectiveHunt.Wait = _ => oldTravelWait.Task;
var oldRun = TaskFor(new RankFarmState(marks, [20]), out _);
var oldWork = oldRun.Run();
oldRun.Cancellation.Cancel();
AutoRankFarm.Release();
var newTravelWait = new TaskCompletionSource<bool>();
AutoObjectiveHunt.Wait = _ => newTravelWait.Task;
var newState = new RankFarmState(marks, [30]);
var newRun = TaskFor(newState, out _);
var newWork = newRun.Run();
oldTravelWait.SetResult(false);
await oldWork;
Check(LifestreamIPC.Instance.Aborts == 1 && LifestreamIPC.Instance.Busy, "old task unwinding does not abort the new task's travel");
Svc.Objects.LocalPlayer!.CurrentWorld = new WorldRow(30);
LifestreamIPC.Instance.Busy = false;
newTravelWait.SetResult(true);
await newWork;
Check(newState.Finished, "new task completes after old task cleanup");

Console.WriteLine("All rank farm checks passed.");
