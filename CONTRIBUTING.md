# Contributing

Thanks for taking an interest. This is a small solo project, but PRs are welcome and I'll review them.

## Quick start

```bash
git clone --recurse-submodules https://github.com/XeldarAlz/FFXIV-AutoHuntGrinder.git
cd FFXIV-AutoHuntGrinder
dotnet build AutoHuntGrinder.sln -c Release
```

You need the .NET 10 SDK. The plugin requires Dalamud at runtime; CI pulls a Dalamud dev build automatically and that's enough to compile. See `.github/workflows/release.yml` if you want to reproduce CI locally.

Load the built plugin via `/xlsettings` -> **Experimental** -> **Dev Plugin Locations**, pointing at `AutoHuntGrinder/bin/Release/AutoHuntGrinder.dll`.

## Project layout

- `AutoHuntGrinder/Core/`: hunt board and bill data, the mark bill reader, automation state machine, IPC adapters.
- `AutoHuntGrinder/Windows/`: ImGui main window, settings, dependencies.
- `AutoHuntGrinder/`: plugin entry points, config, command wiring.
- `ECommons/`: submodule, shared Dalamud helpers. Don't patch this directly; upstream it.

Keep logic small and direct. This plugin has one job.

## Before you open a PR

1. `dotnet build -c Release` cleanly.
2. Test in-game on at least one hunt board per expansion you touched. Boards, bill ranks, and mark spawns differ between expansions, so a fix that works for Dawntrail may not work for Stormblood.
3. Keep the diff focused. One concern per PR.
4. Match the existing style. No heavy abstractions "for later."
5. If your change affects what a user sees or types (commands, window layout, settings), update the README.
6. If you used AI beyond autocomplete, say which level in the PR description. It is one line, and [AI-USAGE.md](AI-USAGE.md) explains the level names and why I ask.

## Good first issues

Check the tracker for anything labeled `good first issue`. Mark-specific quirks (a spawn area the plugin never finds, a mark it can't reach, a board it can't use) are usually the lowest-friction way to help: pick the bill that's misbehaving, attach a log of what the plugin did vs. what should have happened, and a fix is usually a small change.

## Security

Please don't file public issues for security problems; see [SECURITY.md](SECURITY.md).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Be decent.

## License

By contributing, you agree your contributions are licensed under AGPL-3.0-or-later with the additional terms in [NOTICE](NOTICE), the same as the project.
