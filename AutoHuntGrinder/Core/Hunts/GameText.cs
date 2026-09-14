using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace AutoHuntGrinder.Core.Hunts;

internal static class GameText
{
    // English BNpcName rows are stored lower case ("bone crawler"); other clients keep their own casing.
    public static string Title(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (Svc.ClientState.ClientLanguage == ClientLanguage.English)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
        }

        return string.Concat(char.ToUpper(text[0], CultureInfo.InvariantCulture).ToString(), text.AsSpan(1));
    }

    public static string NpcName(ExcelSheet<BNpcName> sheet, uint nameId)
        => Title(sheet.GetRowOrDefault(nameId)?.Singular.ExtractText() ?? string.Empty);

    // A row with no name reads as its id, so a list never shows a blank line.
    public static string NpcNameOrId(ExcelSheet<BNpcName> sheet, uint nameId)
    {
        var name = NpcName(sheet, nameId);
        return name.Length == 0 ? $"#{nameId}" : name;
    }
}
