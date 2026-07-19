# LoRA Training Dataset — LPC Sprite Frames

LPC Sprite Generator로 생성한 2,024년 픽셀아트 캐릭터 스프라이트를 **LoRA 파인튜닝 학습 입력**으로 가공한 데이터셋. Stable Diffusion 1.5 + Kohya_ss 워크플로 기준.

> **규모**: 31 캐릭터 × 104 프레임 = **3,224 PNG + 3,224 TXT 캡션 쌍**
> **해상도**: 512×512 (원본 64×64 → 8× nearest-neighbor 업스케일)
> **배경**: 검정(`#000000`) — alpha flatten
> **캡션**: WD14/Danbooru 태그 호환 `.txt` (같은 이름)

---

## 디렉토리 구조

```
datasets/lora-train/
├─ README.md                  # 이 파일
├─ gen_selections.py          # 31 캐릭터 selections JSON 생성 스크립트
├─ gen_manifest.py            # selections → manifest.json 변환 스크립트
├─ build_train.py             # zip → 512×512 PNG + 캡션 .txt 후처리 스크립트
├─ char_meta.json             # 캐릭터별 메타데이터 (성별/직업/색상 — 캡션 생성용)
├─ catalog.json               # LPC 카탈로그 전체 (참고용, 1.4MB)
├─ items.json                 # 모든 item ID 리스트 (참고용)
├─ manifest.json              # zip-frame 렌더에 사용된 manifest (재현용)
├─ selections/                # 31개 캐릭터 정의 (LPC 도구 입력)
│  ├─ archer-01.json
│  ├─ ...
│  └─ zombie-03.json
├─ sheets/                    # zip-frame 아카이브 원본 (캐릭터당 1개 zip, 546프레임)
│  └─ *.zip (31개)
└─ train/                     # ★ LoRA 학습 입력 (이 폴더를 Kohya에 feed)
   ├─ <char_id>_<anim>_d<dir>_f<NN>.png   # 512×512 검정 배경
   └─ <같은이름>.txt                        # WD14 태그 캡션
```

---

## 캐릭터 구성 (31종)

| 직업/종족 | 수 | bodyType | 특징 |
|---|---|---|---|
| knight | 4 | male×2, female×2 | 판금 갑옷, 대검, 그레이트헬름, 빨간 망토 |
| mage | 4 | female×2, male×2 | 마법사 로브/프록 코트, 지팡이, 마법사 모자 |
| archer | 4 | male×2, female×2 | 가죽 갑옷, 활, 후드, 화살통 |
| rogue | 4 | male×2, female×2 | 긴소매 옷, 단검, 후드 망토 |
| cleric | 4 | female×2, male×2 | 로브/프록 코트, 지팡이, 후드 |
| barbarian | 4 | male | 민소매, 전사 도끼, 바바리안 헬름, 수염 |
| zombie | 3 | male | 녹색 피부, 걸레 옷, 언켐프트 머리 |
| skeleton | 2 | male | 뼈 body, 스켈레톤 head, 검, 바부타 헬름 |
| orc | 2 | male | 짙은 녹색 피부, 가죽 갑옷, 메이스 |

각 캐릭터는 hair 색상(블론드/브라운/블랙/레드) + torso 색상 조합으로 분산되어 **캡션-이미지 매핑 다양성**을 확보.

---

## 프레임 구성 (캐릭터당 104프레임)

| 애니메이션 | 방향 | 프레임 | 부분 합계 |
|---|---|---|---|
| walk | 4방향(up/left/down/right) | 방향당 8 | 32 |
| idle | 4방향 | 방향당 18 | 72 (LPC idle은 18프레임) |
| **합계** | | | **104** |

> walk + idle만 추출. LPC 표준 시트에는 spellcast/thrust/slash/shoot/hurt/climb/run/jump/sit/emote도 있지만, LoRA 학습 기본 액션 다양성은 walk+idle로 충분. 추가 액션이 필요하면 `build_train.py`의 `(walk, idle)` 세트에 다른 애니메이션 추가 후 재실행.

---

## 캡션 포맷 (WD14 태그 호환)

각 `.txt` 파일은 단일 줄의 쉼표 구분 태그:

```
pxlart sprite, <gender_or_monster>, <class>, <gear_tags>, <action>, facing <direction>, <hair> hair, <skin>
```

