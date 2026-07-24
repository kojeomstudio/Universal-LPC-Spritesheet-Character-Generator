#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
LPC Sprite Sheet → SD LoRA Dataset Converter.

Splits Universal-LPC-Spritesheet-Character-Generator output (832×3456 sprite sheets,
64px frames in a 13-col × 54-row grid) into individual frame images suitable for
Stable Diffusion LoRA training with kohya-ss/sd-scripts.

Pipeline:
  1. Split each sheet into 64×64 frames (or a chosen source size).
  2. Upscale to training resolution (768² default) with NEAREST filtering so the
     pixel-art look is preserved (no blurry interpolation).
  3. Drop fully-transparent frames (empty cells in the LPC grid).
  4. Auto-generate a caption (.txt sidecar) per frame from animation + direction +
     optional character description, in the spec-prefix format the SD prompts use.
  5. Emit a kohya dataset TOML so training can start immediately.

Usage:
    python lpc_to_dataset.py \\
        --input  /path/to/lpc-sheets/*.png \\
        --output /path/to/dataset \\
        --size   768 \\
        --caption-spec "pixel_character_sprite, pxlchrctrsprt, sprite, sprite sheet, sprite art, pixel, (pixel art:1.5), retro game, retro, vibrant colors, pixelated, multiple views, concept art, (chibi:1.5), from side, looking away, from behind, back, white background"

CLI flags:
    --input        glob/directory of LPC PNG sheets (default: ./sheets/*.png)
    --output       dataset output directory (default: ./dataset)
    --size         target square size in px (default: 768; use 512 for SD 1.5)
    --source-size  LPC frame size in px (default: 64; do not change for LPC)
    --caption-spec the mandatory spec prefix every caption starts with (see the
                   blue-archive/fantasy config _comment_spec_reference for the canonical
                   baseline). The animation/direction tail is appended automatically.
    --char-desc    optional free-text appended to every caption (e.g. "1girl adventurer")
    --min-opacity  skip frames whose max alpha channel is below this 0-255 (default: 10)
    --toml         also write a kohya dataset.toml (default: on)

Requires Pillow. No other deps.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow is required: pip install Pillow", file=sys.stderr)
    sys.exit(2)


# ---------------------------------------------------------------------------
# LPC layout (mirrors dotnet/src/LpcSpriteGen.Core/Constants/AnimationConfig.cs)
# ---------------------------------------------------------------------------
# Each animation occupies `num` consecutive rows of 4 directions (N, W, S, E),
# at a known Y offset (in rows). Frames per row = 13 (LPC standard).
FRAMES_PER_ROW = 13

# (anim_name, start_row, num_directions)
LPC_ANIMATIONS = [
    ("spellcast",    0, 4),
    ("thrust",       4, 4),
    ("walk",         8, 4),
    ("slash",       12, 4),
    ("shoot",       16, 4),
    ("hurt",        20, 1),  # single row
    ("climb",       21, 1),
    ("idle",        22, 4),
    ("jump",        26, 4),
    ("sit",         30, 4),
    ("emote",       34, 4),
    ("run",         38, 4),
    ("combat_idle", 42, 4),
    ("backslash",   46, 4),
    ("halfslash",   50, 4),
]

# LPC direction order within an animation's 4 rows: N, W, S, E
DIRECTION_NAMES = ["north", "west", "south", "east"]


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Convert LPC sprite sheets into an SD LoRA training dataset."
    )
    p.add_argument("--input", default="./sheets/*.png",
                   help="glob pattern or directory of LPC PNG sheets")
    p.add_argument("--output", default="./dataset",
                   help="dataset output directory")
    p.add_argument("--size", type=int, default=768,
                   help="target square size in px (default 768; 512 for SD 1.5)")
    p.add_argument("--source-size", type=int, default=64,
                   help="LPC frame size in px (default 64; do not change)")
    p.add_argument("--caption-spec", required=True,
                   help="mandatory spec prefix for every caption (the canonical baseline)")
    p.add_argument("--char-desc", default="",
                   help="optional character description appended to every caption "
                        "(e.g. '1girl adventurer, brown ponytail, leather armor')")
    p.add_argument("--min-opacity", type=int, default=10,
                   help="skip frames whose max alpha is below this (0-255, default 10)")
    p.add_argument("--concept", default="lpc_sprite",
                   help="concept name for the Dreambooth subfolder (default lpc_sprite). "
                        "Frames are written to <output>/<repeats>_<concept>/")
    p.add_argument("--num-repeats", type=int, default=4,
                   help="image repeats encoded in the subfolder name (default 4)")
    p.add_argument("--toml", choices=["on", "off"], default="on",
                   help="also emit a kohya dataset.toml (default on)")
    return p.parse_args()


def resolve_inputs(pattern: str) -> list[Path]:
    """Expand a glob pattern or directory into a list of PNG paths."""
    p = Path(pattern)
    if p.is_dir():
        return sorted(p.glob("*.png"))
    # Treat as glob — use parent.glob to handle patterns with wildcards.
    parent = p.parent if p.parent.exists() else Path(".")
    return sorted(parent.glob(p.name))


def split_sheet(sheet: Path, source_size: int) -> list[tuple[str, Image.Image]]:
    """Split one LPC sheet into labeled frames.

    Returns a list of (label, frame_image) where label encodes animation + direction
    + frame index, e.g. 'walk_south_03'. Frames outside known animation rows are
    skipped (the 832×3456 grid has empty/extension areas).
    """
    img = Image.open(sheet).convert("RGBA")
    if img.width % source_size != 0 or img.height % source_size != 0:
        print(f"  WARN: {sheet.name} size {img.size} not divisible by source_size "
              f"{source_size}; skipping", file=sys.stderr)
        return []

    frames: list[tuple[str, Image.Image]] = []
    sheet_stem = sheet.stem  # e.g. character-001

    for anim_name, start_row, num_dirs in LPC_ANIMATIONS:
        for dir_idx in range(num_dirs):
            row = start_row + dir_idx
            y = row * source_size
            if y + source_size > img.height:
                break
            dir_name = DIRECTION_NAMES[dir_idx] if num_dirs == 4 else "all"
            for col in range(FRAMES_PER_ROW):
                x = col * source_size
                if x + source_size > img.width:
                    break
                box = (x, y, x + source_size, y + source_size)
                frame = img.crop(box)
                label = f"{sheet_stem}__{anim_name}_{dir_name}_{col:02d}"
                frames.append((label, frame))
    return frames


def has_content(frame: Image.Image, min_opacity: int) -> bool:
    """True if the frame has at least one pixel whose alpha >= min_opacity."""
    alpha = frame.getchannel("A")
    extrema = alpha.getextrema()
    return extrema[1] >= min_opacity


def upscale_nearest(frame: Image.Image, size: int) -> Image.Image:
    """Upscale to (size, size) with NEAREST filtering to preserve pixel-art crispness."""
    return frame.resize((size, size), Image.NEAREST)


def build_caption(spec: str, anim_name: str, dir_name: str, char_desc: str) -> str:
    """Compose a caption in the project's spec-prefix format.

    The spec is the mandatory baseline (from --caption-spec). We append an
    animation/direction tail so the LoRA can learn pose+direction conditioning.
    The character description (if any) is appended last.
    """
    parts = [spec.rstrip().rstrip(",")]
    # Animation + direction go into the caption as content descriptors; the style
    # (pixel art look) stays in the LoRA's residual by NOT being mentioned here.
    parts.append(f"{anim_name} pose")
    if dir_name != "all":
        parts.append(f"facing {dir_name}")
    if char_desc.strip():
        parts.append(char_desc.strip().rstrip(","))
    return ", ".join(parts)


def write_toml(output_dir: Path, size: int, concept_dir: Path, num_repeats: int = 4) -> Path:
    """Emit a kohya-ss dataset TOML pointing at the generated frames.

    Uses the Dreambooth layout: images live under <concept_dir> named
    '<repeats>_<concept>' (e.g. '4_lpc_sprite'). This is what kohya GUI's
    Dreambooth method validates against — a flat folder of images next to a
    dataset.toml triggers the 'subfolders do not match <repeats>_<name>' error.
    """
    toml_path = output_dir / "dataset.toml"
    content = f"""# kohya-ss / sd-scripts dataset config (Dreambooth layout).
# Images live in {concept_dir.name} (repeats={num_repeats}).
# Docs: https://github.com/kohya-ss/sd-scripts/blob/main/docs/config_README-en.md

[general]
shuffle_caption = false
caption_extension = ".txt"
keep_tokens = 1        # protect the spec prefix tokens from shuffling

[[datasets]]
resolution = {size}
batch_size = 1

  [[datasets.subsets]]
  image_dir = "{concept_dir.resolve()}"
  num_repeats = {num_repeats}
"""
    toml_path.write_text(content, encoding="utf-8")
    return toml_path


def main() -> int:
    args = parse_args()

    inputs = resolve_inputs(args.input)
    if not inputs:
        print(f"No input PNGs matched '{args.input}'", file=sys.stderr)
        return 1

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    # Dreambooth layout: images go into <output>/<repeats>_<concept>/ so kohya's
    # Dreambooth method validation passes (it requires subfolders named
    # <repeats>_<name>, NOT a flat folder of images next to dataset.toml).
    concept_name = args.concept
    concept_dir = output_dir / f"{args.num_repeats}_{concept_name}"
    concept_dir.mkdir(parents=True, exist_ok=True)

    spec = args.caption_spec
    char_desc = args.char_desc
    total_written = 0
    total_skipped = 0

    for sheet in inputs:
        print(f"Processing {sheet.name}...")
        frames = split_sheet(sheet, args.source_size)
        for label, frame in frames:
            if not has_content(frame, args.min_opacity):
                total_skipped += 1
                continue
            big = upscale_nearest(frame, args.size)

            img_path = concept_dir / f"{label}.png"
            big.save(img_path, format="PNG")

            anim, dir_name = _decode_label(label)
            caption = build_caption(spec, anim, dir_name, char_desc)
            (concept_dir / f"{label}.txt").write_text(caption, encoding="utf-8")
            total_written += 1

    print(f"\nDone. Wrote {total_written} frames, skipped {total_skipped} empty.")
    print(f"Output: {output_dir}")

    if args.toml == "on":
        toml = write_toml(output_dir, args.size, concept_dir, args.num_repeats)
        print(f"kohya TOML: {toml}")

    return 0


def _decode_label(label: str) -> tuple[str, str]:
    """Extract (anim_name, direction) from a label like 'character-001__walk_south_03'."""
    tail = label.split("__", 1)[1] if "__" in label else label
    parts = tail.split("_")
    # parts = [anim, dir, col] OR [anim, col] for single-dir anims (dir='all')
    if len(parts) >= 3:
        return parts[0], parts[1]
    if len(parts) == 2:
        return parts[0], "all"
    return tail, "all"


if __name__ == "__main__":
    sys.exit(main())
