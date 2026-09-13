using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHuntGrinder.Core.Hunts;

internal readonly record struct HuntWallet(int AlliedSeals, int CenturioSeals, int Nuts)
{
    public const uint AlliedSealsItemId = 27;
    public const uint CenturioSealsItemId = 10307;
    public const uint SacksOfNutsItemId = 26533;

    // An unloaded currency inventory reads as an empty wallet, which would later count a reload as earnings.
    public static unsafe bool TryRead(out HuntWallet wallet)
    {
        wallet = default;
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null)
        {
            return false;
        }

        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return false;
        }

        var currency = inventory->GetInventoryContainer(InventoryType.Currency);
        if (currency == null || !currency->IsLoaded)
        {
            return false;
        }

        wallet = new HuntWallet(
            inventory->GetInventoryItemCount(AlliedSealsItemId),
            inventory->GetInventoryItemCount(CenturioSealsItemId),
            inventory->GetInventoryItemCount(SacksOfNutsItemId));
        return true;
    }
}
