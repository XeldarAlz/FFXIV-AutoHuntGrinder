using AutoHuntGrinder.Core.Game.Player;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Numerics;

namespace AutoHuntGrinder.Core.Travel;

internal readonly record struct CityInn(uint CityTerritoryId, uint InnTerritoryId, uint InnkeeperBaseId, Vector3 InnkeeperPosition);

internal static class GrandCompanyInns
{
    private static readonly CityInn limsaLominsa = new(128, 177, 1000974, new Vector3(15.42688f, 39.99999f, 12.466553f));
    private static readonly CityInn gridania = new(132, 179, 1000102, new Vector3(25.6627f, -8f, 99.74237f));
    private static readonly CityInn uldah = new(130, 178, 1001976, new Vector3(28.85994f, 6.999999f, -80.12716f));

    // A character with no Grand Company has no home city to prefer, so it falls back to Limsa Lominsa.
    public static CityInn For(byte grandCompany) => grandCompany switch
    {
        GrandCompanyId.TwinAdder      => gridania,
        GrandCompanyId.ImmortalFlames => uldah,
        _                             => limsaLominsa,
    };

    public static unsafe CityInn ForPlayer()
    {
        var playerState = PlayerState.Instance();
        return For(playerState == null ? GrandCompanyId.None : playerState->GrandCompany);
    }
}
