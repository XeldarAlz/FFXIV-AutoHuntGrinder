using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int UpkeepConsumeWaitMs = 6_000;
    private const int UpkeepConsumeCheckFrames = 100;
    // A failed repair or break tends to fail the same way straight after (no Dark Matter, city not attuned), so it rests first.
    private const int UpkeepRepairRetryMs = 600_000;
    private const int UpkeepBreakRetryMs = 300_000;

    private int marksSinceBreak;
    private long repairRetryAtMs;
    private long breakRetryAtMs;

    // One call per mark kill; the humanizer counts these toward its next city break. The count lives on this task, so it
    // starts over whenever a run starts or resumes.
    protected void NoteMarkKilled() => marksSinceBreak++;

    // True when any step ran, which can leave the character dismounted or in another zone, so the caller should travel
    // to its next spot afresh.
    protected async Task<bool> RunUpkeep()
    {
        var configuration = Plugin.Instance.Configuration;
        var repairDue = UpkeepRepairDue(configuration);
        var breakDue = UpkeepBreakDue(configuration);
        if (!repairDue && !breakDue && !UpkeepConsumablesDue(configuration))
        {
            return false;
        }

        if (!await UpkeepWaitUntilFree())
        {
            return false;
        }

        if (repairDue)
        {
            await RunScheduledRepair();
        }

        if (breakDue && !CancelToken.IsCancellationRequested)
        {
            await RunScheduledBreak(configuration);
        }

        if (!CancelToken.IsCancellationRequested && UpkeepConsumablesDue(configuration))
        {
            await RefreshConsumables(configuration);
        }

        return true;
    }

    private bool UpkeepRepairDue(Configuration configuration)
        => configuration.AutoRepair
        && Environment.TickCount64 >= repairRetryAtMs
        && RepairOps.NeedsRepair(configuration.AutoRepairThresholdPercent);

    private bool UpkeepBreakDue(Configuration configuration)
        => configuration.HumanizerEnabled
        && Environment.TickCount64 >= breakRetryAtMs
        && marksSinceBreak >= Math.Max(1, configuration.HumanizerMarksBeforeBreak);

    private static bool UpkeepConsumablesDue(Configuration configuration)
        => configuration.AutoConsume
        && configuration.AutoConsumeItems.Count > 0
        && FoodOps.AnyNeeded(configuration);

    private static bool UpkeepCharacterFree()
        => Svc.Objects.LocalPlayer is { IsDead: false }
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51];

    private async Task<bool> UpkeepWaitUntilFree()
    {
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        await FightOffAttackers("upkeep");
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (UpkeepCharacterFree())
        {
            return true;
        }

        Diag($"Upkeep: something is due but the character is busy ({ConditionTag()}); trying again after the next mark");
        return false;
    }

    private async Task RunScheduledRepair()
    {
        if (await RepairGear() || CancelToken.IsCancellationRequested)
        {
            return;
        }

        repairRetryAtMs = Environment.TickCount64 + UpkeepRepairRetryMs;
        Warn($"Upkeep: the repair did not finish; the next try waits {UpkeepRepairRetryMs / TimeUnits.MillisecondsPerMinute} minutes");
    }

    private async Task RunScheduledBreak(Configuration configuration)
    {
        var cityTerritoryId = PickBreakCity(configuration);
        if (cityTerritoryId == 0)
        {
            Diag("Upkeep: a city break is due but no listed city is ticked; skipping it");
            marksSinceBreak = 0;
            return;
        }

        var minutes = RollBreakMinutes(configuration);
        Diag($"Upkeep: {marksSinceBreak} marks since the last break (every {configuration.HumanizerMarksBeforeBreak}); taking a {minutes}m break");
        if (await TakeCityBreak(cityTerritoryId, minutes * TimeUnits.MillisecondsPerMinute))
        {
            marksSinceBreak = 0;
            return;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        breakRetryAtMs = Environment.TickCount64 + UpkeepBreakRetryMs;
        Warn($"Upkeep: could not reach {TerritoryNames.Of(cityTerritoryId)} for the break; the next try waits {UpkeepBreakRetryMs / TimeUnits.MillisecondsPerMinute} minutes");
    }

    // Each item gets a wall-clock deadline, so a use the game never applies cannot park the run.
    private async Task RefreshConsumables(Configuration configuration)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount("upkeep-dismount-consume");
        }

        if (Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.InCombat])
        {
            Diag($"Upkeep: cannot eat right now ({ConditionTag()}); trying again after the next mark");
            return;
        }

        var minimumSeconds = FoodOps.MinimumBuffSeconds(configuration);
        var items = configuration.AutoConsumeItems;
        for (var index = 0; index < items.Count && !CancelToken.IsCancellationRequested; index++)
        {
            var entry = items[index];
            if (FoodOps.HasStatus(entry.StatusId, minimumSeconds) || !FoodOps.IsAvailable(entry))
            {
                continue;
            }

            Status = $"Consuming {entry.Name}";
            Diag($"Upkeep: consuming {entry.Name} (status {entry.StatusId} missing or under {configuration.AutoConsumeMinMinutes}m)");
            await WaitUntilTimed(() => ConsumeUntilBuffed(entry, minimumSeconds), UpkeepConsumeWaitMs, $"consume-{entry.ItemId}", UpkeepConsumeCheckFrames);
        }
    }

    // Stops once the buff shows, or when combat starts, since eating is refused then and the next upkeep tries again.
    private static bool ConsumeUntilBuffed(ConsumableEntry entry, float minimumSeconds)
    {
        if (FoodOps.HasStatus(entry.StatusId, minimumSeconds) || Svc.Condition[ConditionFlag.InCombat])
        {
            return true;
        }

        FoodOps.UseConsumable(entry);
        return false;
    }

    private static uint PickBreakCity(Configuration configuration)
    {
        var cities = BreakCities.All;
        var selected = configuration.HumanizerCities;
        var eligible = 0;
        for (var index = 0; index < cities.Length; index++)
        {
            if (selected.Contains(cities[index].TerritoryId))
            {
                eligible++;
            }
        }

        if (eligible == 0)
        {
            return 0;
        }

        var pick = Random.Shared.Next(eligible);
        for (var index = 0; index < cities.Length; index++)
        {
            if (!selected.Contains(cities[index].TerritoryId))
            {
                continue;
            }

            if (pick == 0)
            {
                return cities[index].TerritoryId;
            }

            pick--;
        }

        return 0;
    }

    private static int RollBreakMinutes(Configuration configuration)
    {
        var shortest = Math.Max(1, configuration.HumanizerBreakMinMinutes);
        var longest = Math.Max(shortest, configuration.HumanizerBreakMaxMinutes);
        return Random.Shared.Next(shortest, longest + 1);
    }
}
