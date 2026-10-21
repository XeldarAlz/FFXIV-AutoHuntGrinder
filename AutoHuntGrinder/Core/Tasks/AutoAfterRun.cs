using AutoHuntGrinder.Core.Game.Ops;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public sealed class AutoAfterRun(AfterRunAction action, string commandBlock) : AutoCommon
{
    private const int ReadyWaitMs = 20_000;
    private const int ConfirmationWaitMs = 6_000;
    // The game drops a chat command issued in the middle of a transition.
    private const int PreCommandSettleMs = 800;
    // The second try covers a command eaten by lag or a window in the way.
    private const int LogoutAttempts = 2;
    private const string LogoutCommand = "/logout";
    private const string CloseGameCommand = "/xlkill";

    private readonly AfterRunAction action = action;
    private readonly string commandBlock = commandBlock;

    protected override async Task Execute()
    {
        try
        {
            await FinishRun();
        }
        finally
        {
            ReleaseCombatMovement("after-run");
        }
    }

    // A mob still on the character keeps it in combat, where neither the logout nor a clean exit goes through.
    private async Task FinishRun()
    {
        await FightOffAttackers("after-run");
        if (!await WaitUntilTimed(IsSafeToFinish, ReadyWaitMs, "after-run-ready"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"After-run chat commands and {action} skipped: no safe moment (combat, casting, or a zone change) within {ReadyWaitMs / TimeUnits.MillisecondsPerSecond}s.");
            }

            return;
        }

        await SendPostRunCommands();
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        switch (action)
        {
            case AfterRunAction.Logout:
                await LogOut();
                break;
            case AfterRunAction.CloseGame:
                await CloseGame();
                break;
        }
    }

    private async Task SendPostRunCommands()
    {
        var lines = commandBlock.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            var command = lines[lineIndex].Trim();
            if (command.Length == 0)
            {
                continue;
            }

            if (!command.StartsWith('/'))
            {
                Warn($"Post-run line skipped, commands must start with '/': {command}");
                continue;
            }

            Status = "Sending chat commands";
            SendPostRunCommand(command);
            await DelayMs(AhgConstants.PostRunCommandSpacingMs);
        }
    }

    private void SendPostRunCommand(string command)
    {
        try
        {
            Diag($"Post-run command: {command}");
            Chat.ExecuteCommand(command);
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"Post-run command '{command}' failed.");
        }
    }

    private async Task LogOut()
    {
        Status = "Logging out";
        for (var attempt = 1; attempt <= LogoutAttempts; attempt++)
        {
            await DelayMs(PreCommandSettleMs);
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            Diag($"After-run: sending {LogoutCommand} (attempt {attempt}/{LogoutAttempts}).");
            Chat.ExecuteCommand(LogoutCommand);
            if (!await WaitUntilTimed(DialogDriver.ConfirmationOpen, ConfirmationWaitMs, $"logout-confirmation#{attempt}"))
            {
                continue;
            }

            if (DialogDriver.Confirm())
            {
                Diag("After-run: logout confirmation accepted.");
                return;
            }
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"After-run: the logout confirmation was not accepted after {LogoutAttempts} tries; the character may still be logged in.");
        }
    }

    private async Task CloseGame()
    {
        Status = "Closing the game";
        await DelayMs(PreCommandSettleMs);
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        Diag($"After-run: closing the game with {CloseGameCommand}.");
        Chat.ExecuteCommand(CloseGameCommand);
    }

    private static bool IsSafeToFinish()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.Casting];
}
