# Third-party notices

Auto Hunt Grinder ships spawn points derived in part from the two MIT licensed datasets below. The generator in `tools/MarkSpawns` reads both at the pinned commits listed here and writes the hunt mark points to `AutoHuntGrinder/Core/Hunts/Data/MarkSpawnTable.g.cs`. The generator in `tools/MobSpawns` reads the second at its pinned commit and writes the monster points for the Hunting Log and custom lists to `AutoHuntGrinder/Core/Spawns/Data/MobSpawnTable.g.cs`. Only the derived coordinates are shipped; the datasets themselves are not redistributed.

## __LlamaLibrary

- Source: https://github.com/nt153133/__LlamaLibrary, file `Resources/AllHunts.json` at commit `aadcf65fd8315e9ba56e97cfe32336302c1f0a27`.
- Used: one world position (X, Y, Z) per hunt target, keyed by MobHuntTarget row. A position is kept only when its territory and monster name match the game's own hunt sheets.

```text
MIT License

Copyright (c) 2022 nt153133

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## FFXIV Teamcraft

- Source: https://github.com/ffxiv-teamcraft/ffxiv-teamcraft, file `libs/data/src/lib/json/monsters.json` at commit `46dde90c0b070fb13197977fb01fdf288fd56aec`.
- Used: reported monster positions, keyed by BNpcName.
  - Hunt marks: positions limited to each target's own map. The map coordinates are converted to world X and Z and merged into points at least 15 yalms apart. Heights are not taken from this dataset for hunt marks.
  - Hunting Log and custom lists: positions of every monster with a named BNpcName row, limited to open-world zones. The map coordinates are converted to world X and Z, and the reported z times 100 is kept as a height hint that the plugin snaps to the ground. Positions recorded during a FATE are dropped for a monster and zone that has any other position. The rest are merged into points at least 25 yalms apart, and each monster keeps up to 16 points per zone, the most reported first.

```text
MIT License

Copyright (c) 2017 Flavien Normand

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
