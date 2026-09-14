using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Spawns;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace AutoHuntGrinder.Core.Custom;

internal static class CustomMobCatalog
{
    private const int NotListed = -1;
    private const int StackQueryLimit = 128;

    private static readonly Dictionary<uint, string> unlistedNames = new();
    private static CatalogArrays? arrays;

    public static ReadOnlySpan<uint> NameIds => Loaded.NameIds;

    public static ReadOnlySpan<string> Names => Loaded.Names;

    private static CatalogArrays Loaded => arrays ??= Load();

    public static int Search(ReadOnlySpan<char> query, Span<int> results)
    {
        var trimmed = query.Trim();
        if (trimmed.IsEmpty || results.IsEmpty)
        {
            return 0;
        }

        Span<char> needle = trimmed.Length <= StackQueryLimit ? stackalloc char[trimmed.Length] : new char[trimmed.Length];
        trimmed.ToLowerInvariant(needle);
        var keys = Loaded.SearchKeys;
        var found = 0;
        for (var index = 0; index < keys.Length && found < results.Length; index++)
        {
            if (keys[index].AsSpan().StartsWith(needle, StringComparison.Ordinal))
            {
                results[found++] = index;
            }
        }

        for (var index = 0; index < keys.Length && found < results.Length; index++)
        {
            if (keys[index].AsSpan().IndexOf(needle, StringComparison.Ordinal) > 0)
            {
                results[found++] = index;
            }
        }

        return found;
    }

    public static string NameOf(uint nameId)
    {
        var tableIndex = MobSpawns.IndexOf(nameId);
        if (tableIndex != MobSpawns.NotFound)
        {
            var catalogIndex = Loaded.CatalogIndexByTableIndex[tableIndex];
            if (catalogIndex != NotListed)
            {
                return Loaded.Names[catalogIndex];
            }
        }

        return UnlistedName(nameId);
    }

    private static string UnlistedName(uint nameId)
    {
        if (unlistedNames.TryGetValue(nameId, out var cached))
        {
            return cached;
        }

        var name = ReadName(Svc.Data.GetExcelSheet<BNpcName>(), nameId);
        var resolved = name.Length == 0 ? $"#{nameId}" : name;
        unlistedNames[nameId] = resolved;
        return resolved;
    }

    private static string ReadName(ExcelSheet<BNpcName> sheet, uint nameId)
        => GameText.Title(sheet.GetRowOrDefault(nameId)?.Singular.ExtractText() ?? string.Empty);

    private static CatalogArrays Load()
    {
        var tableNameIds = MobSpawns.NameIds;
        var sheet = Svc.Data.GetExcelSheet<BNpcName>();
        var tableIndices = new List<int>(tableNameIds.Length);
        var names = new List<string>(tableNameIds.Length);
        for (var tableIndex = 0; tableIndex < tableNameIds.Length; tableIndex++)
        {
            var name = ReadName(sheet, tableNameIds[tableIndex]);
            if (name.Length == 0)
            {
                continue;
            }

            tableIndices.Add(tableIndex);
            names.Add(name);
        }

        var order = new int[names.Count];
        for (var index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        var compareInfo = CultureInfo.InvariantCulture.CompareInfo;
        Array.Sort(order, (left, right) =>
        {
            var byName = compareInfo.Compare(names[left], names[right], CompareOptions.IgnoreCase);
            return byName != 0 ? byName : tableIndices[left].CompareTo(tableIndices[right]);
        });

        var nameIds = new uint[order.Length];
        var sortedNames = new string[order.Length];
        var searchKeys = new string[order.Length];
        var catalogIndexByTableIndex = new int[tableNameIds.Length];
        Array.Fill(catalogIndexByTableIndex, NotListed);
        for (var index = 0; index < order.Length; index++)
        {
            var source = order[index];
            var tableIndex = tableIndices[source];
            nameIds[index] = tableNameIds[tableIndex];
            sortedNames[index] = names[source];
            searchKeys[index] = names[source].ToLowerInvariant();
            catalogIndexByTableIndex[tableIndex] = index;
        }

        return new CatalogArrays(nameIds, sortedNames, searchKeys, catalogIndexByTableIndex);
    }

    private sealed record CatalogArrays(uint[] NameIds, string[] Names, string[] SearchKeys, int[] CatalogIndexByTableIndex);
}
