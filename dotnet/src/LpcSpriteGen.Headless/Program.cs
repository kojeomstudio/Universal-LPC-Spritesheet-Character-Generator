// LPC Sprite Generator — headless CLI for AI agent / scripting use.
//
// Usage:
//   lpcsprites --random --output out.png
//   lpcsprites --random --seed 42 --count 10 --output-dir ./out
//   lpcsprites --selections in.json --output out.png
//   lpcsprites --selections - --output out.png   (read JSON from stdin)
//   lpcsprites --manifest manifest.json
//   lpcsprites --list-items [--json]
//   lpcsprites --dump-catalog [--indent]
//   lpcsprites --describe <itemId> [--indent]
//   lpcsprites --validate-selections in.json
//
// Common flags:
//   --spritesheets <dir>   override spritesheets directory
//   --sheet-defs <dir>     override sheet_definitions directory
//   --palette-defs <dir>   override palette_definitions directory
//   --format png           output format (png only for now)
//   --json                 machine-readable stdout (NDJSON status lines)
//
// Exit codes:
//   0 = success   1 = partial failure   2 = bad args   3 = validation failure
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Diagnostics;

namespace LpcSpriteGen.Headless;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitPartialFailure = 1;
    private const int ExitBadArgs = 2;
    private const int ExitValidationFailed = 3;

    /// <summary>Case-insensitive JSON options — accepts both PascalCase (C# defaults) and
    /// camelCase (canonical LPC selections JSON) property names.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<int> Main(string[] args)
    {
        // Initialize logging FIRST so any failure still leaves an audit trail under log/.
        Logger.Init(minLevel: LogLevel.Debug, mirrorToConsole: true);
        Logger.Info($"headless CLI starting; args: {string.Join(' ', args)}");
        if (Logger.LogFile != null)
            Console.Error.WriteLine($"[log] {Logger.LogFile}");
        try { return await RunAsync(args); }
        catch (Exception e)
        {
            Logger.Error("fatal: " + e.Message, e);
            Console.Error.WriteLine($"fatal: {e.Message}");
            return ExitPartialFailure;
        }
        finally
        {
            Logger.Info("headless CLI exiting");
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts == null) return ExitBadArgs;

        var (cat, spritesheetsDir) = LoadCatalog(opts);
        var renderer = new Core.Rendering.Renderer(cat, spritesheetsDir);

        if (opts.ListItems)
        {
            if (opts.Json)
                Console.WriteLine(JsonSerializer.Serialize(cat.AllItemIds.OrderBy(i => i)));
            else
            {
                Console.WriteLine($"itemIds ({cat.AllItemIds.Count}):");
                foreach (var id in cat.AllItemIds.OrderBy(i => i))
                    Console.WriteLine($"  {id}");
            }
            return ExitSuccess;
        }

        if (opts.DumpCatalog)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildCatalogDump(cat),
                new JsonSerializerOptions { WriteIndented = opts.Indent }));
            return ExitSuccess;
        }

        if (opts.Describe is { } itemId)
        {
            var r = cat.GetItem(itemId);
            if (!r.IsOk) { Console.Error.WriteLine($"error: {r.Error}"); return ExitPartialFailure; }
            Console.WriteLine(JsonSerializer.Serialize(BuildItemDump(r.Value),
                new JsonSerializerOptions { WriteIndented = opts.Indent }));
            return ExitSuccess;
        }

        if (opts.ValidateSelections is { } validatePath)
        {
            var issues = ValidateSelections(cat, validatePath);
            if (issues.Count == 0) { Console.WriteLine("valid"); return ExitSuccess; }
            foreach (var i in issues) Console.Error.WriteLine(i);
            return ExitValidationFailed;
        }

        if (opts.Manifest is { } manifestPath)
            return await RunManifestAsync(cat, renderer, manifestPath);

        if (opts.Random)
            return await RunRandomAsync(cat, renderer, opts);

        if (opts.SelectionsPath != null)
            return await RunSelectionsAsync(cat, renderer, opts);

        Console.Error.WriteLine("error: no command. Use --random, --selections, --manifest, --list-items, --dump-catalog, --describe, or --validate-selections.");
        return ExitBadArgs;
    }

    // ── Render helpers ─────────────────────────────────────────────────────────────

    private static async Task<int> RunRandomAsync(LpcCatalog cat, Core.Rendering.Renderer renderer, Options opts)
    {
        int count = Math.Max(1, opts.Count);
        int exit = ExitSuccess;
        var gen = new RandomGenerator(cat);

        if (opts.OutputDir != null) Directory.CreateDirectory(opts.OutputDir);

        for (int i = 0; i < count; i++)
        {
            int? seed = opts.Seed.HasValue ? opts.Seed + i : null;
            var result = gen.Generate(seed, opts.BodyType);
            string outPath = opts.OutputDir != null
                ? Path.Combine(opts.OutputDir, $"character-{(i + 1):D3}.png")
                : (opts.Output ?? "character.png");

            try
            {
                if (opts.Format.StartsWith("zip-"))
                    await RenderAndZipAsync(cat, renderer, result.Selections, result.BodyType, outPath, opts.Format);
                else
                    await RenderAndSaveAsync(renderer, result.Selections, result.BodyType, outPath);
                if (opts.Json)
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, output = outPath, seed, count = i + 1, of = count }));
                else
                    Console.WriteLine($"[random] saved {outPath} (seed={seed}, {i + 1}/{count})");
                if (i == 0) PrintCredits(result, opts.Json);
            }
            catch (Exception e)
            {
                exit = ExitPartialFailure;
                Console.Error.WriteLine($"[random] failed (seed={seed}): {e.Message}");
            }
        }
        return exit;
    }

    private static async Task<int> RunSelectionsAsync(LpcCatalog cat, Core.Rendering.Renderer renderer, Options opts)
    {
        var json = opts.SelectionsPath == "-"
            ? Console.In.ReadToEnd()
            : await File.ReadAllTextAsync(opts.SelectionsPath!);
        var payload = JsonSerializer.Deserialize<SelectionsPayload>(json, JsonOpts) ?? throw new("invalid selections JSON");
        string bodyType = payload.BodyType ?? opts.BodyType;
        string outPath = opts.Output ?? "character.png";
        if (opts.Format.StartsWith("zip-"))
            await RenderAndZipAsync(cat, renderer, payload.ToSelections(), bodyType, outPath, opts.Format);
        else
            await RenderAndSaveAsync(renderer, payload.ToSelections(), bodyType, outPath);
        if (opts.Json)
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, output = outPath, bodyType }));
        else
            Console.WriteLine($"[selections] saved {outPath}");
        return ExitSuccess;
    }

    private static async Task<int> RunManifestAsync(LpcCatalog cat, Core.Rendering.Renderer renderer, string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath);
        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(json, JsonOpts) ?? new();
        var gen = new RandomGenerator(cat);
        var results = new List<object>();
        int exit = ExitSuccess;

        foreach (var e in entries)
        {
            var bodyType = e.BodyType ?? "male";
            var manual = e.ToSelections();
            RandomResult r = manual != null
                ? new() { Selections = manual, BodyType = bodyType }
                : gen.Generate(e.Seed, bodyType);

            try
            {
                await RenderAndSaveAsync(renderer, r.Selections, r.BodyType, e.Output);
                results.Add(new { ok = true, output = e.Output, seed = e.Seed, bodyType });
            }
            catch (Exception ex)
            {
                exit = ExitPartialFailure;
                results.Add(new { ok = false, output = e.Output, error = ex.Message });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { results }));
        return exit;
    }

    private static async Task RenderAndSaveAsync(Core.Rendering.Renderer renderer, Selections selections, string bodyType, string outPath)
    {
        using var bmp = await renderer.RenderCharacterAsync(selections, bodyType);
        bmp.Save(outPath, ImageFormat.Png);
    }

    private static async Task RenderAndZipAsync(LpcCatalog cat, Core.Rendering.Renderer renderer, Selections selections, string bodyType, string outPath, string format)
    {
        var layout = format switch
        {
            "zip-anim" => Core.Zip.ZipLayout.ByAnimation,
            "zip-item" => Core.Zip.ZipLayout.ByItem,
            "zip-frame" => Core.Zip.ZipLayout.ByFrame,
            _ => throw new ArgumentException($"unknown zip format: {format}"),
        };
        var exporter = new Core.Zip.ZipExporter(cat, renderer);
        await exporter.ExportAsync(selections, bodyType, layout, outPath);
    }

    private static void PrintCredits(RandomResult result, bool json)
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { credits = result.Credits }));
        else
        {
            Console.WriteLine("credits:");
            foreach (var c in result.Credits)
                Console.WriteLine($"  - {c.Author} ({c.License}): {c.Name ?? ""}");
        }
    }

    // ── Catalog dump helpers ────────────────────────────────────────────────────────

    private static object BuildCatalogDump(LpcCatalog cat)
    {
        var items = cat.AllItemIds.OrderBy(i => i).Select(id =>
        {
            var item = cat.GetItem(id).UnsafeUnwrap();
            return new
            {
                itemId = id,
                name = item.Lite.Name,
                typeName = item.Lite.TypeName,
                required = item.Lite.Required,
                animations = item.Lite.Animations,
                variants = item.Lite.Variants,
                recolors = item.Lite.Recolors.Select(r => new
                {
                    material = r.Material,
                    typeName = r.TypeName,
                    label = r.Label,
                    variants = r.Variants,
                }),
                licenses = item.Lite.Licenses,
            };
        });
        return new
        {
            typeNames = cat.AllTypeNames.OrderBy(t => t),
            items,
            palettes = cat.PaletteMeta.Materials.ToDictionary(
                m => m.Key,
                m => new
                {
                    label = m.Value.Label,
                    @default = m.Value.Default,
                    baseColor = m.Value.Base,
                    versions = m.Value.Palettes.ToDictionary(v => v.Key, v => v.Value.Keys.ToArray()),
                }),
        };
    }

    private static object BuildItemDump(ItemMerged item) => new
    {
        itemId = item.ItemId,
        name = item.Lite.Name,
        typeName = item.Lite.TypeName,
        required = item.Lite.Required,
        animations = item.Lite.Animations,
        variants = item.Lite.Variants,
        matchBodyColor = item.Lite.MatchBodyColor,
        priority = item.Lite.Priority,
        licenses = item.Lite.Licenses,
        recolors = item.Lite.Recolors.Select(r => new
        {
            material = r.Material,
            typeName = r.TypeName,
            label = r.Label,
            @base = r.Base,
            @default = r.Default,
            variants = r.Variants,
            palettes = r.Palettes,
        }),
        layers = item.Layers.ToDictionary(
            l => l.Key,
            l => new { zPos = l.Value.ZPos, customAnimation = l.Value.CustomAnimation, bodyPaths = l.Value.BodyPaths }),
        credits = item.Credits,
    };

    // ── Validation ─────────────────────────────────────────────────────────────────

    private static List<string> ValidateSelections(LpcCatalog cat, string jsonPath)
    {
        var issues = new List<string>();
        var json = File.ReadAllText(jsonPath);
        var payload = JsonSerializer.Deserialize<SelectionsPayload>(json, JsonOpts);
        if (payload == null) { issues.Add("could not parse JSON"); return issues; }
        var sels = payload.Selections;
        string bodyType = payload.BodyType ?? "male";

        if (!sels.ContainsKey("body")) issues.Add("missing 'body' selection (required)");
        if (!sels.ContainsKey("head")) issues.Add("missing 'head' selection (required)");
        if (!sels.ContainsKey("expression")) issues.Add("missing 'expression' selection (required; default = face_neutral)");

        foreach (var (key, sel) in sels)
        {
            if (!cat.HasItem(sel.ItemId))
                issues.Add($"selection '{key}': unknown itemId '{sel.ItemId}'");
            else
            {
                var item = cat.GetItem(sel.ItemId).UnsafeUnwrap();
                if (!item.Lite.Required.Contains(bodyType) &&
                    !(item.Layers.TryGetValue("layer_1", out var l1) && l1.BodyPaths.ContainsKey(bodyType)))
                    issues.Add($"selection '{key}': item '{sel.ItemId}' doesn't support bodyType '{bodyType}'");
            }
        }
        return issues;
    }

    // ── Arg parsing & loading ──────────────────────────────────────────────────────

    private static (LpcCatalog, string) LoadCatalog(Options opts)
    {
        string repoRoot = ResolveRepoRoot();
        string sheetDefs = opts.SheetDefsDir ?? Path.Combine(repoRoot, "sheet_definitions");
        string paletteDefs = opts.PaletteDefsDir ?? Path.Combine(repoRoot, "palette_definitions");
        string spritesheets = opts.SpritesheetsDir ?? Path.Combine(repoRoot, "spritesheets");
        var loaded = new CatalogLoader(sheetDefs, paletteDefs).Load();
        return (new LpcCatalog(loaded), spritesheets);
    }

    private static string ResolveRepoRoot() => Core.Diagnostics.Paths.ResolveRepoRoot();

    private static Options? ParseArgs(string[] args)
    {
        var opts = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Value() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--random": opts.Random = true; break;
                case "--selections": opts.SelectionsPath = Value(); break;
                case "--output": case "-o": opts.Output = Value(); break;
                case "--output-dir": opts.OutputDir = Value(); break;
                case "--count": opts.Count = int.Parse(Value() ?? "1"); break;
                case "--seed": opts.Seed = int.Parse(Value() ?? "0"); break;
                case "--body-type": opts.BodyType = Value() ?? "male"; break;
                case "--list-items": opts.ListItems = true; break;
                case "--dump-catalog": opts.DumpCatalog = true; break;
                case "--describe": opts.Describe = Value(); break;
                case "--manifest": opts.Manifest = Value(); break;
                case "--validate-selections": opts.ValidateSelections = Value(); break;
                case "--format": opts.Format = Value() ?? "png"; break;
                case "--json": opts.Json = true; break;
                case "--indent": opts.Indent = true; break;
                case "--spritesheets": opts.SpritesheetsDir = Value(); break;
                case "--sheet-defs": opts.SheetDefsDir = Value(); break;
                case "--palette-defs": opts.PaletteDefsDir = Value(); break;
                case "--help": case "-h": PrintHelp(); return null;
                default: Console.Error.WriteLine($"unknown flag: {a}"); return null;
            }
        }
        return opts;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"LPC Sprite Generator — headless CLI

