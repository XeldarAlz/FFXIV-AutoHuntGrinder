using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.Hunts;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.HuntingLog;

internal static class HuntingLogRegistry
{
    public const byte NoLog = 0xFF;
    public const int SlotCount = 12;
    public const int MaxRanks = 5;
    public const int EntriesPerRank = 10;
    public const int TargetsPerEntry = 4;

    private const int ZonesPerTarget = 3;
    private const uint ClassRowBaseFactor = 10_000;
    private const uint GrandCompanyRowBaseFactor = 1_000_000;
    private const uint FirstGrandCompanyId = 1;
    private const uint LastGrandCompanyId = 3;
    private const uint OpenWorldIntendedUse = 1;
    private const int SourceKeySlotFactor = 1_000;
    private const int SourceKeyRankFactor = 100;
    private const int SourceKeyEntryFactor = 10;
    private const int SourceKeyDigit = 10;

    private static Tables? tables;

    // Lowest spawn level among each rank's targets in the position dataset; no game sheet carries a level per rank.
    private static ReadOnlySpan<byte> ClassRankLevelFloors => [1, 10, 17, 30, 31];

    private static ReadOnlySpan<byte> GrandCompanyRankLevelFloors => [18, 32, 47];

    private static Tables Data => tables ??= new TableBuilder().Build();

    public static ReadOnlySpan<HuntingLogBook> Books => Data.Books;

    public static int TargetCount => Data.Targets.Length;

    public static bool TryGetBook(byte slot, out HuntingLogBook book)
    {
        var data = Data;
        var bookIndex = slot < SlotCount ? data.BookIndexBySlot[slot] : NoLog;
        if (bookIndex == NoLog)
        {
            book = default;
            return false;
        }

        book = data.Books[bookIndex];
        return true;
    }

    public static ReadOnlySpan<HuntingLogEntry> Rank(byte slot, byte rank)
    {
        if (slot >= SlotCount || rank >= MaxRanks)
        {
            return [];
        }

        var data = Data;
        var rankIndex = slot * MaxRanks + rank;
        return new ReadOnlySpan<HuntingLogEntry>(data.Entries, data.RankFirstEntry[rankIndex], data.RankEntryCount[rankIndex]);
    }

    public static ReadOnlySpan<HuntingLogTarget> Targets(in HuntingLogEntry entry)
        => new(Data.Targets, entry.FirstTarget, entry.TargetCount);

    public static ReadOnlySpan<uint> Zones(in HuntingLogTarget target)
        => new(Data.Zones, target.FirstZone, target.ZoneCount);

    public static string BookName(byte slot)
    {
        var data = Data;
        var bookIndex = slot < SlotCount ? data.BookIndexBySlot[slot] : NoLog;
        return bookIndex == NoLog ? string.Empty : data.BookNames[bookIndex];
    }

    // targetIndex counts through the whole target table: an entry's targets sit at FirstTarget onward.
    public static string TargetName(int targetIndex)
    {
        var names = Data.TargetNames;
        return targetIndex >= 0 && targetIndex < names.Length ? names[targetIndex] : string.Empty;
    }

    public static string SubAreaName(in HuntingLogTarget target, int zoneIndex = 0)
    {
        if (zoneIndex < 0 || zoneIndex >= target.ZoneCount)
        {
            return string.Empty;
        }

        return Data.LocationNames[target.FirstZone + zoneIndex];
    }

    public static byte SlotForClassJob(uint classJobId)
    {
        var slots = Data.ClassJobSlots;
        return classJobId < slots.Length ? slots[classJobId] : NoLog;
    }

    public static byte SlotForGrandCompany(byte grandCompanyId)
    {
        var books = Data.Books;
        for (var index = 0; index < books.Length; index++)
        {
            if (books[index].Kind == HuntingLogKind.GrandCompany && books[index].OwnerRowId == grandCompanyId)
            {
                return books[index].Slot;
            }
        }

        return NoLog;
    }

