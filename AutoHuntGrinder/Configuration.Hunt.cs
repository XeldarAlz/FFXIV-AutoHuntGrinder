namespace AutoHuntGrinder;

public sealed partial class Configuration
{
    // The bundled hunt preset is rewritten in the combat plugin only while this trails its revision, so edits to it survive otherwise.
    public int BundledCombatPresetRevision { get; set; }

    public bool FateHuntOthersWhileWaiting { get; set; } = true;

    public int FateRecheckMinutes { get; set; } = 5;

    public int FateVisitMinutes { get; set; } = 3;

    public int FateWaitBudgetMinutes { get; set; } = 20;
}
