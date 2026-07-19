"""LoRA train셋 후처리기.

sheets/<char_id>.zip 의 walk/idle 프레임만 추출 →
64×64 PNG를 8배 nearest-neighbor 업스케일 → 512×512 검정 배경 flatten →
train/<char_id>_<anim>_d<dir>_f<NN>.png + 같은이름.txt(태그 캡션) 저장.

캡션 형식 (WD14/Danbooru 태그 호환):
  pxlart sprite, <gender>, <class>, <gear>, <action>, facing <dir>, <hair> hair

LPC 방향 순서: d0=up(n), d1=left(w), d2=down(s), d3=right(e).
"""
import io
import json
import os
import zipfile

from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
SHEETS = os.path.join(BASE, 'sheets')
TRAIN = os.path.join(BASE, 'train')
os.makedirs(TRAIN, exist_ok=True)

# 캡션 메타데이터 (gen_selections.py가 생성한 char_meta.json)
with open(os.path.join(BASE, 'char_meta.json'), encoding='utf-8') as f:
    META = json.load(f)

# LPC 애니메이션 d 인덱스 → 인간 친화적 방향명
# LPC는 walk/idle이 4방향. d0=up, d1=left, d2=down, d3=right.
DIR_NAMES = {0: 'up', 1: 'left', 2: 'down', 3: 'right'}

# 애니메이션별 액션 태그
ACTION_TAGS = {'walk': 'walking', 'idle': 'idle'}

# 직업별 장비 태그 (캡션 품질 향상)
CLASS_GEAR = {
    'knight':    'plate armor, steel sword',
    'mage':      'wizard robe, magic staff',
    'archer':    'leather armor, bow',
    'rogue':     'clothes, dagger',
    'cleric':    'robe, cane',
    'barbarian': 'sleeveless, waraxe',
    'zombie':    'tattered clothes',
    'skeleton':  'bones, longsword',
    'orc':       'leather armor, mace',
}

# 업스케일 배율 (64 → 512)
SCALE = 8
BG_COLOR = (0, 0, 0)  # 검정 배경


def flatten_on_bg(img: Image.Image) -> Image.Image:
    """알파 채널을 검정 배경에 합성. 반전 알파를 마스크로 사용."""
    if img.mode != 'RGBA':
        img = img.convert('RGBA')
    bg = Image.new('RGBA', img.size, BG_COLOR + (255,))
    return Image.alpha_composite(bg, img).convert('RGB')


def upscale_nn(img: Image.Image, scale: int) -> Image.Image:
    """Nearest-neighbor 업스케일. 픽셀 하드 엣지 보존."""
    w, h = img.size
    return img.resize((w * scale, h * scale), Image.NEAREST)


def build_caption(char_id: str, anim: str, dir_idx: int) -> str:
    """캐릭터/애니메이션/방향 → 태그 캡션 문자열."""
    meta = META.get(char_id, {})
    gender = meta.get('gender_tag', '1boy')
    cls = meta.get('class', 'character')
    gear = CLASS_GEAR.get(cls, '')
    action = ACTION_TAGS.get(anim, anim)
    direction = DIR_NAMES.get(dir_idx, f'dir{dir_idx}')
    hair = meta.get('hair_label', '')
    skin = meta.get('skin_label', '')

    tags = ['pxlart sprite']
    # 몬스터는 gender 대신 몬스터 태그
    if gender == 'monster':
        tags.append('monster')
    else:
        tags.append(gender)
    tags.append(cls)
    if gear:
        tags.append(gear)
    tags.append(action)
    tags.append('facing ' + direction)
    if hair and gender != 'monster':
        tags.append(hair + ' hair')
    if skin:
        tags.append(skin)
    return ', '.join(tags)


def process_zip(zip_path: str) -> int:
    """zip 하나에서 walk/idle 프레임 추출 후 train/에 저장. 처리된 프레임 수 반환."""
    char_id = os.path.basename(zip_path)[:-4]  # .zip 제거
    count = 0
    with zipfile.ZipFile(zip_path) as z:
        for entry in z.namelist():
            # walk/walk_d0_f00.png 형태
            parts = entry.split('/')
            if len(parts) != 2:
                continue
            anim = parts[0]
            if anim not in ('walk', 'idle'):
                continue
            fname = parts[1]  # walk_d0_f00.png
            if not fname.endswith('.png'):
                continue
            # walk_d0_f00 → anim=walk, dir=0, frame=00
            stem = fname[:-4]
            try:
                tokens = stem.split('_')
                # ['walk', 'd0', 'f00']
                dir_idx = int(tokens[1][1:])  # 'd0' → 0
                frame_str = tokens[2][1:]  # 'f00' → '00'
            except (IndexError, ValueError):
                continue
            if dir_idx not in DIR_NAMES:
                continue

            # zip에서 PNG 읽기
            with z.open(entry) as fp:
                img = Image.open(io.BytesIO(fp.read()))
                img.load()
            # 64×64 검정 배경 합성 후 8× 업스케일
            flattened = flatten_on_bg(img)
            upscaled = upscale_nn(flattened, SCALE)

            out_name = f'{char_id}_{anim}_d{dir_idx}_f{frame_str}.png'
            out_path = os.path.join(TRAIN, out_name)
            upscaled.save(out_path, 'PNG')

            # 캡션 .txt
            caption = build_caption(char_id, anim, dir_idx)
            caption_path = os.path.join(TRAIN, out_name[:-4] + '.txt')
            with open(caption_path, 'w', encoding='utf-8') as cf:
                cf.write(caption + '\n')

            count += 1
    return count


def main():
    total = 0
    per_char = {}
    for fn in sorted(os.listdir(SHEETS)):
        if not fn.endswith('.zip'):
            continue
        char_id = fn[:-4]
        n = process_zip(os.path.join(SHEETS, fn))
        per_char[char_id] = n
        total += n

    print(f'처리 완료: {len(per_char)} 캐릭터, 총 {total} 프레임')
    print(f'출력 디렉토리: {TRAIN}')
    print()
    print('캐릭터별 프레임 수:')
    for cid, n in sorted(per_char.items()):
        print(f'  {cid:18} {n}')

    # 검증: PNG와 TXT 쌍 일치
    pngs = {f[:-4] for f in os.listdir(TRAIN) if f.endswith('.png')}
    txts = {f[:-4] for f in os.listdir(TRAIN) if f.endswith('.txt')}
    only_png = pngs - txts
    only_txt = txts - pngs
    print()
    print(f'PNG 수: {len(pngs)}, TXT 수: {len(txts)}')
    if only_png:
        print(f'PNG만 있음 (캡션 누락): {len(only_png)}')
    if only_txt:
        print(f'TXT만 있음 (이미지 누락): {len(only_txt)}')
    if not only_png and not only_txt:
        print('PNG/TXT 쌍 완전 일치 OK')


if __name__ == '__main__':
    main()
