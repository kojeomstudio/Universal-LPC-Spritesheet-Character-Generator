"""manifest.json 생성기 — selections 내용을 인라인으로 포함.

ManifestEntry 스키마 (Program.cs:459):
  { Seed?, BodyType?, Selections?: Dict<string, Selection>, Output }

Selections가 경로가 아니라 인라인 사전이므로 각 selections/*.json을 읽어
manifest 엔트리로 확장한다.

Output 경로는 절대경로로 기록 (CWD 의존 회피).
"""
import os
import json

BASE = os.path.dirname(os.path.abspath(__file__))
sel_dir = os.path.join(BASE, 'selections')
sheets_dir = os.path.join(BASE, 'sheets')
os.makedirs(sheets_dir, exist_ok=True)

manifest = []
for fn in sorted(os.listdir(sel_dir)):
    if not fn.endswith('.json'):
        continue
    char_id = fn[:-5]
    with open(os.path.join(sel_dir, fn), encoding='utf-8') as f:
        payload = json.load(f)
    manifest.append({
        'bodyType': payload['bodyType'],
        'selections': payload['selections'],
        'output': os.path.join(sheets_dir, char_id + '.zip'),
    })

with open(os.path.join(BASE, 'manifest.json'), 'w', encoding='utf-8') as f:
    json.dump(manifest, f, indent=2)

print('manifest.json 생성:', len(manifest), '엔트리')
print('output 디렉토리:', sheets_dir)
print('처음 엔트리 output:', manifest[0]['output'])

