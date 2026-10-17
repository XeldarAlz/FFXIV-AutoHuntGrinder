using AutoHuntGrinder.Core.Hunts;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System.Diagnostics;

namespace AutoHuntGrinder.Core.Localization;

// Game text follows the client language, which is picked independently of the plugin language, so its glyphs come
// from the sheets the UI draws names from rather than from the active catalog. Without them a Japanese client shows
// mark, zone and bill names as missing-glyph boxes (Discord: "Garbled characters are appearing in Japanese text").
internal static class GameTextGlyphs
{
    private static readonly bool[] present = new bool[GlyphRanges.CodepointCount];
    private static bool collected;

    public static void Collect()
    {
        if (collected)
        {
            return;
        }

        collected = true;
        var watch = Stopwatch.StartNew();
        try
        {
            ScanSheet<BNpcName>(static row => row.Singular);
            ScanSheet<PlaceName>(static row => row.Name);
            ScanSheet<ClassJob>(static row => row.Name);
            ScanSheet<ClassJob>(static row => row.Abbreviation);
            ScanSheet<GrandCompany>(static row => row.Name);
            ScanSheet<Item>(static row => row.Name);
            ScanBills();
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Failed to collect game text glyphs; some game names may render as missing glyphs");
            return;
        }

        RunLog.Info($"Collected {CountGlyphs()} game text glyphs in {watch.ElapsedMilliseconds} ms");
    }

    public static void MarkInto(bool[] destination)
    {
        for (var codepoint = GlyphRanges.FirstNonAsciiCodepoint; codepoint < GlyphRanges.CodepointCount; codepoint++)
        {
            if (present[codepoint])
            {
                destination[codepoint] = true;
            }
        }
    }

    private static void ScanSheet<T>(Func<T, ReadOnlySeString> text) where T : struct, IExcelRow<T>
    {
        var sheet = Svc.Data.GetExcelSheet<T>();
        for (var index = 0; index < sheet.Count; index++)
        {
            GlyphRanges.MarkText(present, text(sheet.GetRowAt(index)).ExtractText());
        }
    }

    private static void ScanBills()
    {
        var bills = HuntRegistry.Bills;
        for (var index = 0; index < bills.Length; index++)
        {
            GlyphRanges.MarkText(present, bills[index].Name);
            GlyphRanges.MarkText(present, bills[index].UnlockQuestName);
        }
    }

    private static int CountGlyphs()
    {
        var count = 0;
        for (var codepoint = GlyphRanges.FirstNonAsciiCodepoint; codepoint < GlyphRanges.CodepointCount; codepoint++)
        {
            if (present[codepoint])
            {
                count++;
            }
        }

        return count;
    }
}
