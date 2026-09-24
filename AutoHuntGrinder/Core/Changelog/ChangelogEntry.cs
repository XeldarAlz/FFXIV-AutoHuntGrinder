using AutoHuntGrinder.Core.Localization;

namespace AutoHuntGrinder.Core.Changelog;

internal readonly record struct ChangelogEntry(string Version, string Date, LocString[] Highlights);