    // 0 when the rank is unknown, so no level warning applies.
    public static byte LevelFloor(byte slot, byte rank)
    {
        if (!TryGetBook(slot, out var book) || rank >= book.RankCount)
        {
            return 0;
        }

        var floors = book.Kind == HuntingLogKind.Class ? ClassRankLevelFloors : GrandCompanyRankLevelFloors;
        return rank < floors.Length ? floors[rank] : (byte)0;
    }

    public static ushort SourceKey(in HuntingLogEntry entry, in HuntingLogTarget target)
        => (ushort)(entry.Slot * SourceKeySlotFactor + entry.Rank * SourceKeyRankFactor + entry.EntryIndex * SourceKeyEntryFactor + target.TargetSlot);

    public static bool TryParseSourceKey(ushort sourceKey, out byte slot, out byte rank, out byte entryIndex, out byte targetSlot)
    {
        slot = (byte)Math.Min(sourceKey / SourceKeySlotFactor, byte.MaxValue);
        rank = (byte)(sourceKey / SourceKeyRankFactor % SourceKeyDigit);
        entryIndex = (byte)(sourceKey / SourceKeyEntryFactor % SourceKeyDigit);
        targetSlot = (byte)(sourceKey % SourceKeyDigit);
        return slot < SlotCount && rank < MaxRanks && entryIndex < EntriesPerRank && targetSlot < TargetsPerEntry;
    }

    public static bool TryFindTarget(ushort sourceKey, out HuntingLogTarget target, out int targetIndex)
    {
        if (TryParseSourceKey(sourceKey, out var slot, out var rank, out var entryIndex, out var targetSlot))
        {
            return TryFindTarget(slot, rank, entryIndex, targetSlot, out target, out targetIndex);
        }

        target = default;
        targetIndex = -1;
        return false;
    }

    public static bool TryFindTarget(byte slot, byte rank, byte entryIndex, byte targetSlot, out HuntingLogTarget target, out int targetIndex)
    {
        var entries = Rank(slot, rank);
        for (var index = 0; index < entries.Length; index++)
        {
            if (entries[index].EntryIndex != entryIndex)
            {
                continue;
            }

            var entry = entries[index];
            var list = Targets(entry);
            for (var targetOffset = 0; targetOffset < list.Length; targetOffset++)
            {
                if (list[targetOffset].TargetSlot != targetSlot)
                {
                    continue;
                }

                target = list[targetOffset];
                targetIndex = entry.FirstTarget + targetOffset;
                return true;
            }

            break;
        }

        target = default;
        targetIndex = -1;
        return false;
    }

    private readonly record struct BookOwner(HuntingLogKind Kind, byte RowId, string Name);

    private sealed class Tables
    {
        public required HuntingLogBook[] Books { get; init; }
        public required string[] BookNames { get; init; }
        public required byte[] BookIndexBySlot { get; init; }
        public required HuntingLogEntry[] Entries { get; init; }
        public required ushort[] RankFirstEntry { get; init; }
        public required byte[] RankEntryCount { get; init; }
        public required HuntingLogTarget[] Targets { get; init; }
        public required string[] TargetNames { get; init; }
        public required uint[] Zones { get; init; }
        public required string[] LocationNames { get; init; }
        public required byte[] ClassJobSlots { get; init; }
    }

    private sealed class TableBuilder
    {
        private readonly ExcelSheet<MonsterNote> notes = Svc.Data.GetExcelSheet<MonsterNote>();
        private readonly Dictionary<uint, uint> openWorldTerritoryByPlace = [];
        private readonly HashSet<uint> dutyPlaces = [];
        private readonly Dictionary<uint, string> placeNames = [];
        private readonly Dictionary<uint, string> npcNames = [];
        private readonly List<HuntingLogBook> books = new(SlotCount);
        private readonly List<string> bookNames = new(SlotCount);
        private readonly byte[] bookIndexBySlot = new byte[SlotCount];
        private readonly List<HuntingLogEntry> entries = [];
        private readonly ushort[] rankFirstEntry = new ushort[SlotCount * MaxRanks];
        private readonly byte[] rankEntryCount = new byte[SlotCount * MaxRanks];
        private readonly List<HuntingLogTarget> targets = [];
        private readonly List<string> targetNames = [];
        private readonly List<uint> zones = [];
        private readonly List<string> locationNames = [];
        private int unresolvedZones;

