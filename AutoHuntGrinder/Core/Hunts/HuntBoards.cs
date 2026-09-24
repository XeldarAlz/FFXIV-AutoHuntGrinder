using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Numerics;
using GrandCompany = ECommons.ExcelServices.GrandCompany;
using PlayerHelpers = ECommons.GameHelpers.Player;

namespace AutoHuntGrinder.Core.Hunts;

internal readonly record struct HuntBoard(uint ObjectId, uint TerritoryId, ExpansionKind Expansion, GrandCompany GrandCompany, uint ShardId);

internal static class HuntBoards
{
    public const int NoBoard = -1;
    // The bill window's accept button answers with callback value 0.
    public const int AcceptCallbackValue = 0;

    private const string MenuSheetName = "custom/002/ComDefMobHuntBoard_00202";
    private const int MenuTextColumn = 1;
    // Level.Type 45 marks a row that places an event object; its Object column then holds the EObj id.
    private const byte EventObjectLevelType = 45;
    // The ARR board lists a regular and an elite tier, later boards three regular tiers and an elite, and every menu ends with "Nothing.".
    private const int RealmRebornMenuEntries = 3;
    private const int ClanMenuEntries = 5;

    // Only a board far from its city's aetheryte names the aethernet shard beside it.
    private static readonly HuntBoard[] boards =
    [
        new(2004438, 128, ExpansionKind.ARR, GrandCompany.Maelstrom, 0),
        new(2004439, 132, ExpansionKind.ARR, GrandCompany.TwinAdder, 0),
        new(2004440, 130, ExpansionKind.ARR, GrandCompany.ImmortalFlames, 0),
        new(2005909, 418, ExpansionKind.HW, GrandCompany.Unemployed, 80),
        new(2008655, 628, ExpansionKind.SB, GrandCompany.Unemployed, 0),
        new(2008654, 635, ExpansionKind.SB, GrandCompany.Unemployed, 0),
        new(2010340, 819, ExpansionKind.ShB, GrandCompany.Unemployed, 0),
        new(2010341, 820, ExpansionKind.ShB, GrandCompany.Unemployed, 0),
        new(2012236, 962, ExpansionKind.EW, GrandCompany.Unemployed, 189),
        new(2012237, 963, ExpansionKind.EW, GrandCompany.Unemployed, 0),
        new(2014155, 1185, ExpansionKind.DT, GrandCompany.Unemployed, 221),
    ];

    private static readonly string[] windowAddons = ["Mobhunt", "Mobhunt2", "Mobhunt3", "Mobhunt4", "Mobhunt5", "Mobhunt6"];

    private static readonly Vector3[] positions = new Vector3[boards.Length];
    private static readonly bool[] positionFound = new bool[boards.Length];

    private static bool positionsResolved;
    private static string[]? menuTexts;

    public static int Count => boards.Length;

    private static ReadOnlySpan<byte> ExpansionByMark => [0, 1, 1, 1, 0, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];

    private static ReadOnlySpan<byte> MenuRowByMark => [1, 5, 6, 7, 2, 8, 12, 13, 14, 15, 18, 19, 20, 21, 24, 25, 26, 27, 30, 31, 32, 33];

    private static ReadOnlySpan<byte> MenuIndexByMark => [0, 0, 1, 2, 1, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3];

    public static HuntBoard Get(int boardIndex) => boards[boardIndex];

    public static string WindowAddon(int boardIndex) => windowAddons[(int)boards[boardIndex].Expansion];

    public static int MenuEntryCount(int boardIndex)
        => boards[boardIndex].Expansion == ExpansionKind.ARR ? RealmRebornMenuEntries : ClanMenuEntries;

    public static int MenuIndex(byte markIndex) => MenuIndexByMark[markIndex];

    public static string MenuText(byte markIndex)
    {
        menuTexts ??= LoadMenuTexts();
        return markIndex < menuTexts.Length ? menuTexts[markIndex] : string.Empty;
    }

