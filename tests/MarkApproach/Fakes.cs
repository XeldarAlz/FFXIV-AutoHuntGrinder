global using Environment = MarkApproach.Tests.Clock;
using System.Numerics;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace MarkApproach.Tests
{
    internal static class Clock { public static long TickCount64 = 1_000; }
}

namespace AutoHuntGrinder
{
    internal sealed class Plugin
    {
        public static readonly Plugin Instance = new();
        public static readonly Ledger Kills = new();
        public readonly Config Configuration = new();
    }
    internal sealed class Ledger { public void Watch(ulong id, uint name) { } }
    internal sealed class Config { public int BundledCombatPresetRevision; public void Save() { } }
}

namespace AutoHuntGrinder.Core
{
    internal static class TimeUnits { public const int MillisecondsPerSecond = 1_000; }
}

namespace AutoHuntGrinder.Core.Game.Player
{
    internal static class EnmityList { public static bool Contains(uint id) => false; }
}

namespace Dalamud.Game.ClientState.Conditions
{
    internal enum ConditionFlag { Mounted, InCombat }
}

namespace ECommons.DalamudServices
{
    internal static class Svc
    {
        public static readonly Objects Objects = new();
        public static readonly Targets Targets = new();
        public static readonly Conditions Condition = new();
    }
    internal sealed class Objects { public Player? LocalPlayer = new(); }
    internal sealed class Player { public Vector3 Position = Vector3.Zero; public readonly JobRow ClassJob = new(); }
    internal sealed class JobRow { public Job? ValueNullable = new(); }
    internal sealed class Job { public byte Role = 3; }
    internal sealed class Targets { public GameObject? Target; }
    internal sealed class GameObject
    {
        public ulong GameObjectId = 1;
        public uint EntityId = 1;
        public bool IsDead => false;
        public string Name => "Quarry";
    }
    internal sealed class Conditions
    {
        public bool Mounted;
        public bool this[ConditionFlag flag] => flag == ConditionFlag.Mounted && Mounted;
    }
}

namespace AutoHuntGrinder.Core.Hunts
{
    internal readonly record struct MarkSighting(ulong GameObjectId, Vector3 Position, float HitboxRadius, float DistanceToHitbox, uint CurrentHp, ushort FateId);
    internal static class MarkFinder
    {
        public static bool Visible = true;
        public static bool Alive = true;
        public static MarkSighting Mark = new(1, new(10, 0, 0), 1, 9, 100, 0);
        public static bool HasLineOfSight(in MarkSighting mark) => Visible;
        public static bool TryGetLive(ulong id, Vector3 position, out MarkSighting mark) { mark = Mark; return Alive; }
        public static GameObject? Resolve(in MarkSighting mark) => Alive ? new() : null;
    }
}

namespace AutoHuntGrinder.Core.Travel
{
    internal static class GroundDistance { public static float Between(Vector3 a, Vector3 b) => Vector3.Distance(a, b); }
}

namespace AutoHuntGrinder.Core.Ipc
{
    internal sealed class NavmeshIPC
    {
        public static readonly NavmeshIPC Instance = new();
        public Vector3? NearestStandablePoint(Vector3 position, float x, float y) => position;
        public Vector3? PointOnFloor(Vector3 position, bool allowUnlandable, float extent) => position;
    }
    internal static class HuntCombatPreset
    {
        public const string Name = "Hunt";
        public const int Revision = 1;
        public static string GetSerialized() => "";
    }
    internal sealed class BossModIPC
    {
        public static readonly BossModIPC Instance = new();
        public bool Parked;
        public bool CanClearTransientStrategy => true;
        public bool AddTransientStrategy(string a, string b, string c, string d) { Parked = true; return true; }
        public bool ClearTransientStrategy(string a, string b, string c) { Parked = false; return true; }
        public void ClearActive() { }
        public string? GetPreset(string name) => name;
        public bool CreatePreset(string preset, bool overwrite) => true;
        public string GetActive() => HuntCombatPreset.Name;
        public bool SetActive(string name) => true;
    }
}

namespace AutoHuntGrinder.Core.Tasks
{
    internal enum HuntPhase { Fighting }
    internal sealed class MoveOp(float tolerance, Func<bool>? stop)
    {
        public float Tolerance = tolerance;
        public Func<bool>? Stop = stop;
        public Exception? Fault => null;
    }
    internal static class StuckDetector
    {
        public const int FalseStartMs = 500;
        public static Func<bool> MoveStallAbort(string scope) => () => false;
    }