        public Tables Build()
        {
            IndexTerritories();
            Array.Fill(bookIndexBySlot, NoLog);
            var owners = CollectOwners();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (owners[slot] is { } owner)
                {
                    AddBook((byte)slot, owner);
                }
            }

            Svc.Log.Info($"{AhgConstants.LogPrefix} Hunting Log registry: {books.Count} logs, {entries.Count} entries, {targets.Count} targets, {zones.Count} zones ({unresolvedZones} listed zones matched no open-world territory)");
            return new Tables
            {
                Books = [.. books],
                BookNames = [.. bookNames],
                BookIndexBySlot = bookIndexBySlot,
                Entries = [.. entries],
                RankFirstEntry = rankFirstEntry,
                RankEntryCount = rankEntryCount,
                Targets = [.. targets],
                TargetNames = [.. targetNames],
                Zones = [.. zones],
                LocationNames = [.. locationNames],
                ClassJobSlots = BuildClassJobSlots(),
            };
        }

        // A dungeon target still names the overworld zone around the entrance; the dungeon itself is its sub-area.
        private void IndexTerritories()
        {
            var sheet = Svc.Data.GetExcelSheet<TerritoryType>();
            for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
            {
                var territory = sheet.GetRowAt(rowIndex);
                var placeId = territory.PlaceName.RowId;
                if (placeId == 0)
                {
                    continue;
                }

                if (territory.ContentFinderCondition.RowId != 0)
                {
                    dutyPlaces.Add(placeId);
                }

                if (territory.TerritoryIntendedUse.RowId == OpenWorldIntendedUse)
                {
                    openWorldTerritoryByPlace.TryAdd(placeId, territory.RowId);
                }
            }
        }

        // Jobs share their base class's log, so only base classes (their own parent) own a class log.
        private static BookOwner?[] CollectOwners()
        {
            var owners = new BookOwner?[SlotCount];
            var classJobs = Svc.Data.GetExcelSheet<ClassJob>();
            for (var rowIndex = 0; rowIndex < classJobs.Count; rowIndex++)
            {
                var classJob = classJobs.GetRowAt(rowIndex);
                var slot = classJob.MonsterNote.RowId;
                if (classJob.ClassJobParent.RowId != classJob.RowId || slot >= SlotCount || owners[slot] is not null)
                {
                    continue;
                }

                owners[slot] = new BookOwner(HuntingLogKind.Class, (byte)classJob.RowId, GameText.Title(classJob.Name.ExtractText()));
            }

            var companies = Svc.Data.GetExcelSheet<GrandCompany>();
            for (var companyId = FirstGrandCompanyId; companyId <= LastGrandCompanyId; companyId++)
            {
                if (!companies.TryGetRow(companyId, out var company))
                {
                    continue;
                }

                var slot = company.MonsterNote.RowId;
                if (slot >= SlotCount || owners[slot] is not null)
                {
                    continue;
                }

                // Company names are proper nouns the game already cases ("Order of the Twin Adder"), which title casing would break.
                owners[slot] = new BookOwner(HuntingLogKind.GrandCompany, (byte)companyId, company.Name.ExtractText());
            }

            return owners;
        }

        private void AddBook(byte slot, BookOwner owner)
        {
            var rowBase = owner.RowId * (owner.Kind == HuntingLogKind.Class ? ClassRowBaseFactor : GrandCompanyRowBaseFactor);
            byte rankCount = 0;
            for (var rank = 0; rank < MaxRanks; rank++)
            {
                var rankIndex = slot * MaxRanks + rank;
                rankFirstEntry[rankIndex] = (ushort)entries.Count;
                for (var entryIndex = 0; entryIndex < EntriesPerRank; entryIndex++)
                {
                    var rowId = rowBase + (uint)(rank * EntriesPerRank + entryIndex + 1);
                    if (notes.TryGetRow(rowId, out var note) && AddEntry(slot, (byte)rank, (byte)entryIndex, note))
                    {
                        rankEntryCount[rankIndex]++;
                    }
                }

                if (rankEntryCount[rankIndex] > 0)
                {
                    rankCount = (byte)(rank + 1);
                }
            }

            bookIndexBySlot[slot] = (byte)books.Count;
            books.Add(new HuntingLogBook(slot, owner.Kind, owner.RowId, rowBase, rankCount, HuntAchievements.ForLog(slot)));
            bookNames.Add(owner.Name);
        }

