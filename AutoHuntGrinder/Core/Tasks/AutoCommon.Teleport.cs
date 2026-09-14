using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Travel;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int TeleportCombatClearMs = 30_000;
    private const int GroundingMoveMs = 20_000;
    private const int ZoneLoadSettleMs = 5_000;
    private const int TeleportRetryBackoffMs = 2_000;
    private const int ReturnHomeWaitMs = 30_000;
    private const int ReturnHomeReissueMs = 3_000;
    private const int ReturnHomePollMs = 250;
    // General action 8 is Return, the home-point teleport, which takes a different path than an aetheryte teleport.
    private const uint ReturnGeneralActionId = 8;
    private const int MaxTeleportFaults = 2;
    private const int StallsBeforeReturnHome = 2;
    // A gateway attempt costs more than twice a plain one, so it stops once try, go home, and try from home have run.
    private const int GatewayAttempts = 3;
    private const int AethernetLegMs = 90_000;
    private const int AethernetNavmeshWaitMs = 60_000;
    private const int AethernetNavmeshPollFrames = 60;
    private const float GroundSearchHalfExtentMeters = 30f;
    private const float GroundedEnoughMeters = 2f;
    private const float GroundArrivalToleranceMeters = 3f;

    private static readonly ConditionTagRule[] conditionTagRules =
    [
        new("combat", [ConditionFlag.InCombat]),
        new("cast", [ConditionFlag.Casting, ConditionFlag.Casting87]),
        new("mount", [ConditionFlag.Mounted, ConditionFlag.RidingPillion]),
        new("flight", [ConditionFlag.InFlight]),
        new("dive", [ConditionFlag.Diving]),
        new("swim", [ConditionFlag.Swimming]),
        new("jump", [ConditionFlag.Jumping, ConditionFlag.Jumping61]),
        new("moved", [ConditionFlag.BeingMoved]),
        new("zoning", [ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51]),
        new("occupied", [ConditionFlag.Occupied33, ConditionFlag.Occupied38, ConditionFlag.Occupied39]),
    ];

    internal static string ConditionTag()
    {
        var condition = Svc.Condition;
        var tags = new StringBuilder();
        for (var ruleIndex = 0; ruleIndex < conditionTagRules.Length; ruleIndex++)
        {
            var rule = conditionTagRules[ruleIndex];
            if (!AnyFlagSet(condition, rule.Flags))
            {
                continue;
            }

            if (tags.Length > 0)
            {
                tags.Append(',');
            }

            tags.Append(rule.Tag);
        }

        return tags.Length == 0 ? "grounded" : tags.ToString();
    }

    // Gate for every teleport: stop navigation (being moved blocks the cast), wait out combat, get back onto solid
    // ground, then dismount. From the air or the water the library spins on a cast that never starts.
    internal async Task PrepareForTeleport(string scope)
    {
        NavmeshIPC.Instance.Stop();

        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Casting])
        {
            Status = "Waiting for combat to clear before teleporting";
            Diag($"{scope}: combat or casting ({ConditionTag()}), waiting up to {TeleportCombatClearMs / TimeUnits.MillisecondsPerSecond}s for a castable window");
            await WaitUntilTimed(
                () => !Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting],
                TeleportCombatClearMs,
                $"{scope}-wait-teleportable");
        }

        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        if (NotOnSolidGround())
        {
            await GroundForTeleport(scope);
        }

        if (CancelToken.IsCancellationRequested || !Svc.Condition[ConditionFlag.Mounted])
        {
            return;
        }

        Diag($"{scope}: dismounting before teleport ({ConditionTag()})");
        await SafeDismount($"{scope}-dismount");
    }

    // Clears a stuck "another teleport is already underway" state that silently blocks every aetheryte teleport. Return
    // takes a different path that still fires and lands in a city the next attempt can teleport out of.
    internal async Task<bool> TryReturnHome(string scope)
    {
        var startTerritory = Svc.ClientState.TerritoryType;
        Diag($"{scope}: teleport not starting ({ConditionTag()}); casting Return to the home aetheryte to clear a stuck teleport");
        Status = "Returning home to clear a stuck teleport";

        var deadline = Environment.TickCount64 + ReturnHomeWaitMs;
        var nextCastAt = 0L;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType != startTerritory)
            {
                await DelayMs(ZoneLoadSettleMs);
                return true;
            }

            if (Environment.TickCount64 >= nextCastAt
                && !Svc.Condition[ConditionFlag.Casting]
                && !Svc.Condition[ConditionFlag.BetweenAreas]
                && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                UseGeneralAction(ReturnGeneralActionId);
                nextCastAt = Environment.TickCount64 + ReturnHomeReissueMs;
            }

            await DelayMs(ReturnHomePollMs);
        }

        if (Svc.ClientState.TerritoryType != startTerritory)
        {
            return true;
        }

        Diag($"{scope}: Return home did not complete within {ReturnHomeWaitMs / TimeUnits.MillisecondsPerSecond}s");
        return false;
    }

    // A teleport can be accepted and never start casting; the idle guard catches that in seconds and the attempt is
    // retried after a short backoff. A fault is different: the request was answered and the same request gets the same
    // answer, so it earns one retry and never the Return escalation.
    internal async Task<bool> TeleportToTerritory(uint territoryId, Vector3 destination, string label, int perAttemptTimeoutMs, int attempts = 4)
    {
        if (Svc.ClientState.TerritoryType == territoryId)
        {
            return true;
        }

        var zoneName = TerritoryNames.Of(territoryId);
        var viaGateway = ZoneAetherytes.TryFindGateway(territoryId, out var gateway);
        uint hopTerritoryId;
        Vector3 hopDestination;
        if (viaGateway)
        {
            if (Svc.ClientState.TerritoryType != gateway.TerritoryId && !ZoneAetherytes.IsAttuned(gateway.AetheryteId))
            {
                Warn($"{label}: {gateway.Name}, the way into {zoneName}, is not attuned; giving up");
                return false;
            }

            hopTerritoryId = gateway.TerritoryId;
            hopDestination = gateway.Position;
            Diag($"{label}: {zoneName} owns no aetheryte; going through {gateway.Name} and its aethernet");
        }
        else if (ZoneAetherytes.TryFindNearest(territoryId, destination, out var aetheryte))
        {
            hopTerritoryId = territoryId;
            // Its exact position makes the library pick this aetheryte instead of whichever shard sits nearest the destination.
            hopDestination = aetheryte.Position;
            Diag($"{label}: {aetheryte.Name} is the attuned aetheryte nearest the destination in {zoneName}");
        }
        else
        {
            Warn(ZoneAetherytes.AttunableIdsIn(territoryId).Length == 0
                ? $"{label}: {zoneName} ({territoryId}) has no aetheryte to teleport to; giving up"
                : $"{label}: no aetheryte in {zoneName} ({territoryId}) is attuned; giving up");
            return false;
        }

        var maxAttempts = viaGateway ? Math.Min(attempts, GatewayAttempts) : attempts;
        var returnedHome = false;
        var stalls = 0;
        var faults = 0;
        for (var attempt = 1; attempt <= maxAttempts && !CancelToken.IsCancellationRequested; attempt++)
        {
            if (Svc.ClientState.TerritoryType == territoryId)
            {
                return true;
            }

            var scope = $"{label}#{attempt}";
            await PrepareForTeleport(scope);
            if (CancelToken.IsCancellationRequested)
            {
                break;
            }

            if (Svc.ClientState.TerritoryType != hopTerritoryId)
            {
                var operation = new MoveOp(move => move.Teleport(hopTerritoryId, hopDestination, allowSameZoneTeleport: false));
                var completed = await RunCancellable(operation, perAttemptTimeoutMs, scope, StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));
                if (operation.Fault is { } fault)
                {
                    faults++;
                    Diag($"{scope} teleport faulted ({faults}/{MaxTeleportFaults}): {fault.Message}");
                }
                else if (!completed)
                {
                    stalls++;
                }
            }

            if (viaGateway && Svc.ClientState.TerritoryType == hopTerritoryId)
            {
                await RideAethernetInto(territoryId, destination, gateway, scope);
            }

            if (Svc.ClientState.TerritoryType == territoryId)
            {
                return true;
            }

            if (faults >= MaxTeleportFaults)
            {
                Diag($"{label}: teleport rejected {faults} times, not a stuck cast; giving up without Return");
                break;
            }

            // Two stalled attempts almost always mean another teleport is stuck underway; Return clears it.
            if (!returnedHome && stalls >= StallsBeforeReturnHome)
            {
                returnedHome = true;
                await TryReturnHome(label);
            }
            else
            {
                await DelayMs(TeleportRetryBackoffMs);
            }
        }

        return Svc.ClientState.TerritoryType == territoryId;
    }

    private static bool NotOnSolidGround()
        => Svc.Condition[ConditionFlag.InFlight]
        || Svc.Condition[ConditionFlag.Diving]
        || Svc.Condition[ConditionFlag.Swimming];

    private static bool AnyFlagSet(ICondition condition, ConditionFlag[] flags)
    {
        for (var flagIndex = 0; flagIndex < flags.Length; flagIndex++)
        {
            if (condition[flags[flagIndex]])
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe void UseGeneralAction(uint generalActionId)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager is null)
        {
            return;
        }

        actionManager->UseAction(ActionType.GeneralAction, generalActionId);
    }

    // From the air the floor straight below is the landing; in the water the nearest reachable point is the way out.
    private async Task GroundForTeleport(string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        var position = player.Position;
        var navmesh = NavmeshIPC.Instance;
        var landing = Svc.Condition[ConditionFlag.InFlight]
            ? navmesh.PointOnFloor(position, allowUnlandable: false, GroundSearchHalfExtentMeters)
                ?? navmesh.NearestPointReachable(position, GroundSearchHalfExtentMeters, GroundSearchHalfExtentMeters)
            : navmesh.NearestPointReachable(position, GroundSearchHalfExtentMeters, GroundSearchHalfExtentMeters);
        if (landing is not { } solidGround)
        {
            Warn($"{scope}: off solid ground ({ConditionTag()}) with no reachable mesh point to relocate to; teleport may fail");
            return;
        }

        var distance = Vector3.Distance(position, solidGround);
        if (distance < GroundedEnoughMeters)
        {
            return;
        }

        Status = "Returning to solid ground before teleporting";
        Diag($"{scope}: off solid ground ({ConditionTag()}); relocating ~{distance:F0}m to a reachable point before teleport");
        var groundingScope = $"{scope}-ground";
        var move = new MoveOp(operation => operation.MoveInZone(solidGround, MovementConfig.Everything.WithTolerance(GroundArrivalToleranceMeters), null));
        await RunCancellable(move, GroundingMoveMs, groundingScope, StuckDetector.MoveStallAbort(groundingScope));
    }

    // Second leg of a gateway territory: walk to the hub's aetheryte and ride its aethernet in. It runs without the idle
    // guard, because the walk-up, the menu and the hop all hold the character still in ways it reads as a stuck teleport.
    private async Task RideAethernetInto(uint territoryId, Vector3 destination, ZoneGateway gateway, string scope)
    {
        Status = $"Riding the aethernet from {gateway.Name}";
        await WaitForNavmeshReady(AethernetNavmeshWaitMs, AethernetNavmeshPollFrames);
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        var aim = destination;
        if (ZoneAetherytes.TryFindEntryNode(territoryId, destination, out var entry))
        {
            // Aiming at the entry stop itself makes the library ride to exactly that one.
            aim = entry.Position;
            Diag($"{scope}: reached {gateway.Name}; riding its aethernet to {entry.Name} in territory {territoryId}");
        }
        else
        {
            Diag($"{scope}: reached {gateway.Name}; riding its aethernet into territory {territoryId}");
        }

        var operation = new MoveOp(move => move.Aethernet(territoryId, aim));
        await RunCancellable(operation, AethernetLegMs, $"{scope}-aethernet");
        if (operation.Fault is { } fault)
        {
            Diag($"{scope}: aethernet leg faulted: {fault.Message}");
        }
    }

    private readonly record struct ConditionTagRule(string Tag, ConditionFlag[] Flags);
}
