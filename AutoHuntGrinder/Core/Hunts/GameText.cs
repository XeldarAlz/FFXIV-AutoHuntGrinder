using Dalamud.Game;
using ECommons.DalamudServices;
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
}