        private bool AddEntry(byte slot, byte rank, byte entryIndex, in MonsterNote note)
        {
            var firstTarget = (ushort)targets.Count;
            byte targetCount = 0;
            var listed = Math.Min(Math.Min(note.MonsterNoteTarget.Count, note.Count.Count), TargetsPerEntry);
            for (var targetSlot = 0; targetSlot < listed; targetSlot++)
            {
                var reference = note.MonsterNoteTarget[targetSlot];
                if (reference.RowId == 0 || reference.ValueNullable is not { } target)
                {
                    continue;
                }

                AddTarget(target, note.Count[targetSlot], (byte)targetSlot);
                targetCount++;
            }

            if (targetCount == 0)
            {
                return false;
            }

            entries.Add(new HuntingLogEntry(slot, rank, entryIndex, firstTarget, targetCount));
            return true;
        }

        private void AddTarget(in MonsterNoteTarget target, byte needed, byte targetSlot)
        {
            var firstZone = (ushort)zones.Count;
            byte zoneCount = 0;
            var inDuty = false;
            var listed = Math.Min(Math.Min(target.PlaceNameZone.Count, target.PlaceNameLocation.Count), ZonesPerTarget);
            for (var zoneIndex = 0; zoneIndex < listed; zoneIndex++)
            {
                var zonePlaceId = target.PlaceNameZone[zoneIndex].RowId;
                if (zonePlaceId == 0)
                {
                    continue;
                }

                var locationPlaceId = target.PlaceNameLocation[zoneIndex].RowId;
                inDuty |= dutyPlaces.Contains(locationPlaceId);
                if (!openWorldTerritoryByPlace.TryGetValue(zonePlaceId, out var territoryId))
                {
                    unresolvedZones++;
                    continue;
                }

                zones.Add(territoryId);
                locationNames.Add(PlaceText(locationPlaceId));
                zoneCount++;
            }

            targets.Add(new HuntingLogTarget(target.BNpcName.RowId, needed, targetSlot, firstZone, zoneCount, inDuty));
            targetNames.Add(NpcName(target.BNpcName));
        }

        private string PlaceText(uint placeId)
        {
            if (placeId == 0)
            {
                return string.Empty;
            }

            if (placeNames.TryGetValue(placeId, out var cached))
            {
                return cached;
            }

            var name = Svc.Data.GetExcelSheet<PlaceName>().GetRowOrDefault(placeId)?.Name.ExtractText() ?? string.Empty;
            placeNames[placeId] = name;
            return name;
        }

        private string NpcName(RowRef<BNpcName> reference)
        {
            if (npcNames.TryGetValue(reference.RowId, out var cached))
            {
                return cached;
            }

            var name = GameText.Title(reference.ValueNullable?.Singular.ExtractText() ?? string.Empty);
            npcNames[reference.RowId] = name;
            return name;
        }

        // A job without its own log falls back to its parent class's, the log its kills advance.
        private static byte[] BuildClassJobSlots()
        {
            var sheet = Svc.Data.GetExcelSheet<ClassJob>();
            uint highestRowId = 0;
            for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
            {
                highestRowId = Math.Max(highestRowId, sheet.GetRowAt(rowIndex).RowId);
            }

            var slots = new byte[highestRowId + 1];
            Array.Fill(slots, NoLog);
            for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
            {
                var classJob = sheet.GetRowAt(rowIndex);
                slots[classJob.RowId] = SlotOf(classJob);
            }

            return slots;
        }

        private static byte SlotOf(in ClassJob classJob)
        {
            var own = classJob.MonsterNote.RowId;
            if (own < SlotCount)
            {
                return (byte)own;
            }

            var parent = classJob.ClassJobParent.ValueNullable;
            return parent is { } parentRow && parentRow.MonsterNote.RowId < SlotCount ? (byte)parentRow.MonsterNote.RowId : NoLog;
        }
    }
}
