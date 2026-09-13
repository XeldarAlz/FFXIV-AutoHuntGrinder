using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Numerics;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using CSObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace AutoHuntGrinder.Core.Hunts;

internal readonly record struct MarkSighting(
    ulong GameObjectId,
    int ObjectIndex,
    Vector3 Position,
    float HitboxRadius,
    float DistanceToHitbox,
    uint CurrentHp,
    ushort FateId);

// Walks the object table through its cached wrappers, so a scan allocates nothing and is cheap enough for a move's stop check.
internal static unsafe class MarkFinder
{
    // The id the game stores when a character targets nothing.
    private const ulong NoTargetId = 0xE0000000;

    // fateId 0 matches only mobs outside any FATE; any other value matches only mobs spawned by that FATE.
    public static bool TryFindNearest(uint nameId, uint fateId, bool honorClaims, Vector3 from, ReadOnlySpan<ulong> ignored, out MarkSighting sighting, out int claimedSkipped)
    {
        sighting = default;
        claimedSkipped = 0;
        var found = false;
        var bestDistance = float.MaxValue;
        var objects = Svc.Objects;
        var localPlayerId = objects.LocalPlayer?.GameObjectId ?? 0;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc || npc.NameId != nameId)
            {
                continue;
            }

            if (!IsHuntable(npc, fateId) || ignored.Contains(npc.GameObjectId))
            {
                continue;
            }

            if (honorClaims && ClaimedByOther(npc.TargetObjectId, localPlayerId))
            {
                claimedSkipped++;
                continue;
            }

            var distance = DistanceToHitbox(from, npc);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            sighting = Describe(npc, objectIndex, distance);
            found = true;
        }

        return found;
    }

    public static bool TryGetLive(ulong gameObjectId, Vector3 from, out MarkSighting sighting)
    {
        sighting = default;
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc || npc.GameObjectId != gameObjectId)
            {
                continue;
            }

            if (!IsAlive(npc))
            {
                return false;
            }

            sighting = Describe(npc, objectIndex, DistanceToHitbox(from, npc));
            return true;
        }

        return false;
    }

    public static IGameObject? Resolve(in MarkSighting sighting)
    {
        var gameObject = Svc.Objects[sighting.ObjectIndex];
        return gameObject is not null && gameObject.GameObjectId == sighting.GameObjectId ? gameObject : null;
    }

    private static bool IsHuntable(IBattleNpc npc, uint fateId)
    {
        if (!IsAlive(npc))
        {
            return false;
        }

        var native = (CSGameObject*)npc.Address;
        return native->BattleNpcSubKind == BattleNpcSubKind.Combatant && native->FateId == fateId;
    }

    private static bool IsAlive(IBattleNpc npc) => npc.IsTargetable && !npc.IsDead && npc.CurrentHp > 0;

    // A mob another player's party pulled first gives that party the credit, so fighting it would never count.
    private static bool ClaimedByOther(ulong targetId, ulong localPlayerId)
    {
        if (targetId == 0 || targetId == NoTargetId || targetId == localPlayerId)
        {
            return false;
        }

        var manager = GameObjectManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var target = manager->Objects.GetObjectByGameObjectId(targetId);
        if (target == null || target->ObjectKind != CSObjectKind.Pc)
        {
            return false;
        }

        var groups = GroupManager.Instance();
        return groups == null || !groups->MainGroup.IsEntityIdInParty(target->EntityId);
    }

    private static MarkSighting Describe(IBattleNpc npc, int objectIndex, float distance)
        => new(npc.GameObjectId, objectIndex, npc.Position, npc.HitboxRadius, distance, npc.CurrentHp, ((CSGameObject*)npc.Address)->FateId);

    private static float DistanceToHitbox(Vector3 from, IBattleNpc npc)
        => MathF.Max(0f, Vector3.Distance(from, npc.Position) - npc.HitboxRadius);
}
