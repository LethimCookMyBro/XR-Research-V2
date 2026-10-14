"""Local read-only gallery of existing PNG bytes. No Unity/device/image edits."""
import hashlib
import html
import json
import pathlib
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

folder = pathlib.Path(__file__).resolve().parent
root = folder.parents[2]
ui = root / 'Logs/QuestFinalIntegration/visual-distill-20261004/release-verified/synthetic-mono-preview'
sprites = root / 'Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchFlowTutorial'
states = [
    ('coach_teleport', '1. เคลื่อนที่ด้วย Teleport'),
    ('coach_snap', '2. หันด้วย Snap turn'),
    ('coach_roomscale', '3. ขยับศีรษะในพื้นที่ปลอดภัย'),
    ('coach_select', '4. ชี้และเลือกปุ่ม'),
    ('coach_grab', '5. จับวัตถุ'),
    ('coach_move', '6. ยกวัตถุ'),
    ('coach_regrab_before_move', '7. จับต่อก่อนยก'),
    ('coach_rotate', '8. หมุนวัตถุ'),
    ('coach_regrab_before_rotate', '9. จับต่อก่อนหมุน'),
    ('coach_release_instruction', '10. ปล่อยวัตถุใกล้โต๊ะ'),
    ('reader_switch', '11. สลับรูปแบบเอกสาร'),
    ('reader_reposition', '12. ย้ายแผงเอกสาร'),
    ('reader_close', '13. ปิดเอกสาร'),
    ('coach_inspect', '14. บทฝึก Examine / Inspect'),
    ('task_A', '15. Task A'),
    ('task_B', '16. Task B'),
]
references = [
    ('01-grab.png', '01 · Grab', 'ใช้แนวภาพใหญ่และคำสั่งเดียว'),
    ('02-regrab.png', '02 · Regrab', 'ใช้หัวข้อจับต่อและสถานะเก็บความคืบหน้า'),
    ('03-select.png', '03 · Select', 'ภาพ controller บอกปุ่มผิด จึงใช้ diagram เดิมใน Unity'),
    ('04-rotate.png', '04 · Rotate', 'ใช้ภาพมือ/วัตถุ และคงคำสอน 30 องศาเดิม'),
    ('05-reference-switch.png', '05 · Reference switch', 'ใช้ icon คู่กับชื่อ ไม่ใช้เนื้อหาซ่อมที่ AI แต่งขึ้น'),
    ('06-reference-move.png', '06 · Reference move', 'ภาพ Grip ไม่ตรงของจริง ซึ่งใช้ Trigger/pinch จึงตัดส่วนผิดออก'),
    ('07-reference-reading.png', '07 · Reference reading', 'ใช้แนว spacing ไม่แทนที่เอกสารวิจัย'),
    ('08-pause.png', '08 · Pause', 'ใช้ hierarchy โดยคงสิทธิ์ resume ตาม protocol เดิม'),
    ('09-neutral-task.png', '09 · Task', 'ตัด card และปุ่มใหญ่ที่แข่งกับชิ้นงาน'),
    ('10-minimized-reference.png', '10 · Minimized reference', 'ตัด composition นี้ออก เพราะปุ่มและแผงมากเกินไป'),
]
images = []
for language in ['Thai', 'Japanese', 'English']:
    for state, title in states:
        images.append({'group': 'ui', 'language': language, 'title': title,
                       'note': 'Unity Editor · synthetic mono preview', 'path': str(ui / (language + '_' + state + '.png'))})
for name, title, note in references:
    images.append({'group': 'ref', 'language': '', 'title': title, 'note': note, 'path': str(folder / name)})
for name, title in [('Grasp', 'ภาพมือจับวัตถุ'), ('Release', 'ภาพมือปล่อยวัตถุ')]:
    images.append({'group': 'asset', 'language': '', 'title': title,
                   'note': 'Sprite โปร่งใสที่นำไปใช้ใน Tutorial จริง', 'path': str(sprites / (name + '.png'))})
for index, record in enumerate(images):
    source = pathlib.Path(record['path'])
    if not source.is_file():
        raise FileNotFoundError(source)
    record.update(id=index, url='/images/' + str(index) + '.png', bytes=source.stat().st_size,
                  sha256=hashlib.sha256(source.read_bytes()).hexdigest())
