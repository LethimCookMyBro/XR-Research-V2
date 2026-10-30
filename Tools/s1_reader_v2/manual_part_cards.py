"""Generic renderer for authored V4 manuals grouped by physical part."""
import html

SECTION_IDS = {'A': ['task','ram','ssd','gpu','finish','reference'],
               'B': ['task','front_guard','blade','rear_guard','finish','scope']}
CSS = '''
.action-card{border:2px solid #C9D6E5;border-left:7px solid #1F5FAF;padding:15px 20px;margin:14px 0;break-inside:avoid}
.action-card h2{font-size:34px;margin:0 0 8px}
.action-card p{font-size:28px;line-height:1.45;margin:5px 0;white-space:pre-line}
.action-card .result{border-top:1px solid #C9D6E5;margin-top:10px;padding-top:8px;color:#1E5E22}
.manual-goal{font-size:34px;font-weight:700;line-height:1.4;margin:15px 0}
.manual-note{font-size:27px;line-height:1.45;margin:10px 0}
.compact-table td,.compact-table th{font-size:25px;padding:7px 10px;line-height:1.35}
.part-body{display:grid;grid-template-columns:300px 1fr;gap:20px;align-items:start}
.part-image{width:300px;height:225px;position:relative;overflow:hidden;border:2px solid #C9D6E5;background:#fff}
.part-image img{position:absolute;max-width:none}
.part-image svg{position:absolute;left:0;top:0}
'''
def text(node,lang):return html.escape(node if isinstance(node,str) else node[lang])
def table(node,lang):
 cap='<caption>'+text(node['title'],lang)+'</caption>' if 'title' in node else ''
 head='<tr>'+''.join('<th>'+text(x,lang)+'</th>' for x in node['head'])+'</tr>'
 rows=''.join('<tr>'+''.join('<td>'+text(x,lang)+'</td>' for x in row)+'</tr>' for row in node['rows'])
 return '<table class="compact-table">'+cap+head+rows+'</table>'
def figure(node,frames):
 if frames is None:raise ValueError('Part-card manual requires captured frames')
 frame=frames.pick(node['shot'],node['pick'])
 anchor=frame['anchors'].get(node['anchor'])
 if not anchor or not anchor[2]:raise ValueError('Manual target anchor is not visible: '+node['anchor'])
 x,y=anchor[:2];width,height=600,450
 cx=max(0,min(1280-width,x-width/2));cy=max(0,min(720-height,y-height/2));scale=.5
 marker_x=(x-cx)*scale;marker_y=(y-cy)*scale
 return ('<div class="part-image"><img src="'+frames.path(node['shot'],frame)+'" style="width:640px;height:360px;left:'+str(-cx*scale)+'px;top:'+str(-cy*scale)+'px">'
  +'<svg width="300" height="225"><circle cx="'+str(marker_x)+'" cy="'+str(marker_y)+'" r="18" fill="none" stroke="white" stroke-width="7"/><circle cx="'+str(marker_x)+'" cy="'+str(marker_y)+'" r="18" fill="none" stroke="#1F5FAF" stroke-width="3"/></svg></div>')
def blocks(lang,c,frames=None):
 if [s['id'] for s in c['manual']]!=SECTION_IDS.get(c['task']):raise ValueError('Invalid part-card manual sections')
 out=[]
 for section in c['manual']:
  if section['id']=='task':
   out.extend('<p class="'+('manual-goal' if i==0 else 'manual-note')+'">'+text(item,lang)+'</p>' for i,item in enumerate(section['items']))
  elif section.get('layout')=='reference':
   out.append('<div><h2>'+text(section['title'],lang)+'</h2>'+''.join('<p class="manual-note">'+text(item,lang)+'</p>' for item in section['items'])+'</div>')
   if 'table' in section:out.append(table(section['table'],lang))
   out.extend('<p class="manual-note">'+text(item,lang)+'</p>' for item in section.get('after',[]))
  else:
   body=''.join('<p>'+text(item,lang)+'</p>' for item in section['items'])
   if 'figure' in section:body='<div class="part-body">'+figure(section['figure'],frames)+'<div>'+body+'</div></div>'
   body+=''.join('<p class="result">'+text(item,lang)+'</p>' for item in section.get('after',[]))
   out.append('<section class="action-card"><h2>'+text(section['title'],lang)+'</h2>'+body+'</section>')
 return out
