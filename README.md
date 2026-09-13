<p align="center">
  <img src="AutoHuntGrinder/Images/Icon.png" width="180" alt="Auto Hunt Grinder icon" />
</p>

<h1 align="center">Auto Hunt Grinder</h1>

<p align="center">
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHuntGrinder/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoHuntGrinder?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHuntGrinder/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoHuntGrinder/total?style=flat-square&color=blue&cacheSeconds=300"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoHuntGrinder/actions/workflows/release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoHuntGrinder/release.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Hunt bills, cleared for you. Built on Dalamud.</em>
</p>

---

> **Early development.** The first release is not out yet. The window, the bill picker and the live bill reader are in place; the automation that picks up bills and hunts the marks is being built now.

## What it does

Lists every daily and weekly hunt bill from A Realm Reborn through Dawntrail in one window. Tick the bills you want and press **Start**: the plugin travels to each hunt board you still need a bill from, accepts it, then teleports and flies to every mark on your bills, fights it, and moves on until each bill is done.

## Features

- **Every hunt board**: Grand Company, clan and guildship bills from ARR through DT, dailies and weekly Elite Marks, with boards you haven't unlocked yet greyed out.
- **Live bill progress**: reads the bills you hold and your kill counts straight from the game, so the window always matches what the game shows.
- **Board pickup**: walks up to the hunt board and accepts any selected bill you aren't holding yet.
- **Route planning**: groups marks by zone, so a run teleports as little as possible.
- **Mark finder**: flies to each mark's known spawn areas and sweeps them until it shows up.
- **Pause & resume**: park a run without losing your progress, and auto-pause while you're in a duty.
- **History**: every run recorded with bills cleared, marks hunted, and seals or nuts earned.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto Hunt Grinder**, and install.

The plugin needs a few helpers for movement and combat to be installed and loaded. Open `/ahg deps` after install to see the list and one-click each missing one.

## Commands

| Command | Action |
|---|---|
| `/ahg` | Toggle the main window |
| `/huntgrinder` | Alias for `/ahg` |
| `/ahg config` | Open the Settings page |
| `/ahg stats` | Open the History page |
| `/ahg deps` | Open the Plugins page |
| `/ahg about` | Open the About page |
| `/ahg pause` | Pause or resume the current run |
| `/ahg target` | Log targeted NPC's BaseId (debug helper) |

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as zone, mark, and bill names always follows the game client.

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoHuntGrinder/issues/new?template=translation_report.yml) and tell me what it should say instead.

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
