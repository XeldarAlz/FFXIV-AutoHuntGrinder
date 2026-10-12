using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Tasks;

namespace AutoHuntGrinder
{
    internal static class Plugin { public static readonly Ledger Kills = new(); }
    internal sealed class Ledger
    {
        public event Action<uint>? Credited;
        public void Credit(uint name) => Credited?.Invoke(name);
        public void SetInterest(uint[] names) { }
        public void ClearTracking() { }
    }
}

namespace AutoHuntGrinder.Core
{
    internal static class AhgConstants { public const string LogPrefix = "[test]"; }
    internal static class TimeUnits { public const int MillisecondsPerMinute = 60_000; }
}

namespace AutoHuntGrinder.Core.Marks
{
    internal static class RankFarmSelection { public static string WorldName(uint world) => world.ToString(); }
}

namespace AutoHuntGrinder.Core.Hunts
{
    internal static class ObjectivePlanner { public static HuntObjective[] Plan(HuntObjective[] objectives) => objectives; }
}

namespace Dalamud.Game.ClientState.Conditions
{
    internal enum ConditionFlag { BetweenAreas, BetweenAreas51 }
}

namespace ECommons.DalamudServices
{
    internal static class Svc
    {
        public static readonly ObjectTable Objects = new();
        public static readonly Chat Chat = new();
        public static readonly Conditions Condition = new();
    }
    internal sealed class ObjectTable { public Player? LocalPlayer { get; set; } = new(); }
    internal sealed class Player { public WorldRow CurrentWorld { get; set; } = new(10); }
    internal readonly record struct WorldRow(uint RowId);
    internal sealed class Chat
    {
        public readonly List<string> Errors = [];
        public void Print(string text) { }
        public void PrintError(string text) => Errors.Add(text);
    }
    internal sealed class Conditions { public bool this[Dalamud.Game.ClientState.Conditions.ConditionFlag flag] => false; }
}

namespace AutoHuntGrinder.Core.Ipc
{
    internal sealed class LifestreamIPC
    {
        public static readonly LifestreamIPC Instance = new();
        public bool Available = true;
        public bool Busy;
        public bool Accept = true;
        public int Aborts;
        public readonly List<uint> Requests = [];
        public bool IsBusy() => Busy;
        public bool ChangeWorld(uint world)
        {
            Requests.Add(world);
            if (Accept) Busy = true;
            return Accept;
        }
        public void Abort() { Aborts++; Busy = false; }
    }
    internal sealed class BossModIPC
    {
        public static readonly BossModIPC Instance = new();
        public void ClearActive() { }
    }
    internal sealed class NavmeshIPC
    {
        public static readonly NavmeshIPC Instance = new();
        public void Stop() { }
    }
}

namespace AutoHuntGrinder.Core.Tasks
{
    internal enum HuntPhase { Travelling }
    internal sealed class HuntProgress { public void ClearObjectives() { } }
    internal sealed class AutoHuntSession(Marks.RankFarmState farm)
    {
        public Marks.RankFarmState RankFarm = farm;
        public bool CompletedByStopCondition;
        public void RecordFault(Exception exception, CancellationToken token) { }
    }

    // Only the host/game boundary is replaced. Tests execute the real AutoRankFarm loop and state.
    internal abstract class AutoObjectiveHunt(AutoHuntSession session, HuntProgress progress)
    {
        protected AutoHuntSession RunSession = session;
        protected HuntProgress RunProgress = progress;
        public readonly CancellationTokenSource Cancellation = new();
        protected CancellationToken CancelToken => Cancellation.Token;
        protected string Status = "";
        public static Func<HuntObjective, MarkOutcome> Hunt = _ => MarkOutcome.NotFound;
        public static Func<Func<bool>, Task<bool>> Wait = condition => Task.FromResult(condition());
        public static readonly List<(uint World, uint Name)> Attempts = [];
        public Task Run() => Execute();
        protected abstract Task Execute();
        protected Task<bool> PrepareToHunt(string label) => Task.FromResult(true);
        protected Task<bool> EnsureStanding() => Task.FromResult(true);
        protected Task FightOffAttackers(string scope) => Task.CompletedTask;
        protected Task<bool> WaitUntilTimed(Func<bool> condition, int timeout, string scope) => Wait(condition);
        protected void ReportPhase(HuntPhase phase) { }
        protected void LogPlan(HuntObjective[] objectives, int pass) { }
        protected void Diag(string message) { }
        protected void ReleaseCombatMovement(string scope) { }
        private protected virtual bool CanContinueObjectives() => true;
        private protected virtual void OnObjectiveFinished(in HuntObjective objective, MarkOutcome outcome) { }
        protected Task<bool> HuntObjectives(HuntObjective[] objectives, int pass)
        {
            foreach (var objective in objectives)
            {
                if (CancelToken.IsCancellationRequested || !CanContinueObjectives()) return Task.FromResult(false);
                Attempts.Add((ECommons.DalamudServices.Svc.Objects.LocalPlayer!.CurrentWorld.RowId, objective.NameId));
                var outcome = Hunt(objective);
                if (CancelToken.IsCancellationRequested || !CanContinueObjectives()) return Task.FromResult(false);
                OnObjectiveFinished(objective, outcome);
                if (outcome is MarkOutcome.Cancelled or MarkOutcome.CombatUnavailable) return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }
}
