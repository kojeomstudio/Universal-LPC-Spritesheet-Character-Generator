// ZIP exporter — packages a rendered sprite sheet into per-animation / per-item / per-frame
// archives. Port of sources/state/zip.ts (the structural layout; JS uses JSZip, C# uses
// System.IO.Compression.ZipArchive).
//
// Layouts:
//   ByAnimation       : walk.png, idle.png, ...            (one PNG per animation, full 4-direction strip)
//   ByItem            : body/walk.png, head/walk.png, ...  (one PNG per item × animation)
//   ByFrame           : walk/0001.png, walk/0002.png, ...  (one PNG per frame across all anims)
//
// Image backend: SkiaSharp (MIT). Frame extraction uses SKBitmap.ExtractSubset (replaces
// System.Drawing Bitmap.Clone(rect, format)); PNG encoding via SKImage.Encode.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.Rendering;
using SkiaSharp;

namespace LpcSpriteGen.Core.Zip;

public enum ZipLayout { ByAnimation, ByItem, ByFrame }

public sealed class ZipExporter
{
    private readonly LpcCatalog _catalog;
    private readonly Renderer _renderer;

    public ZipExporter(LpcCatalog catalog, Renderer renderer)
    {
        _catalog = catalog;
        _renderer = renderer;
    }

    /// <summary>
    /// Build a ZIP at <paramref name="outputPath"/> for the given selections/bodyType.
    /// </summary>
    public async Task ExportAsync(Selections selections, string bodyType, ZipLayout layout, string outputPath)
    {
        // Ensure dir exists; truncate any existing file so the new archive starts clean.
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            switch (layout)
            {
                case ZipLayout.ByAnimation: await ExportByAnimationAsync(zip, selections, bodyType); break;
                case ZipLayout.ByItem: await ExportByItemAsync(zip, selections, bodyType); break;
                case ZipLayout.ByFrame: await ExportByFrameAsync(zip, selections, bodyType); break;
            }
        } // dispose writes the central directory
    }

    private async Task ExportByAnimationAsync(ZipArchive zip, Selections selections, string bodyType)
    {
        // Render the full sheet once, then split each animation block into a separate PNG entry.
        using var sheet = await _renderer.RenderCharacterAsync(selections, bodyType);
        foreach (var (anim, yOffset) in AnimationTables.AnimationOffsets)
        {
            var cfg = AnimationTables.AnimationConfigs.FirstOrDefault(c => c.Key == anim).Value
                      ?? new AnimationConfig(yOffset / LpcConstants.FrameSize, 4, new[] { 0 });
            int num = cfg.Num;
            var rect = SKRectI.Create(0, yOffset, LpcConstants.SheetWidth, num * LpcConstants.FrameSize);
            if (rect.Bottom > sheet.Height) continue;
            using var animSheet = ExtractSubset(sheet, rect);
            AddPngEntry(zip, $"{anim}.png", animSheet);
        }
    }

    private async Task ExportByItemAsync(ZipArchive zip, Selections selections, string bodyType)
    {
        // One PNG per (item × animation). Re-render per item to isolate its contribution.
        foreach (var (typeName, sel) in selections)
        {
            var itemR = _catalog.GetItem(sel.ItemId);
            if (!itemR.IsOk) continue;
            // Render just this item.
            var single = new Selections { [typeName] = sel };
            try
            {
                using var sheet = await _renderer.RenderCharacterAsync(single, bodyType);
                foreach (var (anim, yOffset) in AnimationTables.AnimationOffsets)
                {
                    var cfg = AnimationTables.AnimationConfigs.FirstOrDefault(c => c.Key == anim).Value
                              ?? new AnimationConfig(yOffset / LpcConstants.FrameSize, 4, new[] { 0 });
                    var rect = SKRectI.Create(0, yOffset, LpcConstants.SheetWidth, cfg.Num * LpcConstants.FrameSize);
                    if (rect.Bottom > sheet.Height) continue;
                    using var animSheet = ExtractSubset(sheet, rect);
                    AddPngEntry(zip, $"{sel.ItemId}/{anim}.png", animSheet);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[zip-by-item] skipping {sel.ItemId}: {e.Message}");
            }
        }
    }

    private async Task ExportByFrameAsync(ZipArchive zip, Selections selections, string bodyType)
    {
        // One PNG per frame across all animations.
        using var sheet = await _renderer.RenderCharacterAsync(selections, bodyType);
        foreach (var (anim, yOffset) in AnimationTables.AnimationOffsets)
        {
            var cfg = AnimationTables.AnimationConfigs.FirstOrDefault(c => c.Key == anim).Value;
            if (cfg == null) continue;
            int num = cfg.Num;
            for (int dir = 0; dir < num; dir++)
            {
                for (int col = 0; col < LpcConstants.StandardAnimationFramesPerRow; col++)
                {
                    int x = col * LpcConstants.FrameSize;
                    int y = yOffset + dir * LpcConstants.FrameSize;
                    var rect = SKRectI.Create(x, y, LpcConstants.FrameSize, LpcConstants.FrameSize);
                    if (rect.Bottom > sheet.Height || rect.Right > sheet.Width) continue;
                    using var frame = ExtractSubset(sheet, rect);
                    AddPngEntry(zip, $"{anim}/{anim}_d{dir}_f{col:D2}.png", frame);
                }
            }
        }
    }

    /// <summary>
    /// Extract a sub-rectangle from a source SKBitmap as a new disposable SKBitmap.
    /// Wraps SKBitmap.ExtractSubset (instance method: writes into a provided destination)
    /// so callers can use a `using` pattern like the old Bitmap.Clone(rect, format).
    /// </summary>
    private static SKBitmap ExtractSubset(SKBitmap src, SKRectI rect)
    {
        var dst = new SKBitmap(rect.Width, rect.Height, src.ColorType, src.AlphaType);
        if (!src.ExtractSubset(dst, rect))
        {
            dst.Dispose();
            throw new InvalidOperationException($"ExtractSubset failed for rect {rect}");
        }
        return dst;
    }

    private static void AddPngEntry(ZipArchive zip, string entryName, SKBitmap bmp)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var image = SKImage.FromBitmap(bmp);
        // Encode to PNG; SKEncodedImageFormat.Png is lossless and matches the old ImageFormat.Png.
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }
}
