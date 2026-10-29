"""Generate Documentation/S1/ILLUSTRATED_CAPTION_SPLIT_TH_JA.md from the committed S1 Reader packages.

Mirrors ResearchCaptionParts.Split (C#): paragraphs separated by a blank line; captions with >= 4 non-empty paragraphs become
sub-pages (heading repeated + one paragraph each). Text is copied verbatim; the C# checks prove Join(Split(x)) == x.
Usage: python Tools/s1_caption_split_table.py [--check]
"""
import json, sys, hashlib
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
PKG = ROOT / 'Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchReaderS1'
OUT = ROOT / 'Documentation/S1/ILLUSTRATED_CAPTION_SPLIT_TH_JA.md'
MIN = 4

def split(caption):
    paragraphs = caption.split('\n\n')
    if len(paragraphs) < MIN or any(not p.strip() for p in paragraphs): return None, [caption]
    return paragraphs[0], paragraphs[1:]

def cell(text): return text.replace('|', '\\|').replace('\n', '<br>')

def render():
    lines = ['# Illustrated: แบ่ง caption ยาวเป็นหน้าย่อย (ตารางเทียบ TH/JA)', '',
             'สร้างอัตโนมัติด้วย `Tools/s1_caption_split_table.py` จาก package ที่ commit แล้ว — **ข้อความไม่ได้ถูกแก้ไข** แค่แบ่งที่บรรทัดว่างเดิม',
             'หัวข้อ (ย่อหน้าแรก) แสดงซ้ำทุกหน้าย่อย; จำนวนหน้าย่อย TH และ JA เท่ากันทุกภาพ (ตรวจใน ResearchFlowChecks)',
             'สถานะ: รูปแบบการแสดงผลใหม่ `S1_ILLUSTRATED_PARAGRAPH_PAGES_20261015`; เนื้อหายังเป็น `S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW` (รอคนตรวจภาษา/เนื้อหา)',
             'ภาพย่อย: ทุกหน้าย่อยใช้ภาพเต็มเดิม ยังไม่ได้ครอปภาพเฉพาะส่วน (ต้องเลือกกรอบภาพใน Unity/ภาพจริง และต้องระวังไม่ชี้ชิ้นที่ถูก)', '']
    for task in ('A', 'B', 'T', 'T2', 'T3'):
        th = json.loads((PKG / f'{task}_th/package.json').read_text(encoding='utf-8-sig'))
        ja = json.loads((PKG / f'{task}_ja/package.json').read_text(encoding='utf-8-sig'))
        lines += [f'## Task {task}', '']
        for i, (a, b) in enumerate(zip(th['images'], ja['images']), 1):
            ct, cj = a['caption_th'], b['caption_ja']
            ht, pt = split(ct); hj, pj = split(cj)
            assert len(pt) == len(pj), (task, i)
            name = a['resource'].split('/')[-1]
            lines += [f'### ภาพ {i}: `{name}` — เดิม 1 caption (TH {len(ct)} / JA {len(cj)} ตัวอักษร) → {len(pt)} หน้าย่อย', '',
                      f'sha256 caption TH `{hashlib.sha256(ct.encode()).hexdigest()[:16]}` JA `{hashlib.sha256(cj.encode()).hexdigest()[:16]}`', '']
            if ht is not None: lines += [f'หัวข้อที่แสดงซ้ำ: TH “{cell(ht)}” / JA “{cell(hj)}”', '']
            lines += ['| หน้าย่อย | TH (ข้อความเดิม) | JA (ข้อความเดิม) |', '|---|---|---|']
            for k, (x, y) in enumerate(zip(pt, pj), 1):
                lines.append(f'| {k}/{len(pt)} | {cell(x)} | {cell(y)} |')
            lines.append('')
    return '\n'.join(lines) + '\n'

if __name__ == '__main__':
    text = render()
    if '--check' in sys.argv:
        ok = OUT.exists() and OUT.read_text(encoding='utf-8') == text
        print('UP_TO_DATE' if ok else 'STALE'); sys.exit(0 if ok else 1)
    OUT.write_text(text, encoding='utf-8'); print('wrote', OUT)