    // The ARR boards stand in the grand company headquarters, and a player takes ARR bills only from their own company's board.
    public static bool Serves(int boardIndex, byte markIndex)
    {
        if (markIndex >= ExpansionByMark.Length)
        {
            return false;
        }

        var board = boards[boardIndex];
        if (board.Expansion != (ExpansionKind)ExpansionByMark[markIndex])
        {
            return false;
        }

        return board.Expansion != ExpansionKind.ARR || board.GrandCompany == PlayerHelpers.GrandCompany;
    }

    // The board in the current city wins. Between two other cities, the board closest to one of its attuned aetherytes
    // leaves the shortest walk after the teleport.
    public static bool TryChoose(byte markIndex, out int boardIndex)
    {
        boardIndex = NoBoard;
        var currentTerritory = Svc.ClientState.TerritoryType;
        var firstServing = NoBoard;
        var bestWalk = float.MaxValue;
        for (var index = 0; index < boards.Length; index++)
        {
            if (!Serves(index, markIndex))
            {
                continue;
            }

            if (boards[index].TerritoryId == currentTerritory)
            {
                boardIndex = index;
                return true;
            }

            if (firstServing == NoBoard)
            {
                firstServing = index;
            }

            if (!TryGetPosition(index, out var position) || !ZoneAetherytes.TryFindNearest(boards[index].TerritoryId, position, out var aetheryte))
            {
                continue;
            }

            var walk = Vector3.DistanceSquared(aetheryte.Position, position);
            if (walk >= bestWalk)
            {
                continue;
            }

            bestWalk = walk;
            boardIndex = index;
        }

        if (boardIndex == NoBoard)
        {
            boardIndex = firstServing;
        }

        return boardIndex != NoBoard;
    }

    public static bool TryGetPosition(int boardIndex, out Vector3 position)
    {
        if (!positionsResolved)
        {
            ResolvePositions();
            positionsResolved = true;
        }

        position = positions[boardIndex];
        return positionFound[boardIndex];
    }

    public static string ShardName(int boardIndex)
    {
        var shardId = boards[boardIndex].ShardId;
        var name = Svc.Data.GetExcelSheet<Aetheryte>().GetRowOrDefault(shardId)?.AethernetName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"aethernet shard #{shardId}" : name;
    }

    private static void ResolvePositions()
    {
        var sheet = Svc.Data.GetExcelSheet<Level>();
        var remaining = boards.Length;
        for (var rowIndex = 0; rowIndex < sheet.Count && remaining > 0; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (row.Type != EventObjectLevelType)
            {
                continue;
            }

            var boardIndex = IndexOfObject(row.Object.RowId);
            if (boardIndex == NoBoard || positionFound[boardIndex] || row.Territory.RowId != boards[boardIndex].TerritoryId)
            {
                continue;
            }

            positions[boardIndex] = new Vector3(row.X, row.Y, row.Z);
            positionFound[boardIndex] = true;
            remaining--;
        }

        for (var boardIndex = 0; boardIndex < boards.Length; boardIndex++)
        {
            var board = boards[boardIndex];
            if (!positionFound[boardIndex])
            {
                RunLog.Warning($"Hunt board {board.ObjectId} has no Level row in territory {board.TerritoryId}; its bills cannot be picked up");
                continue;
            }

            var position = positions[boardIndex];
            RunLog.Info($"Hunt board {board.ObjectId} in {TerritoryNames.Of(board.TerritoryId)} stands at ({position.X:F1}, {position.Y:F1}, {position.Z:F1})");
        }
    }

    private static int IndexOfObject(uint objectId)
    {
        for (var index = 0; index < boards.Length; index++)
        {
            if (boards[index].ObjectId == objectId)
            {
                return index;
            }
        }

        return NoBoard;
    }

    private static string[] LoadMenuTexts()
    {
        var rows = MenuRowByMark;
        var texts = new string[rows.Length];
        Array.Fill(texts, string.Empty);
        try
        {
            var sheet = Svc.Data.GetExcelSheet<RawRow>(null, MenuSheetName);
            for (var markIndex = 0; markIndex < texts.Length; markIndex++)
            {
                if (sheet.TryGetRow(rows[markIndex], out var row))
                {
                    texts[markIndex] = row.ReadStringColumn(MenuTextColumn).ExtractText().Trim();
                }
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Could not read the hunt board menu; tiers will be chosen by their place in the menu");
        }

        return texts;
    }
}
