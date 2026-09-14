using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace AutoHuntGrinder.Core.Kills;

// The game keeps no counter for an arbitrary mob, so two signals are combined: a tracked mob we tagged that is later
// seen down (death), and a "You defeat" line naming a tracked mob (log). A credit waits up to FoldWindowMs for its
// other half, so one kill seen both ways credits once.
internal sealed unsafe class KillLedger : IDisposable
{
    private const int WatchCapacity = 16;
    private const int InterestTrackCapacity = 16;
    private const int CreditedRingCapacity = 32;
    private const int PendingCapacity = 16;
    // A Hunting Log rank names at most 40 targets (10 entries of up to 4).
    private const int InitialInterestCapacity = 64;
    private const long ScanIntervalMs = 200;
    private const long FoldWindowMs = 2_000;
    // A slot for a mob that left object range is dropped, so it cannot hold a name or a slot forever.
    private const long UnseenExpiryMs = 60_000;
    private const uint DefeatLogMessageId = 557;
    // "<target> is defeated." Its trigger is unconfirmed (another player's kill reads the same), so it is logged for review, never credited.
    private const uint DefeatedLogMessageId = 559;
    private const uint HuntingLogFirstProgressMessageId = 1001;
    private const uint HuntingLogLastProgressMessageId = 1005;
    private const uint HuntingLogRankUnlockedMessageId = 1011;
    private const uint HuntingLogRankUnlockedWithArticleMessageId = 1012;
    // ObjStr ids from here up name event NPCs and objects; below it a battle NPC's id is its BNpcName row.
    private const uint FirstNonBattleNpcObjStrId = 1_000_000;
    private const byte UntaggedType = 0;
    // A party tag may carry the party id in place of a character's id.
    private const byte PartyTagType = 2;
    // The id the game stores when an object has no owner.
    private const uint NoOwnerId = 0xE0000000;

    private readonly TrackedMob[] watchSlots = new TrackedMob[WatchCapacity];
    private readonly TrackedMob[] interestSlots = new TrackedMob[InterestTrackCapacity];
    private readonly ulong[] creditedIds = new ulong[CreditedRingCapacity];
    private readonly PendingCredit[] pending = new PendingCredit[PendingCapacity];

    private uint[] interestNames = new uint[InitialInterestCapacity];
    private int interestCount;
    private int creditedCursor;
    private int pendingCount;
    private long nextScanAtMs;
    private ulong localPlayerId;
    private uint localPlayerEntityId;
    private bool huntingLogChangePending;

    public KillLedger()
    {
        Svc.Chat.LogMessage += OnLogMessage;
        Svc.Framework.Update += OnUpdate;
    }

    public event Action<uint>? Credited;

    public event Action? HuntingLogChanged;

    private enum TagOwner : byte
    {
        Nobody,
        Us,
        OurCompanion,
        Others,
    }

    [Flags]
    private enum KillSignal : byte
    {
        None = 0,
        Death = 1,
        Log = 2,
        Both = Death | Log,
    }

    private readonly record struct TrackedMob(ulong GameObjectId, long TrackedAtMs, long SeenAtMs, uint NameId, bool TaggedByUs)
    {
        public bool InUse => GameObjectId != 0;
    }