    // Execute the production approach, engagement and stop checks; replace only game/IPC, navigation and time.
    public abstract partial class AutoCommon
    {
        private const float MinLegToleranceMeters = 1.5f;
        private const int MaxFalseStarts = 2;
        internal CancellationTokenSource Cancellation = new();
        private CancellationToken CancelToken => Cancellation.Token;
        internal string Status = "Searching for Quarry";
        private HuntPhase MarkPhase { get; set; }
        internal bool MovementHeld;
        internal int Moves;
        internal bool Done;
        internal bool Dead;
        internal Action<MoveOp> OnMove = _ => { };
        internal Action OnFrame = () => { };
        internal bool Ride;
        internal bool RouteReachable = true;
        internal float PlannedReach;
        internal Task<string> Approach() => AsText(CloseOnMark(new(), 1, "test"));
        internal Task<string> Engage() => AsText(EngageMark(new(), 1, 0, "test"));
        internal Task Reposition() => RepositionToMark(new(), 1, "test");
        internal Func<bool> StopCheck(bool requireSight = true) => StopWhenMarkWithin(1, 20, new(10, 0, 0), "Approaching", requireSight);
        private static async Task<string> AsText(Task<MarkFight> task) => (await task).ToString();
        private Task<LegPlan> PlanLeg(uint zone, Vector3 goal, float reach, float mount, bool allowed, bool failed, string scope)
        {
            PlannedReach = reach;
            return Task.FromResult(new LegPlan(Ride ? LegMode.Flight : LegMode.OnFoot, goal, RouteReachable));
        }
        private static MoveOp LegOperation(LegMode mode, Vector3 target, float tolerance, Func<bool>? stop) => new(tolerance, stop);
        private Task<bool> RunCancellable(MoveOp operation, int timeout, string scope, Func<bool> abort)
        {
            Moves++;
            OnMove(operation);
            Environment.TickCount64 += 1_000;
            return Task.FromResult(true);
        }
        private Task NextFrame(int frames) { Environment.TickCount64 += frames * 100; OnFrame(); return Task.CompletedTask; }
        private static Task<bool> RecoverIfOffMesh(uint zone, Vector3 goal, string scope) => Task.FromResult(false);
        private static bool TerritoryAllowsMount(uint zone) => true;
        private static Task<bool> LandAndDismount(Vector3 goal, float gap, string scope) { Svc.Condition.Mounted = false; return Task.FromResult(true); }
        private static Task SafeDismount(string scope) { Svc.Condition.Mounted = false; return Task.CompletedTask; }
        private static Task RecoverFromFalseStart(string scope, int attempt) => Task.CompletedTask;
        private static LegMode StepMode() => LegMode.OnFoot;
        private bool IsMarkKnockedOut() => Dead;
        private static string FormatPosition(Vector3 position) => position.ToString();
        private static string ConditionTag() => "";
        private static string DescribeProgress(MarkHuntContext hunt) => "";
        private static void Diag(string message) { }
        private static void Warn(string message) { }
        private void HoldCombatMovement(string scope) => MovementHeld = true;
        private void ReleaseCombatMovement(string scope) => MovementHeld = false;
        private static void SyncToMarkFate(MarkHuntContext hunt) { }
        private MarkProgress ReadMarkProgress(MarkHuntContext hunt, bool force) => new(Done, true, true, Done ? 1 : 0, 1);
        private static Task<bool> WaitUntilTimed(Func<bool> condition, int timeout, string scope) => Task.FromResult(condition());
        private static MarkOutcome? CheckMarkState(MarkHuntContext hunt) => null;
        private static MarkOutcome? CheckMarkStanding(MarkHuntContext hunt) => null;
        private static bool SightMark(MarkHuntContext hunt, out MarkSighting mark) { mark = MarkFinder.Mark; return MarkFinder.Alive; }
        private enum LegMode { OnFoot, Flight }
        private readonly record struct LegPlan(LegMode Mode, Vector3 Target, bool Reachable)
        {
            public bool Rides => Mode == LegMode.Flight;
            public float ShortfallMeters => 0;
        }
        private readonly record struct MarkProgress(bool Done, bool Tracked, bool Listed, int Killed, int Needed);
        private sealed class MarkHuntContext
        {
            public string Name => "Quarry";
            public string SourceName => "Bill";
            public string ApproachLabel => "Approaching Quarry";
            public string FightLabel => "Fighting Quarry";
            public uint NameId => 1;
            public uint TerritoryId => 1;
            public uint FateId => 0;
            public bool IsHuntMark => false;
            public bool RespawnsWithinSearch => true;
            public bool WatchesKills => false;
            public int FightBudgetMs => 180_000;
            public int UncountedKills;
            public void Ignore(ulong id) { }
        }
    }
    internal sealed class TestHunt : AutoCommon;
}
