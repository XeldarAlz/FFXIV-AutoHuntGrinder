using AutoHuntGrinder.Core.Ipc;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    // Two frames give the combat plugin one update on the ground to drop the target it was steering toward.
    private const int CombatMovementSettleFrames = 2;

    protected void HoldCombatMovement(string scope)
    {
        if (BossModIPC.Instance.HoldMovement())
        {
            Diag($"{scope}: the combat plugin's automatic movement is paused while this plugin drives the character");
        }
    }

    // Taken on the ground at the start of a run: while the character flies, the combat plugin never recomputes, so a
    // hold taken in the air leaves its stale target in place until the next landing.
    protected async Task HoldCombatMovementAndSettle(string scope)
    {
        HoldCombatMovement(scope);
        if (!CancelToken.IsCancellationRequested)
        {
            await NextFrame(CombatMovementSettleFrames);
        }
    }

    protected void ReleaseCombatMovement(string scope)
    {
        if (BossModIPC.Instance.ReleaseMovement())
        {
            Diag($"{scope}: the combat plugin's automatic movement is back to your own setting");
        }
    }

    protected string DescribeOutsideMovement()
    {
        var bossMod = BossModIPC.Instance;
        return $"combat plugin navigating {bossMod.IsNavigating()}, forcing movement {bossMod.IsForcingMovement()}, movement hold {bossMod.MovementHeld}, {ConditionTag()}";
    }
}
