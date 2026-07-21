# LPC → SD LoRA Dataset Converter

`lpc_to_dataset.py` converts Universal LPC Spritesheet output into a training
dataset for Stable Diffusion LoRA (kohya-ss/sd-scripts).

## Why this exists

LPC sheets are 832×3456 grids of 64×64 frames — too large and wrong shape for
SD training, which expects individual images at 512² (SD 1.5) or 768²/1024²
(SDXL). kohya also needs a `.txt` caption sidecar per image. This script does
both in one pass:

1. Splits each sheet into 64×64 frames using the LPC layout table.
2. Upscales to 768² (configurable) with **NEAREST** filtering so the pixel-art
   look is preserved (no blurry interpolation).
3. Drops fully-transparent frames (empty grid cells).
4. Auto-generates a caption per frame: `<spec-prefix>, <anim> pose, facing <dir>, <char-desc>`.
5. Emits a kohya `dataset.toml` so training can start immediately.

## Requirements

```bash
pip install Pillow
```

Pillow is the only dependency. Python 3.10+.

## Usage

```bash
python scripts/lpc_to_dataset.py \
    --input  /path/to/lpc-sheets/*.png \
    --output /path/to/dataset \
    --size   768 \
    --caption-spec "pixel_character_sprite, pxlchrctrsprt, sprite, sprite sheet, sprite art, pixel, (pixel art:1.5), retro game, retro, vibrant colors, pixelated, multiple views, concept art, (chibi:1.5), from side, looking away, from behind, back, white background" \
    --char-desc "1girl adventurer, brown ponytail, leather armor"
```

### Flags

| Flag | Default | Purpose |
|---|---|---|
| `--input` | `./sheets/*.png` | Glob pattern or directory of LPC PNG sheets |
| `--output` | `./dataset` | Dataset output directory |
| `--size` | `768` | Target square size in px (use `512` for SD 1.5, `1024` for SDXL) |
| `--source-size` | `64` | LPC frame size (do not change) |
| `--caption-spec` | **required** | Mandatory spec prefix for every caption |
| `--char-desc` | `""` | Optional character description appended to captions |
| `--min-opacity` | `10` | Skip frames whose max alpha is below this (0-255) |
| `--toml` | `on` | Also emit a kohya `dataset.toml` |

### Caption format

Captions follow the project's spec-prefix convention (see
`dotnet/src/LpcSpriteGen.Core` and the SD build configs). Example for a walk-south
frame of an adventurer character:

```
pixel_character_sprite, pxlchrctrsprt, sprite, sprite sheet, sprite art, pixel,
(pixel art:1.5), retro game, retro, vibrant colors, pixelated, multiple views,
concept art, (chibi:1.5), from side, looking away, from behind, back, white
background, walk pose, facing south, 1girl adventurer, brown ponytail, leather armor
```

The **style** (pixel art look) is deliberately NOT described in the tail — the
LoRA learns it as the residual. The tail only describes **content** (pose,
direction, character).

## Output layout

```
dataset/
├── character-001__walk_south_00.png
├── character-001__walk_south_00.txt
├── character-001__walk_south_01.png
├── character-001__walk_south_01.txt
├── ...
└── dataset.toml              # kohya config, points at this dir
```

Filenames encode `<sheet>__<animation>_<direction>_<frame>`, e.g.
`character-001__walk_south_03.png` = sheet 1, walk animation, facing south, frame 3.

## Next step: train

With kohya-ss/sd-scripts installed:

```bash
sdxl_train_network.py \
    --pretrained_model_name_or_path /path/to/base.safetensors \
    --dataset_config dataset/dataset.toml \
    --output_name my-sprite-lora \
    --network_dim 16 --network_alpha 8 \
    --learning_rate 1e-4 \
    --max_train_epochs 10
```
