using AutoHuntGrinder.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int MarkReviveCommand = (int)clib.Enums.CommandFlag.Revive;
    private const int MarkReviveReturn = (int)clib.Enums.AgentReviveOp.Return;
    private const int MarkReviveAccept = (int)clib.Enums.AgentReviveOp.AcceptRevive;
    private const int MarkRaiseWaitMs = 30_000;
    private const int MarkRaisePollMs = 1_000;
    private const int MarkReviveTransitionMs = 60_000;
    // The revive window ignores input for a moment after the knockout, so Return is re-sent until the trip home starts.
    private const int MarkReturnReissueMs = 1_500;
    private const int MarkRevivePollMs = 250;
    private const int MarkWeaknessSettleMs = 1_000;

    // Solo, it answers the return prompt straight away; in a party it first gives a raise a while to arrive. True once the
    // character stands again, wherever that is; the hunt travels back from there.
    private protected async Task<bool> RecoverFromMarkKnockout()
    {
        BossModIPC.Instance.ClearActive();
        NavmeshIPC.Instance.Stop();
        if (!IsMarkKnockedOut())
        {
            return true;
        }

        MarkPhase = HuntPhase.Travelling;
        var inParty = Svc.Party.Length > 0;
        if (inParty && await WaitForMarkRaise())
        {
            Diag("Hunt: raised where the character fell");
            await DelayMs(MarkWeaknessSettleMs);
            return true;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        Status = "Knocked out, returning to the home point";
        Diag($"Hunt: knocked out {(inParty ? "with no raise coming" : "while solo")}; answering the return prompt");
        SendMarkReviveCommand(MarkReviveReturn);
        var deadline = Environment.TickCount64 + MarkReviveTransitionMs;
        var nextReturnAt = Environment.TickCount64 + MarkReturnReissueMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var knockedOut = IsMarkKnockedOut();
            var zoning = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (!knockedOut && !zoning)
            {
                await DelayMs(MarkWeaknessSettleMs);
                Diag($"Hunt: back on its feet in territory {Svc.ClientState.TerritoryType}");
                return true;
            }

            if (knockedOut && !zoning && Environment.TickCount64 >= nextReturnAt)
            {
                SendMarkReviveCommand(MarkReviveReturn);
                nextReturnAt = Environment.TickCount64 + MarkReturnReissueMs;
            }

            await DelayMs(MarkRevivePollMs);
        }

        Warn($"Hunt: the return to the home point did not complete within {MarkReviveTransitionMs / TimeUnits.MillisecondsPerSecond}s");
        return !IsMarkKnockedOut();
    }

    // A knockout the revive cannot clear would fail every later mark the same way, so the run stops instead.
    private protected async Task<bool> EnsureStanding()
    {
        if (!IsMarkKnockedOut())
        {
            return true;
        }

        Diag("Run: the character is knocked out; bringing it back before going on");
        var standing = await RecoverFromMarkKnockout();
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (standing)
        {
            return true;
        }

        Warn("Run: the character is still knocked out after the revive; stopping the run");
        Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The character could not get back on its feet, so the hunt stops.");
        return false;
    }

    private async Task<bool> WaitForMarkRaise()
    {
        Status = "Knocked out, waiting for a raise";
        Diag($"Hunt: knocked out in a party; waiting up to {MarkRaiseWaitMs / TimeUnits.MillisecondsPerSecond}s for a raise");
        var deadline = Environment.TickCount64 + MarkRaiseWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (!IsMarkKnockedOut())
            {
                return true;
            }

            // Accepting does nothing unless a raise is on offer, so repeating it is harmless.
            SendMarkReviveCommand(MarkReviveAccept);
            await DelayMs(MarkRaisePollMs);
        }

        return !IsMarkKnockedOut();
    }

    private void SendMarkReviveCommand(int operation)
    {
        try
        {
            if (!GameMain.ExecuteCommand(MarkReviveCommand, operation))
            {
                Diag($"Hunt: the revive command ({operation}) was not accepted");
            }
        }
        catch (Exception exception)
        {
            Warn($"Hunt: the revive command ({operation}) failed: {exception.Message}");
        }
    }
}