    private readonly record struct PendingCredit(ulong GameObjectId, long FirstSignalAtMs, uint NameId, KillSignal Signals);

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.Chat.LogMessage -= OnLogMessage;
    }

    public void Watch(ulong gameObjectId, uint nameId)
    {
        if (gameObjectId == 0 || nameId == 0)
        {
            return;
        }

        var taggedByUs = false;
        var interestIndex = FindSlot(interestSlots, gameObjectId);
        if (interestIndex >= 0)
        {
            taggedByUs = interestSlots[interestIndex].NameId == nameId && interestSlots[interestIndex].TaggedByUs;
            interestSlots[interestIndex] = default;
        }

        var watchIndex = FindSlot(watchSlots, gameObjectId);
        if (watchIndex >= 0)
        {
            taggedByUs |= watchSlots[watchIndex].NameId == nameId && watchSlots[watchIndex].TaggedByUs;
        }
        else
        {
            watchIndex = ClaimSlot(watchSlots);
        }

        var now = Environment.TickCount64;
        watchSlots[watchIndex] = new TrackedMob(gameObjectId, now, now, nameId, taggedByUs);
    }

    public void SetInterest(ReadOnlySpan<uint> nameIds)
    {
        if (interestNames.Length < nameIds.Length)
        {
            interestNames = new uint[nameIds.Length];
        }

        nameIds.CopyTo(interestNames);
        interestCount = nameIds.Length;
        Array.Sort(interestNames, 0, interestCount);
        for (var index = 0; index < interestSlots.Length; index++)
        {
            if (interestSlots[index].InUse && !IsInterest(interestSlots[index].NameId))
            {
                interestSlots[index] = default;
            }
        }
    }

    public void ClearInterest()
    {
        interestCount = 0;
        Array.Clear(interestSlots);
    }

    private void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        EmitSettledCredits(now);
        if (huntingLogChangePending)
        {
            huntingLogChangePending = false;
            HuntingLogChanged?.Invoke();
        }

        if (now < nextScanAtMs)
        {
            return;
        }

        nextScanAtMs = now + ScanIntervalMs;
        Scan(now);
    }

    // Runs inside the game's log hook: only the ids are copied out, and the Hunting Log refresh is raised on the next
    // framework update, after the handler that printed the line has also written the new counts.
    private void OnLogMessage(ILogMessage message)
    {
        var logMessageId = message.LogMessageId;
        if (IsHuntingLogMessage(logMessageId))
        {
            huntingLogChangePending = true;
            return;
        }

        if (logMessageId != DefeatLogMessageId && logMessageId != DefeatedLogMessageId)
        {
            return;
        }

        if (message.TargetEntity is not { IsPlayer: false } target)
        {
            return;
        }

        var nameId = target.ObjStrId;
        if (nameId == 0 || nameId >= FirstNonBattleNpcObjStrId || !IsTrackedName(nameId))
        {
            return;
        }

        if (logMessageId == DefeatedLogMessageId)
        {
            Diag($"Kill ledger: log message {DefeatedLogMessageId} named BNpcName {nameId}; not credited");
            return;
        }

        RecordLog(nameId, Environment.TickCount64);
    }

    private void Scan(long now)
    {
        if (interestCount == 0 && !AnyInUse(watchSlots))
        {
            return;
        }

        var objects = Svc.Objects;
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        localPlayerId = player.GameObjectId;
        localPlayerEntityId = player.EntityId;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc)
            {
                continue;
            }

            var gameObjectId = npc.GameObjectId;
            var nameId = npc.NameId;
            if (TryObserve(watchSlots, npc, gameObjectId, nameId, now))
            {
                continue;
            }

            if (!IsInterest(nameId) || TryObserve(interestSlots, npc, gameObjectId, nameId, now))
            {
                continue;
            }

            TrackIfTaggedByUs(npc, gameObjectId, nameId, now);
        }

        ExpireUnseen(watchSlots, now);
        ExpireUnseen(interestSlots, now);
    }

    // The tag is recorded while the mob lives because it may clear once the mob dies; a tag still readable on the
    // body counts too, which catches a mob that went from untouched to dead between two scans.
    private bool TryObserve(TrackedMob[] slots, IBattleNpc npc, ulong gameObjectId, uint nameId, long now)
    {
        var index = FindSlot(slots, gameObjectId);
        if (index < 0)
        {
            return false;
        }

        var slot = slots[index];
        if (slot.NameId != nameId)
        {
            Diag($"Kill ledger: object {gameObjectId:X} is BNpcName {nameId}, tracked as {slot.NameId}; dropping it");
            slots[index] = default;
            return false;
        }

        var owner = ReadTag(npc, out var tagType, out var taggerId);
        if (IsDown(npc))
        {
            slots[index] = default;
            if (IsOurs(owner) || (owner == TagOwner.Nobody && slot.TaggedByUs))
            {
                RecordDeath(gameObjectId, nameId, now);
            }
            else
            {
                Diag($"Kill ledger: BNpcName {nameId}, object {gameObjectId:X}, went down without our tag (tag type {tagType}, tagger {taggerId:X}); no death credit");
            }

            return true;
        }

        var taggedByUs = owner switch
        {
            TagOwner.Us or TagOwner.OurCompanion => true,
            TagOwner.Others => false,
            _ => slot.TaggedByUs,
        };
        if (taggedByUs != slot.TaggedByUs)
        {
            Diag($"Kill ledger: BNpcName {nameId}, object {gameObjectId:X}, {DescribeTag(owner)} (tag type {tagType}, tagger {taggerId:X})");
        }

        slots[index] = slot with { SeenAtMs = now, TaggedByUs = taggedByUs };
        return true;
    }

    private void TrackIfTaggedByUs(IBattleNpc npc, ulong gameObjectId, uint nameId, long now)
    {
        if (IsDown(npc))
        {
            return;
        }

        var owner = ReadTag(npc, out var tagType, out var taggerId);
        if (!IsOurs(owner))
        {
            return;
        }

        interestSlots[ClaimSlot(interestSlots)] = new TrackedMob(gameObjectId, now, now, nameId, true);
        Diag($"Kill ledger: tracking BNpcName {nameId}, object {gameObjectId:X}, {DescribeTag(owner)} (tag type {tagType}, tagger {taggerId:X})");
    }

    private TagOwner ReadTag(IBattleNpc npc, out byte tagType, out ulong taggerId)
    {
        var character = (CSCharacter*)npc.Address;
        tagType = character->CombatTagType;
        taggerId = character->CombatTaggerId;
        if (tagType == UntaggedType)
        {
            return TagOwner.Nobody;
        }

        if (IsOurTagger(tagType, character->CombatTaggerId))
        {
            return TagOwner.Us;
        }

        return IsOurCompanion(character->CombatTaggerId) ? TagOwner.OurCompanion : TagOwner.Others;
    }

    private bool IsOurTagger(byte tagType, GameObjectId taggerId)
    {
        if (taggerId.Id == localPlayerId)
        {
            return true;
        }

        var groups = GroupManager.Instance();
        if (groups != null)
        {
            if (groups->MainGroup.IsEntityIdInParty(taggerId.ObjectId))
            {
                return true;
            }

            var partyId = (ulong)groups->MainGroup.PartyId;
            if (tagType == PartyTagType && partyId != 0 && taggerId.Id == partyId)
            {
                return true;
            }
        }

        return InfoProxyCrossRealm.IsCrossRealmParty() && InfoProxyCrossRealm.GetMemberByEntityId(taggerId.ObjectId) != null;
    }

    // A pet or chocobo that lands the first hit may tag the mob with its own id, and the kill still counts for its owner.
    private bool IsOurCompanion(GameObjectId taggerId)
    {
        var manager = GameObjectManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var tagger = manager->Objects.GetObjectByGameObjectId(taggerId);
        if (tagger == null || tagger->ObjectKind is not (CSObjectKind.BattleNpc or CSObjectKind.Companion))
        {
            return false;
        }

        var ownerId = tagger->OwnerId;
        if (ownerId == 0 || ownerId == NoOwnerId)
        {
            return false;
        }

        if (ownerId == localPlayerEntityId)
        {
            return true;
        }

        var groups = GroupManager.Instance();
        return groups != null && groups->MainGroup.IsEntityIdInParty(ownerId);
    }

    private void RecordDeath(ulong gameObjectId, uint nameId, long now)
    {
        if (WasCredited(gameObjectId))
        {
            return;
        }

        RememberCredited(gameObjectId);
        for (var index = 0; index < pendingCount; index++)
        {
            var credit = pending[index];
            if (credit.NameId == nameId && credit.Signals == KillSignal.Log)
            {
                pending[index] = credit with { GameObjectId = gameObjectId, Signals = KillSignal.Both };
                return;
            }
        }

        AddPending(new PendingCredit(gameObjectId, now, nameId, KillSignal.Death));
    }

    private void RecordLog(uint nameId, long now)
    {
        for (var index = 0; index < pendingCount; index++)
        {
            var credit = pending[index];
            if (credit.NameId == nameId && credit.Signals == KillSignal.Death)
            {
                pending[index] = credit with { Signals = KillSignal.Both };
                return;
            }
        }

        AddPending(new PendingCredit(0, now, nameId, KillSignal.Log));
    }

    private void AddPending(PendingCredit credit)
    {
        if (pendingCount == pending.Length)
        {
            var oldest = pending[0];
            RemovePendingAt(0);
            Emit(oldest);
        }

        pending[pendingCount] = credit;
        pendingCount++;
    }

    private void RemovePendingAt(int index)
    {
        pendingCount--;
        if (index < pendingCount)
        {
            Array.Copy(pending, index + 1, pending, index, pendingCount - index);
        }

        pending[pendingCount] = default;
    }

    private void EmitSettledCredits(long now)
    {
        var index = 0;
        while (index < pendingCount)
        {
            var credit = pending[index];
            if (credit.Signals != KillSignal.Both && now - credit.FirstSignalAtMs < FoldWindowMs)
            {
                index++;
                continue;
            }

            RemovePendingAt(index);
            Emit(credit);
        }
    }

    private void Emit(PendingCredit credit)
    {
        Diag(credit.GameObjectId == 0
            ? $"Kill ledger: credited BNpcName {credit.NameId} (log)"
            : $"Kill ledger: credited BNpcName {credit.NameId}, object {credit.GameObjectId:X} ({(credit.Signals == KillSignal.Both ? "both" : "death")})");
        Credited?.Invoke(credit.NameId);
    }

    private bool WasCredited(ulong gameObjectId)
    {
        for (var index = 0; index < creditedIds.Length; index++)
        {
            if (creditedIds[index] == gameObjectId)
            {
                return true;
            }
        }

        return false;
    }

    private void RememberCredited(ulong gameObjectId)
    {
        creditedIds[creditedCursor] = gameObjectId;
        creditedCursor = (creditedCursor + 1) % creditedIds.Length;
    }

    private bool IsInterest(uint nameId)
        => interestCount > 0 && Array.BinarySearch(interestNames, 0, interestCount, nameId) >= 0;

    private bool IsTrackedName(uint nameId)
    {
        if (IsInterest(nameId) || HoldsName(watchSlots, nameId))
        {
            return true;
        }

        for (var index = 0; index < pendingCount; index++)
        {
            if (pending[index].NameId == nameId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOurs(TagOwner owner) => owner is TagOwner.Us or TagOwner.OurCompanion;

    private static string DescribeTag(TagOwner owner) => owner switch
    {
        TagOwner.Us => "tagged by us",
        TagOwner.OurCompanion => "tagged by our pet or companion",
        TagOwner.Others => "tagged by someone else",
        _ => "untagged",
    };

    private static bool IsHuntingLogMessage(uint logMessageId)
        => logMessageId is >= HuntingLogFirstProgressMessageId and <= HuntingLogLastProgressMessageId
            or HuntingLogRankUnlockedMessageId
            or HuntingLogRankUnlockedWithArticleMessageId;

    private static bool IsDown(IBattleNpc npc) => npc.IsDead || npc.CurrentHp == 0;

    private static int FindSlot(TrackedMob[] slots, ulong gameObjectId)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index].InUse && slots[index].GameObjectId == gameObjectId)
            {
                return index;
            }
        }

        return -1;
    }

    private static int ClaimSlot(TrackedMob[] slots)
    {
        var oldest = 0;
        for (var index = 0; index < slots.Length; index++)
        {
            if (!slots[index].InUse)
            {
                return index;
            }

            if (slots[index].TrackedAtMs < slots[oldest].TrackedAtMs)
            {
                oldest = index;
            }
        }

        return oldest;
    }

    private static bool AnyInUse(TrackedMob[] slots)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index].InUse)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HoldsName(TrackedMob[] slots, uint nameId)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index].InUse && slots[index].NameId == nameId)
            {
                return true;
            }
        }

        return false;
    }

    private static void ExpireUnseen(TrackedMob[] slots, long now)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index].InUse && now - slots[index].SeenAtMs >= UnseenExpiryMs)
            {
                slots[index] = default;
            }
        }
    }

    private static void Diag(string message) => Svc.Log.Info($"{AhgConstants.LogPrefix} {message}");
}
