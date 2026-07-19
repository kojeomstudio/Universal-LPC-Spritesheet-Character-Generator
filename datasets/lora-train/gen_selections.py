"""LoRA 학습용 30개 캐릭터 selections JSON 생성기.

직업 아키타입 6종 × 변형 + 몬스터 6종 = 30개.
각 selections JSON은 LpcSpriteGen.Headless --validate-selections 를 통과해야 함
(body/head/expression 필수).
"""
import json
import os
import random

OUT_DIR = os.path.join(os.path.dirname(__file__), 'selections')
os.makedirs(OUT_DIR, exist_ok=True)

# 직업별 장비 템플릿. 빈 문자열이면 해당 슬롯 생략.
# 각 템플릿은 (bodyType, gender_tag, class_name, slots_dict) 를 반환.
HAIR_COLORS = ['ulpc.blonde', 'ulpc.brown', 'ulpc.black', 'ulpc.red']
HAIR_LABEL = {'ulpc.blonde': 'blonde', 'ulpc.brown': 'brown',
              'ulpc.black': 'black', 'ulpc.red': 'red'}
SKIN_TONES = ['light', 'olive', 'tan', 'amber']
SKIN_LABEL = {'light': 'light skin', 'olive': 'olive skin',
              'tan': 'tan skin', 'amber': 'amber skin'}

# === 직업 템플릿 ===
# 슬롯 키: body, head, expression, ears, hair, beard, torso/clothes, legs, feet/shoes,
#         arms, shoulders, hat, cape, shield, weapon, belt
# 각 값은 (itemId, recolor or None)


def knight(hair_recolor, torso_recolor, body_type, skin):
    gender_tag = '1boy' if body_type in ('male', 'muscular', 'teen') else '1girl'
    head_id = 'heads_human_male' if gender_tag == '1boy' else 'heads_human_female'
    return {
        'bodyType': body_type,
        'class': 'knight',
        'gender_tag': gender_tag,
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': head_id,                   'recolor': skin},
            'expression': {'itemId': 'face_neutral',            'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_page',               'recolor': hair_recolor},
            'clothes':    {'itemId': 'torso_armour_plate',      'recolor': f'metal.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_armour',             'recolor': f'metal.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_armour',             'recolor': f'metal.ulpc.{torso_recolor}'},
            'shoulders':  {'itemId': 'shoulders_pauldrons',     'recolor': f'metal.ulpc.{torso_recolor}'},
            'hat':        {'itemId': 'hat_helmet_greathelm',    'recolor': f'metal.ulpc.{torso_recolor}'},
            'cape':       {'itemId': 'cape_solid',              'recolor': 'cloth.ulpc.red'},
            'weapon':     {'itemId': 'weapon_sword_longsword',  'recolor': 'metal.ulpc.steel'},
            'arms':       {'itemId': 'arms_armour',             'recolor': f'metal.ulpc.{torso_recolor}'},
        },
    }


def mage(hair_recolor, torso_recolor, body_type, skin):
    gender_tag = '1boy' if body_type in ('male', 'muscular', 'teen') else '1girl'
    head_id = 'heads_human_male' if gender_tag == '1boy' else 'heads_human_female'
    # male은 robe 미지원 → frock jacket으로 로브 느낌. female은 robe 그대로.
    if gender_tag == '1boy':
        torso_item = 'torso_jacket_frock'
        belt_item = 'belt_robe'
    else:
        torso_item = 'torso_clothes_robe'
        belt_item = 'belt_robe'
    return {
        'bodyType': body_type,
        'class': 'mage',
        'gender_tag': gender_tag,
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': head_id,                   'recolor': skin},
            'expression': {'itemId': 'face_neutral',            'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_long',               'recolor': hair_recolor},
            'clothes':    {'itemId': torso_item,                'recolor': f'cloth.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_pants',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.brown'},
            'hat':        {'itemId': 'hat_magic_wizard',        'recolor': f'cloth.ulpc.{torso_recolor}'},
            'belt':       {'itemId': belt_item,                 'recolor': 'cloth.ulpc.brown'},
            'weapon':     {'itemId': 'weapon_magic_gnarled',    'recolor': 'wood.ulpc.oak'},
        },
    }


