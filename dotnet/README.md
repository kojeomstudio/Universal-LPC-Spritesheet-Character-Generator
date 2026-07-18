# LPC Sprite Generator — C# / WPF port

A native Windows port of the [Universal LPC Sprite Sheet Character Generator](https://github.com/sanderfrenken/Universal-LPC-Spritesheet-Character-Generator), built with .NET 8 + WPF. The original TypeScript/Mithril/Electron project is preserved unchanged one directory up; this is an independent implementation that shares the same source data (`sheet_definitions/`, `palette_definitions/`, `spritesheets/`) but renders via `System.Drawing.Common` instead of canvas/WebGL.

## Layout

```
dotnet/
├─ LpcSpriteGen.sln
├─ src/
│  ├─ LpcSpriteGen.Core/         # Pure C# logic — catalog, paths, palette recolor, renderer, random, ZIP
│  ├─ LpcSpriteGen.Headless/     # CLI (AI-agent friendly)
│  └─ LpcSpriteGen.Wpf/          # 3-panel GUI
└─ tests/
   └─ LpcSpriteGen.Core.Tests/   # xUnit (30 tests, includes a 99.99% pixel-parity check against the JS baseline)
```

## Build

```bash
# From this directory. Requires .NET 8 SDK (net8.0-windows).
dotnet build LpcSpriteGen.sln -c Release
dotnet test tests/LpcSpriteGen.Core.Tests/LpcSpriteGen.Core.Tests.csproj
```

Single-file binaries (publish):

```bash
# Windows
scripts/build-dotnet.bat           # from the parent (sprite-generator) repo
# Or directly:
dotnet publish src/LpcSpriteGen.Headless/LpcSpriteGen.Headless.csproj /p:PublishProfile=win-x64.pubxml
dotnet publish src/LpcSpriteGen.Wpf/LpcSpriteGen.Wpf.csproj /p:PublishProfile=win-x64.pubxml
```

Output lands in `<workspace>/bins/lpc-sprite-generator-dotnet/{headless,wpf}/`.

## Headless CLI (for AI agents / scripting)

The headless binary (`LpcSpriteGen.Headless.exe`) is the primary automation surface. Run it from the **sprite-generator repo root** so it can find `sheet_definitions/`, `palette_definitions/`, and `spritesheets/` (or set `LPC_REPO_ROOT` / use `--sheet-defs` / `--palette-defs` / `--spritesheets` overrides).

### Commands

| Command | Effect |
|---|---|
| `--random --output out.png` | Generate one random character |
| `--random --seed 42 --count 10 --output-dir ./out` | Batch — files named `character-001.png`...; seed increments per item |
| `--selections in.json --output out.png` | Render from a selections JSON |
| `--selections - --output out.png` | Read selections JSON from stdin |
| `--manifest manifest.json` | Batch from a `[{seed,bodyType,output}]` manifest; returns a result JSON |
| `--list-items [--json]` | All itemIds (human / JSON) |
| `--dump-catalog [--indent]` | Full catalog as JSON (items, typeNames, palettes) |
| `--describe <itemId> [--indent]` | One item's full metadata (layers, zPos, recolors, credits) |
| `--validate-selections in.json` | Dry-run validation; exit 0 valid / 3 invalid |

### Flags

| Flag | Values | Notes |
|---|---|---|
| `--body-type` | male / female / teen / child / muscular / pregnant | Default `male` |
| `--format` | `png` (default) / `zip-anim` / `zip-item` / `zip-frame` | Output container |
| `--json` | — | Machine-readable NDJSON status lines on stdout |
| `--spritesheets <dir>` | path | Override the spritesheets root |
| `--sheet-defs <dir>`, `--palette-defs <dir>` | path | Override catalog roots |

### Exit codes

`0` = success · `1` = partial failure · `2` = bad args · `3` = validation failure

### Selections JSON format

```json
{
  "bodyType": "male",
  "selections": {
    "body":       { "itemId": "body",            "recolor": "light" },
    "head":       { "itemId": "heads_human_male", "recolor": "light" },
    "expression": { "itemId": "face_neutral",    "recolor": "light" },
    "hair":       { "itemId": "hair_long",       "recolor": "ulpc.orange" }
  }
}
```

Required selections: `body`, `head`, `expression` (default: `face_neutral`). All others optional.

## GUI

The WPF app (`LpcSpriteGen.Wpf.exe`) presents an Aseprite-style 3-panel layout: catalog tree + zoomable preview + selections list, with menu/toolbar/status bar. File → Open/Save selections (`.json`); Character → Randomize (with seed); Export → PNG + automatic credits.txt (legally required attribution). Animation dropdown + zoom slider live above the canvas.

## Logging

Every run writes a timestamped log to `<exe-dir>/log/lpc-<stamp>-<pid>.log` and refreshes `log/latest.log`. The Headless CLI mirrors log lines to stderr (so stdout stays clean for `--json`/`--dump-catalog`/`--list-items --json` machine consumers). The WPF GUI writes the same file but keeps stdout quiet. Open the log folder from the GUI via **Tools → Open log folder**, or read `latest.log` to debug a silent failure.

## Differences from the JS/Electron project

- **No URL hash state** — replaced with explicit `.json` file save/load. (Removed ~500 lines of alias-resolution machinery.)
- **No WebGL** — uses the proven CPU palette-recolor path (tolerance=1/channel, first-match-wins, LRU cache). Produces 99.99% pixel-identical output to the WebGL path on the test baseline.
- **7 random-generator bugs fixed** (head type_name, body.ulpc palette keys, mandatory head/expression inclusion, deterministic seeds, etc.) — see `RandomGeneratorTests`.
- **Binaries are ~15–25MB** single-file, self-contained — no Electron/Chromium runtime.
- **No `alert()` dialogs, no sponsor banner, no web chunk-loading** — replaced with native WPF UI / synchronous in-memory catalog load.

## License attribution

LPC assets are predominantly CC-BY / CC-BY-SA / GPL / OGA-BY — **attribution is mandatory**. The GUI auto-emits `*.credits.txt` alongside every PNG export; the CLI prints credits to stdout for the first character of each batch. Always ship the credits file with the sprites.
