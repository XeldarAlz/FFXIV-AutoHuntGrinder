using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Game.Watchers;

// Latches on the rising edge so one appearance alerts once, and re-arms once no GM is in range so a return alerts again.
internal sealed class GmAlertWatcher : IDisposable
{
    // OnlineStatus rows 1 to 3 are the Game Master tiers.
    private const uint GmStatusMin = 1;
    private const uint GmStatusMax = 3;
    // Players stream into the object table over several frames, so a few scans a second still catch an arrival.
    private const int ScanIntervalMs = 250;
    private const int BeepCountMax = 100;
    // Console.Beep rejects tones outside this range.
    private const int BeepFrequencyMinHz = 37;
    private const int BeepFrequencyMaxHz = 32767;
    private const int BeepDurationMaxMs = 5000;
    private const string KillGameCommand = "/xlkill";

    private bool fired;
    private long nextScanAtMs;

    public GmAlertWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    // Console.Beep blocks its thread for the whole tone, so the burst plays on the thread pool.
    public static void PlayBeeps(int count, int frequencyHz, int durationMs)
    {
        var beeps = Math.Clamp(count, 1, BeepCountMax);
        var frequency = Math.Clamp(frequencyHz, BeepFrequencyMinHz, BeepFrequencyMaxHz);
        var duration = Math.Clamp(durationMs, 1, BeepDurationMaxMs);
        _ = Task.Run(() =>
        {
            for (var beepIndex = 0; beepIndex < beeps; beepIndex++)
            {
                try
                {
                    Console.Beep(frequency, duration);
                }
                catch (Exception exception)
                {
                    RunLog.Debug($"Console.Beep failed: {exception.Message}");
                    break;
                }
            }
        });
    }

    private void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (now < nextScanAtMs)
        {
            return;
        }

        nextScanAtMs = now + ScanIntervalMs;
        var configuration = Plugin.Instance.Configuration;
        if (!AnyActionEnabled(configuration) || FindNearbyGm() is not { } gm)
        {
            fired = false;
            return;
        }

        if (fired)
        {
            return;
        }

        fired = true;
        FireAlerts(configuration, gm);
    }

    private static IPlayerCharacter? FindNearbyGm()
    {
        var objects = Svc.Objects;
        var local = objects.LocalPlayer;
        if (local is null)
        {
            return null;
        }

        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IPlayerCharacter player || player.EntityId == local.EntityId)
            {
                continue;
            }

            if (player.OnlineStatus.RowId is >= GmStatusMin and <= GmStatusMax)
            {
                return player;
            }
        }

        return null;
    }

    private static bool AnyActionEnabled(Configuration configuration)
        => configuration.GmAlertStopRun
        || configuration.GmAlertToast
        || configuration.GmAlertChat
        || configuration.GmAlertSound
        || configuration.GmAlertKillGame
        || configuration.GmAlertCommands.Count > 0;

    private static void FireAlerts(Configuration configuration, IPlayerCharacter gm)
    {
        var name = gm.Name.TextValue;
        RunLog.Warning($"GM detected nearby: {name} (OnlineStatus {gm.OnlineStatus.RowId}). Firing the GM alert.");

        if (configuration.GmAlertStopRun)
        {
            RunGuarded(() => Plugin.Instance.Controller.Stop(), "stopping the run");
        }

        if (configuration.GmAlertToast)
        {
            RunGuarded(() => Svc.Toasts.ShowNormal($"GM {name} is nearby!"), "toast");
        }

        if (configuration.GmAlertChat)
        {
            RunGuarded(() => Svc.Chat.PrintError($"{AhgConstants.LogPrefix} GM {name} is nearby!"), "chat print");
        }

        if (configuration.GmAlertSound)
        {
            PlayBeeps(configuration.GmAlertBeepCount, configuration.GmAlertBeepFrequencyHz, configuration.GmAlertBeepDurationMs);
        }

        var commands = configuration.GmAlertCommands;
        for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            var command = commands[commandIndex];
            RunGuarded(() => Chat.ExecuteCommand(command), $"command '{command}'");
        }

        if (configuration.GmAlertKillGame)
        {
            RunGuarded(() => Chat.ExecuteCommand(KillGameCommand), KillGameCommand);
        }
    }

    // One failing reaction must not stop the ones after it.
    private static void RunGuarded(Action reaction, string what)
    {
        try
        {
            reaction();
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"GM alert: {what} threw.");
        }
    }
}