if len(images) != 60 or len(list(ui.glob('*.png'))) != 48:
    raise RuntimeError('Unexpected image inventory')
(folder / 'ALL_IMAGES_MANIFEST.json').write_text(json.dumps(images, ensure_ascii=False, indent=2), encoding='utf-8')

template = '''<!doctype html><html lang="th"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ภาพทั้งหมด · VR ResearchFlow</title><style>
:root{font-family:system-ui,sans-serif;color:#edf1f4;background:#182129;color-scheme:dark}*{box-sizing:border-box}body{margin:0}header{padding:28px 5vw 20px;border-bottom:1px solid #3a4853}h1{font-size:clamp(24px,4vw,38px);margin:0 0 10px}p{line-height:1.65;margin:8px 0;color:#c9d4dc}.controls{display:flex;gap:12px;flex-wrap:wrap;margin:20px 0 8px}label{display:grid;gap:6px;font-size:14px}select,button{font:inherit;padding:10px 14px;border:1px solid #657989;border-radius:8px;background:#263440;color:#fff;cursor:pointer}select:focus-visible,button:focus-visible,.thumb:focus-visible{outline:3px solid #95c8fa;outline-offset:3px}main{padding:24px 5vw 40px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,340px),1fr));gap:28px 20px}figure{margin:0;min-width:0}.thumb{width:100%;border:1px solid #435666;border-radius:8px;padding:0;overflow:hidden;display:block;background:repeating-conic-gradient(#dce2e6 0% 25%,#edf1f4 0% 50%) 50%/24px 24px}.thumb img{display:block;width:100%;height:260px;object-fit:contain}figcaption{padding:12px 0 0;line-height:1.55}figcaption strong{font-weight:600}figcaption small{display:block;color:#bccbd5;margin-top:4px}dialog{border:1px solid #526879;border-radius:10px;padding:14px;background:#172129;width:min(96vw,1450px);max-width:none;color:#fff}dialog::backdrop{background:#05090de8}.viewer-head{display:flex;justify-content:space-between;align-items:center;gap:12px}#large{width:100%;height:min(72vh,1000px);object-fit:contain;display:block;margin:12px 0;background:repeating-conic-gradient(#dce2e6 0% 25%,#edf1f4 0% 50%) 50%/24px 24px}.viewer-foot{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}#detail{max-width:70ch}.tag{color:#9fc7e8;font-size:14px}#count{font-weight:600;color:#edf1f4}a{color:#a9d3ff}@media(min-width:1500px){.grid{grid-template-columns:repeat(3,1fr)}}@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto}}
</style><header><h1>ภาพทั้งหมด 60 รูป</h1><p>UI จาก Unity 48 รูป · ภาพอ้างอิง 10 รูป · ภาพมือที่ใช้จริง 2 รูป</p><p>UI เป็นภาพตรวจ layout ใน Editor ยังไม่ใช่ภาพขณะสวม Quest ส่วนภาพอ้างอิงเป็น concept จาก AI กดภาพเพื่อขยาย</p><div class="controls"><label>ประเภท<select id="group"><option value="all">ทั้งหมด</option><option value="ui">UI จาก Unity</option><option value="ref">ภาพอ้างอิง 10 รูป</option><option value="asset">ภาพมือที่ใช้จริง</option></select></label><label>ภาษา UI<select id="language"><option value="all">ทุกภาษา</option><option value="Thai">ไทย</option><option value="Japanese">日本語</option><option value="English">English</option></select></label></div><p id="count" aria-live="polite"></p></header><main><div id="grid" class="grid"></div></main><dialog id="viewer"><div class="viewer-head"><strong id="title"></strong><button id="close" aria-label="ปิดภาพ">ปิด ×</button></div><img id="large" alt=""><div class="viewer-foot"><p id="detail"></p><div><button id="previous" aria-label="ภาพก่อนหน้า">← ก่อนหน้า</button> <button id="next" aria-label="ภาพถัดไป">ถัดไป →</button></div></div></dialog><script>
const images=__IMAGES__, grid=document.querySelector('#grid'), group=document.querySelector('#group'), language=document.querySelector('#language'), viewer=document.querySelector('#viewer');let shown=[],selected=0;
function render(){shown=images.filter(i=>(group.value==='all'||i.group===group.value)&&(language.value==='all'||i.group!=='ui'||i.language===language.value));grid.replaceChildren();document.querySelector('#count').textContent='แสดง '+shown.length+' รูป';shown.forEach((item,index)=>{const figure=document.createElement('figure'),button=document.createElement('button'),img=document.createElement('img'),caption=document.createElement('figcaption'),title=document.createElement('strong'),note=document.createElement('small');button.className='thumb';button.setAttribute('aria-label','ขยาย '+item.title+(item.language?' · '+item.language:''));img.src=item.url;img.alt=item.title;img.loading='lazy';button.append(img);button.onclick=()=>open(index);title.textContent=item.title+(item.language?' · '+item.language:'');note.textContent=item.note;caption.append(title,note);figure.append(button,caption);grid.append(figure)})}
function update(){const item=shown[selected];document.querySelector('#title').textContent=item.title+(item.language?' · '+item.language:'');document.querySelector('#large').src=item.url;document.querySelector('#large').alt=item.title;document.querySelector('#detail').textContent=(selected+1)+' / '+shown.length+' · '+item.note;document.querySelector('#previous').disabled=shown.length<2;document.querySelector('#next').disabled=shown.length<2}
function open(index){selected=index;update();viewer.showModal()}
function advance(delta){selected=(selected+delta+shown.length)%shown.length;update()}
group.onchange=render;language.onchange=render;document.querySelector('#close').onclick=()=>viewer.close();document.querySelector('#previous').onclick=()=>advance(-1);document.querySelector('#next').onclick=()=>advance(1);viewer.addEventListener('keydown',e=>{if(e.key==='ArrowLeft'){e.preventDefault();advance(-1)}if(e.key==='ArrowRight'){e.preventDefault();advance(1)}});viewer.addEventListener('click',e=>{if(e.target===viewer){const r=viewer.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)viewer.close()}});render();
</script></html>'''
public = [{key: record[key] for key in ['id', 'url', 'group', 'language', 'title', 'note']} for record in images]
page = template.replace('__IMAGES__', json.dumps(public, ensure_ascii=False))
(folder / 'ALL_IMAGES.html').write_text(page, encoding='utf-8')
markdown = ['# ภาพทั้งหมด 60 รูป', '', '48 ภาพ UI เป็น synthetic mono preview ใน Unity Editor ไม่ใช่ wearer evidence; 10 concepts และ 2 transparent tutorial assets แยกไว้ตาม caption', '']
for record in images:
    markdown.extend([record['title'] + (' · ' + record['language'] if record['language'] else ''), '',
                     record['note'], '', '![' + record['title'] + '](' + pathlib.Path(record['path']).as_posix() + ')', ''])
(folder / 'ALL_IMAGES.md').write_text('\n'.join(markdown), encoding='utf-8')


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        route = self.path.split('?', 1)[0]
        match = re.fullmatch(r'/images/(\d+)\.png', route)
        if route in ['/', '/index.html']:
            content, mime = page.encode('utf-8'), 'text/html; charset=utf-8'
        elif match and 0 <= int(match[1]) < len(images):
            content = pathlib.Path(images[int(match[1])]['path']).read_bytes()
            mime = 'image/png'
        elif route == '/favicon.ico':
            self.send_response(204)
            self.end_headers()
            return
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(content)))
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.end_headers()
        self.wfile.write(content)

    def log_message(self, *_):
        pass


server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
url = 'http://127.0.0.1:' + str(server.server_port) + '/'
(folder / 'GALLERY_SERVER.json').write_text(json.dumps({'url': url, 'image_count': len(images), 'read_only': True,
                                                     'scope': 'Loopback only; serves HTML and 60 explicitly selected PNGs; no directory listing'}, indent=2), encoding='utf-8')
print(json.dumps({'url': url, 'image_count': len(images)}), flush=True)
server.serve_forever()
