using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Windows;

internal static class ExpansionLabels
{
    public static string Name(ExpansionKind kind) => kind switch
    {
        ExpansionKind.ARR => Loc.T(L.Hunt.ExpansionArr),
        ExpansionKind.HW  => Loc.T(L.Hunt.ExpansionHw),
        ExpansionKind.SB  => Loc.T(L.Hunt.ExpansionSb),
        ExpansionKind.ShB => Loc.T(L.Hunt.ExpansionShb),
        ExpansionKind.EW  => Loc.T(L.Hunt.ExpansionEw),
        _                 => Loc.T(L.Hunt.ExpansionDt),
    };
}