Commands (pick one):
  --random                    Generate random character(s)
  --selections <path|->       Render from a selections JSON file (- = stdin)
  --manifest <path>           Batch-render from a manifest JSON
  --list-items                Print all itemIds
  --dump-catalog              Print full catalog as JSON
  --describe <itemId>         Print one item's full metadata as JSON
  --validate-selections <p>   Dry-run validation

Options:
  --output <path>             Output PNG path (single render)
  --output-dir <dir>          Output directory (batch; files named character-NNN.png)
  --count N                   Batch size (default 1)
  --seed N                    Deterministic RNG seed (incremented per item in --count)
  --body-type T               male|female|teen|child|muscular|pregnant (default male)
  --format png                Output format (png only for now)
  --json                      Machine-readable NDJSON status
  --indent                    Pretty-print JSON output
  --spritesheets <dir>        Override spritesheets directory
  --sheet-defs <dir>          Override sheet_definitions directory
  --palette-defs <dir>        Override palette_definitions directory");
    }

    private sealed class Options
    {
        public bool Random;
        public string? SelectionsPath;
        public string? Output;
        public string? OutputDir;
        public int Count = 1;
        public int? Seed;
        public string BodyType = "male";
        public bool ListItems;
        public bool DumpCatalog;
        public string? Describe;
        public string? Manifest;
        public string? ValidateSelections;
        public string Format = "png";
        public bool Json;
        public bool Indent;
        public string? SpritesheetsDir;
        public string? SheetDefsDir;
        public string? PaletteDefsDir;
    }

    private sealed class SelectionsPayload
    {
        // Use Dictionary<string, Selection> directly — System.Text.Json handles it cleanly.
        // (Subclass-of-Dictionary types like Selections have known deserialization quirks.)
        public Dictionary<string, Selection> Selections { get; set; } = new();
        public string? BodyType { get; set; }

        public Selections ToSelections()
        {
            var s = new Selections();
            foreach (var (k, v) in Selections) s[k] = v;
            return s;
        }
    }

    private sealed class ManifestEntry
    {
        public int? Seed { get; set; }
        public string? BodyType { get; set; }
        public Dictionary<string, Selection>? Selections { get; set; }
        public string Output { get; set; } = "";

        public Selections? ToSelections()
        {
            if (Selections == null) return null;
            var s = new Selections();
            foreach (var (k, v) in Selections) s[k] = v;
            return s;
        }
    }
}
