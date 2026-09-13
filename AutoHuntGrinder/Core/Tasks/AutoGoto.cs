using AutoHuntGrinder.Core.External;
using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// Debug probe for the travel stack: "/ahg goto <territoryId> <x> <y> <z>" travels there, "/ahg goto stop" cancels it.
internal sealed class AutoGoto(uint territoryId, Vector3 destination) : AutoCommon
{
    private const float ArriveWithinMeters = 3f;
    private const string StopArgument = "stop";
    private const string Usage = AhgConstants.LogPrefix + " Usage: /ahg goto <territoryId> <x> <y> <z>, or /ahg goto stop.";

    private static readonly char[] ArgumentSeparators = [' ', ','];

    public static void HandleCommand(string arguments, bool huntRunning)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopArgument, StringComparison.OrdinalIgnoreCase))
        {
            if (automation.CurrentTask is not AutoGoto)
            {
                Svc.Chat.Print($"{AhgConstants.LogPrefix} No goto is running.");
                return;
            }

            automation.Stop();
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Goto stopped.");
            return;
        }

        if (!TryParse(arguments, out var territoryId, out var destination))
        {
            Svc.Chat.PrintError(Usage);
            return;
        }

        if (automation.CurrentTask is AutoGoto)
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} A goto is already running. /ahg goto stop cancels it.");
            return;
        }

        if (huntRunning)
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Stop the hunt before using goto.");
            return;
        }

        if (!ExternalPlugins.IsInstalled(ExternalPlugin.Vnavmesh))
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Goto needs the pathfinding plugin listed on the Plugins page.");
            return;
        }

        Svc.Chat.Print($"{AhgConstants.LogPrefix} Goto: heading to {TerritoryNames.Of(territoryId)} ({territoryId}) at {destination.X:F1}, {destination.Y:F1}, {destination.Z:F1}.");
        automation.Start(new AutoGoto(territoryId, destination));
    }

    protected override async Task Execute()
    {
        var zoneName = TerritoryNames.Of(territoryId);
        Diag($"Goto: travelling to {zoneName} ({territoryId}) at {destination}.");
        var arrived = await TravelTo(territoryId, destination, ArriveWithinMeters);
        if (CancelToken.IsCancellationRequested)
        {
            Diag("Goto: cancelled.");
            return;
        }

        if (!arrived)
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Goto: could not reach the spot in {zoneName}. The log has the details.");
            return;
        }

        Status = "Arrived";
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Goto: arrived in {zoneName}.");
    }

    private static bool TryParse(string arguments, out uint territoryId, out Vector3 destination)
    {
        territoryId = 0;
        destination = default;
        var parts = arguments.Split(ArgumentSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out territoryId))
        {
            return false;
        }

        if (!TryParseCoordinate(parts[1], out var x) || !TryParseCoordinate(parts[2], out var y) || !TryParseCoordinate(parts[3], out var z))
        {
            return false;
        }

        destination = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseCoordinate(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