def archer(hair_recolor, torso_recolor, body_type, skin):
    gender_tag = '1boy' if body_type in ('male', 'muscular', 'teen') else '1girl'
    head_id = 'heads_human_male' if gender_tag == '1boy' else 'heads_human_female'
    return {
        'bodyType': body_type,
        'class': 'archer',
        'gender_tag': gender_tag,
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': head_id,                   'recolor': skin},
            'expression': {'itemId': 'face_neutral',            'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_ponytail',           'recolor': hair_recolor},
            'clothes':    {'itemId': 'torso_armour_leather',    'recolor': f'cloth.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_pants',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.brown'},
            'hat':        {'itemId': 'hat_hood_cloth',          'recolor': f'cloth.ulpc.{torso_recolor}'},
            'weapon':     {'itemId': 'weapon_ranged_bow_recurve','recolor': 'wood.ulpc.oak'},
            'quiver':     {'itemId': 'quiver',                  'recolor': 'cloth.ulpc.brown'},
        },
    }


def rogue(hair_recolor, torso_recolor, body_type, skin):
    gender_tag = '1boy' if body_type in ('male', 'muscular', 'teen') else '1girl'
    head_id = 'heads_human_male' if gender_tag == '1boy' else 'heads_human_female'
    return {
        'bodyType': body_type,
        'class': 'rogue',
        'gender_tag': gender_tag,
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': head_id,                   'recolor': skin},
            'expression': {'itemId': 'face_neutral',            'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_bangs',              'recolor': hair_recolor},
            'clothes':    {'itemId': 'torso_clothes_longsleeve','recolor': f'cloth.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_pants',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.black'},
            'cape':       {'itemId': 'cape_solid',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'hat':        {'itemId': 'hat_hood_cloth',          'recolor': f'cloth.ulpc.{torso_recolor}'},
            'weapon':     {'itemId': 'weapon_sword_dagger',     'recolor': 'metal.ulpc.steel'},
        },
    }


def cleric(hair_recolor, torso_recolor, body_type, skin):
    gender_tag = '1boy' if body_type in ('male', 'muscular', 'teen') else '1girl'
    head_id = 'heads_human_male' if gender_tag == '1boy' else 'heads_human_female'
    # male은 robe 미지원 → frock jacket. female은 robe.
    torso_item = 'torso_jacket_frock' if gender_tag == '1boy' else 'torso_clothes_robe'
    return {
        'bodyType': body_type,
        'class': 'cleric',
        'gender_tag': gender_tag,
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': head_id,                   'recolor': skin},
            'expression': {'itemId': 'face_neutral',            'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_bob',                'recolor': hair_recolor},
            'clothes':    {'itemId': torso_item,                'recolor': f'cloth.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_pants',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_shoes_basic',        'recolor': 'cloth.ulpc.brown'},
            'hat':        {'itemId': 'hat_hood_cloth',          'recolor': f'cloth.ulpc.{torso_recolor}'},
            'weapon':     {'itemId': 'weapon_polearm_cane',     'recolor': 'wood.ulpc.oak'},
            'belt':       {'itemId': 'belt_robe',               'recolor': f'cloth.ulpc.{torso_recolor}'},
        },
    }


def barbarian(hair_recolor, torso_recolor, body_type, skin):
    # muscular는 clothes/legs 지원이 거의 없어 male로 사용.
    # hair+beard+야만인 helmet+대형 무기로 아치타입 표현.
    body_type = 'male'
    return {
        'bodyType': body_type,
        'class': 'barbarian',
        'gender_tag': '1boy',
        'skin_label': SKIN_LABEL[skin],
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': skin},
            'head':       {'itemId': 'heads_human_male',        'recolor': skin},
            'expression': {'itemId': 'face_angry',              'recolor': skin},
            'ears':       {'itemId': 'head_ears_medium'},
            'hair':       {'itemId': 'hair_long',               'recolor': hair_recolor},
            'beard':      {'itemId': 'beards_beard',            'recolor': hair_recolor},
            'clothes':    {'itemId': 'torso_clothes_sleeveless','recolor': f'cloth.ulpc.{torso_recolor}'},
            'legs':       {'itemId': 'legs_pants',              'recolor': f'cloth.ulpc.{torso_recolor}'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.brown'},
            'hat':        {'itemId': 'hat_helmet_barbarian',    'recolor': 'metal.ulpc.iron'},
            'shoulders':  {'itemId': 'shoulders_pauldrons',     'recolor': 'metal.ulpc.iron'},
            'weapon':     {'itemId': 'weapon_blunt_waraxe',     'recolor': 'metal.ulpc.iron'},
        },
    }


# === 몬스터 템플릿 ===
def zombie(skin_tone):
    return {
        'bodyType': 'male',
        'class': 'zombie',
        'gender_tag': 'monster',
        'skin_label': 'zombie green skin',
        'selections': {
            'body':       {'itemId': 'body_zombie',             'recolor': 'green'},
            'head':       {'itemId': 'heads_zombie',            'recolor': 'green'},
            'expression': {'itemId': 'face_angry',              'recolor': 'green'},
            'ears':       {'itemId': 'head_ears_big'},
            'hair':       {'itemId': 'hair_unkempt',            'recolor': 'ulpc.black'},
            'clothes':    {'itemId': 'torso_clothes_tshirt',    'recolor': f'cloth.ulpc.{skin_tone}'},
            'legs':       {'itemId': 'legs_shorts',             'recolor': 'cloth.ulpc.brown'},
            'shoes':      {'itemId': 'feet_armour',             'recolor': 'metal.ulpc.bronze'},
            'beard':      {'itemId': 'beards_5oclock_shadow',   'recolor': 'ulpc.black'},
        },
    }


def skeleton():
    return {
        'bodyType': 'male',
        'class': 'skeleton',
        'gender_tag': 'monster',
        'skin_label': 'bone',
        'selections': {
            'body':       {'itemId': 'body_skeleton'},
            'head':       {'itemId': 'heads_skeleton'},
            'expression': {'itemId': 'face_neutral'},
            'clothes':    {'itemId': 'torso_clothes_longsleeve','recolor': 'cloth.ulpc.navy'},
            'legs':       {'itemId': 'legs_pants',              'recolor': 'cloth.ulpc.navy'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.brown'},
            'weapon':     {'itemId': 'weapon_sword_longsword',  'recolor': 'metal.ulpc.steel'},
            'hat':        {'itemId': 'hat_helmet_barbuta',      'recolor': 'metal.ulpc.iron'},
        },
    }


def orc():
    return {
        'bodyType': 'male',
        'class': 'orc',
        'gender_tag': 'monster',
        'skin_label': 'orc green skin',
        'selections': {
            'body':       {'itemId': 'body',                    'recolor': 'dark_green'},
            'head':       {'itemId': 'heads_orc_male',          'recolor': 'dark_green'},
            'expression': {'itemId': 'face_angry',              'recolor': 'dark_green'},
            'ears':       {'itemId': 'head_ears_big'},
            'hair':       {'itemId': 'hair_ponytail',           'recolor': 'ulpc.black'},
            'clothes':    {'itemId': 'torso_armour_leather',    'recolor': 'cloth.ulpc.brown'},
            'legs':       {'itemId': 'legs_pants',              'recolor': 'cloth.ulpc.brown'},
            'shoes':      {'itemId': 'feet_boots_basic',        'recolor': 'cloth.ulpc.black'},
            'shoulders':  {'itemId': 'shoulders_pauldrons',     'recolor': 'metal.ulpc.bronze'},
            'weapon':     {'itemId': 'weapon_blunt_mace',       'recolor': 'metal.ulpc.iron'},
        },
    }


# === 30개 캐릭터 생성 계획 ===
# 직업 × 변형 조합 설계 (24 인간 + 6 몬스터)
PLAN = []

# Knight 변형 4 (남2/여2, 다른 hair/armor 색)
knight_variants = [
    ('male',   'ulpc.blonde', 'steel',  'light'),
    ('male',   'ulpc.brown',  'iron',   'tan'),
    ('female', 'ulpc.red',    'steel',  'light'),
    ('female', 'ulpc.black',  'bronze', 'olive'),
]
# Mage 4
mage_variants = [
    ('female', 'ulpc.blonde', 'purple', 'light'),
    ('male',   'ulpc.black',  'blue',   'light'),
    ('female', 'ulpc.red',    'red',    'olive'),
    ('male',   'ulpc.brown',  'navy',   'tan'),
]
# Archer 4
archer_variants = [
    ('male',   'ulpc.brown',  'forest', 'tan'),
    ('female', 'ulpc.red',    'leather','light'),
    ('male',   'ulpc.black',  'brown',  'olive'),
    ('female', 'ulpc.blonde', 'green',  'light'),
]
# Rogue 4
rogue_variants = [
    ('male',   'ulpc.black',  'navy',   'light'),
    ('female', 'ulpc.brown',  'black',  'olive'),
    ('male',   'ulpc.red',    'maroon', 'tan'),
    ('female', 'ulpc.black',  'teal',   'light'),
]
# Cleric 4
cleric_variants = [
    ('female', 'ulpc.blonde', 'white',  'light'),
    ('male',   'ulpc.brown',  'lavender','light'),
    ('female', 'ulpc.red',    'rose',   'olive'),
    ('male',   'ulpc.black',  'yellow', 'tan'),
]
# Barbarian 4 (항상 muscular male)
barbarian_variants = [
    ('muscular', 'ulpc.red',    'brown',  'tan'),
    ('muscular', 'ulpc.black',  'maroon', 'olive'),
    ('muscular', 'ulpc.brown',  'forest', 'tan'),
    ('muscular', 'ulpc.blonde', 'tan',    'amber'),
]

# 24개 인간 캐릭터 등록
template_variants = [
    ('knight',    knight,    knight_variants),
    ('mage',      mage,      mage_variants),
    ('archer',    archer,    archer_variants),
    ('rogue',     rogue,     rogue_variants),
    ('cleric',    cleric,    cleric_variants),
    ('barbarian', barbarian, barbarian_variants),
]

for class_name, builder, variants in template_variants:
    for i, (body_type, hair, torso, skin) in enumerate(variants, 1):
        char_id = f'{class_name}-{i:02d}'
        spec = builder(hair, torso, body_type, skin)
        spec['hair_label'] = HAIR_LABEL.get(hair, hair)
        spec['gear_color_label'] = torso
        PLAN.append((char_id, spec))

# 6개 몬스터
for i, (skin_tone) in enumerate(['brown', 'red', 'navy'], 1):
    PLAN.append((f'zombie-{i:02d}', zombie(skin_tone)))
for i in range(1, 3):
    PLAN.append((f'skeleton-{i:02d}', skeleton()))
for i in range(1, 3):
    PLAN.append((f'orc-{i:02d}', orc()))


# === JSON 파일로 저장 ===
print(f'총 캐릭터 수: {len(PLAN)}')
print()
for char_id, spec in PLAN:
    # selections JSON 본문 (bodyType + selections만)
    payload = {
        'bodyType': spec['bodyType'],
        'selections': spec['selections'],
    }
    out_path = os.path.join(OUT_DIR, f'{char_id}.json')
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)
    print(f'  {char_id:18} {spec["gender_tag"]:8} {spec["class"]:10} {spec["skin_label"]}')

# 메타데이터 (캡션 생성용) 별도 저장
meta_path = os.path.join(os.path.dirname(__file__), 'char_meta.json')
with open(meta_path, 'w', encoding='utf-8') as f:
    json.dump({cid: {k: v for k, v in spec.items() if k != 'selections'}
               for cid, spec in PLAN}, f, indent=2, ensure_ascii=False)
print(f'\n메타데이터: {meta_path}')
