using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoHuntGrinder.Core.Game.Player;

// The game's own enemy list: every mob that holds enmity on the character. The combat plugin picks its attackers from
// this same list.
internal static unsafe class EnmityList
{
    public static bool Contains(uint entityId)
    {
        var state = UIState.Instance();
        if (state == null)
        {
            return false;
        }

        ref var hater = ref state->Hater;
        var haters = hater.Haters;
        var count = Math.Min(hater.HaterCount, haters.Length);
        for (var haterIndex = 0; haterIndex < count; haterIndex++)
        {
            if (haters[haterIndex].EntityId == entityId)
            {
                return true;
            }
        }

        return false;
    }
}
