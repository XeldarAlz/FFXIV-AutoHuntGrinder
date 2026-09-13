# Third-party notices

Auto Hunt Grinder ships hunt mark spawn points derived in part from the two MIT licensed datasets below. The generator in `tools/MarkSpawns` reads them at the pinned commits listed here and writes `AutoHuntGrinder/Core/Hunts/Data/MarkSpawnTable.g.cs`. Only the derived coordinates for hunt targets are shipped; the datasets themselves are not redistributed.

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
- Used: reported monster positions for hunt targets, keyed by BNpcName and limited to each target's own map. The map coordinates are converted to world X and Z and merged into points at least 15 yalms apart. Heights are not taken from this dataset.

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