**예시**:
- `pxlart sprite, 1boy, knight, plate armor, steel sword, walking, facing left, blonde hair, light skin`
- `pxlart sprite, 1girl, mage, wizard robe, magic staff, idle, facing down, black hair, light skin`
- `pxlart sprite, monster, zombie, tattered clothes, walking, facing down, zombie green skin`

**트리거 워드**: `pxlart sprite` (모든 캡션 첫 토큰) — 추론 시 프롬프트 맨 앞에 사용.

**방향 매핑** (LPC 표준 순서 → 인간 명칭):
- `d0` = up (뒤모습)
- `d1` = left
- `d2` = down (정면)
- `d3` = right

---

## Kohya_ss 학습 설정 권장값

```
caption_extension = .txt
resolution = 512,512
bucket_no_upscale = false        # 512×512 정사각이라 버켓 효과 제한
flip_aug = false                 # 캐릭터 스프라이트는 좌우 비대칭(무기/방패) → flip 금지
keep_tokens = 1                  # 트리거 "pxlart sprite" 보존
learning_rate = 1e-4             # 작은 데이터셋+LoRA 표준
network_dim = 16                 # 작은 용량 우선
network_alpha = 8
train_batch_size = 4
gradient_accumulation_steps = 4
epochs = 10~20                   # 3,224장 기준 10~20 epochs면 수렴
```

---

## 재현 / 확장 방법

데이터셋을 다시 생성하거나 캐릭터를 추가하려면:

```bash
cd tools/lpc-sprite-generator

# 1. Headless CLI 빌드 (이미 되어있으면 스킵)
dotnet build dotnet/src/LpcSpriteGen.Headless/LpcSpriteGen.Headless.csproj -c Release

# 2. selections JSON 생성/수정 (gen_selections.py 편집 후)
python datasets/lora-train/gen_selections.py

# 3. manifest 생성
python datasets/lora-train/gen_manifest.py

# 4. zip-frame 렌더 (selections 하나당 한 번씩 — manifest는 zip 미지원 버그 회피)
EXE=dotnet/src/LpcSpriteGen.Headless/bin/Release/net8.0-windows/LpcSpriteGen.Headless.exe
for f in datasets/lora-train/selections/*.json; do
  cid=$(basename "$f" .json)
  $EXE --selections "$f" --format zip-frame --output "datasets/lora-train/sheets/${cid}.zip"
done

# 5. train/ 폴더 후처리
python datasets/lora-train/build_train.py
```

**캐릭터 추가**: `gen_selections.py`의 `PLAN`에 새 `(char_id, builder(variants))` 튜플 추가. builder 함수(knight/mage/...)를 복사해 다른 직업을 만들면 됨. 카탈로그에 존재하는 itemId만 사용 (`catalog.json` 참조).

---

## 알려진 한계 / 향후 개선

- **male mage/cleric**: LPC 카탈로그의 `torso_clothes_robe`가 male 미지원이라, male mage/cleric은 `torso_jacket_frock`으로 대체 (로브와 시각 유사). 정통 로브 학습이 필요하면 female만 사용하거나 카탈로그 확장 필요.
- **muscular bodyType**: 판타지 barbarian에 어울리지만, LPC 카탈로그의 muscular 호환 clothes/legs가 거의 없어 male로 대체. hair/beard로 아치타입 표현.
- **애니메이션 제한**: walk/idle만. 공격/주문 시전 등 전투 액션 학습이 필요하면 `build_train.py`에서 `('walk', 'idle')` 세트에 `'slash'`, `'spellcast'` 추가.
- **단일 트리거**: 모든 캡션이 `pxlart sprite`로 시작. 클래스별 별도 트리거(`pxlart_knight`, `pxlart_mage`)가 필요하면 `build_caption()` 수정.
- **색상 다양성**: hair 4색 × torso 직업별 1~2색. 더 풍부한 색상 학습이 필요하면 `gen_selections.py`의 variants에 색상 추가.

---

## 라이선스

원본 LPC 아트는 CC-BY-SA 3.0 / GPL 3.0 / OGA-BY 3.0 (Universal LPC Sprite Sheet Character Generator 프로젝트). 각 item의 정확한 라이선스는 `catalog.json`의 `items[].licenses` 참조. 이 데이터셋은 LoRA 학습이라는 변환 작업을 거쳤으며, 학습된 LoRA 가중치의 배포는 원본 라이선스 조건을 따라야 함.
