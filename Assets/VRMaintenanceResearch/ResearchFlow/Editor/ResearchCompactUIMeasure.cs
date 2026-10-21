#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchCompactUIMeasure
    {
        public static string Measure(ResearchFlowView ownedFixture, string outputPath)
        {
            if (EditorApplication.isPlaying || !ownedFixture || !ownedFixture.controller)
                throw new InvalidOperationException("Use an owned non-Play fixture with the actual view and authored fonts.");
            string path = Path.GetFullPath(outputPath);
            if (File.Exists(path)) throw new IOException("Do not overwrite measurement evidence: " + path);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var content = ((GameObject)typeof(ResearchFlowView).GetField("mainContent", flags).GetValue(ownedFixture)).GetComponent<RectTransform>();
            ownedFixture.Refresh(); Canvas.ForceUpdateCanvases();
            var panelBounds = Bounds(content, ownedFixture.mainPanel.GetComponent<RectTransform>());
            var measures = new List<object>(); var failures = new List<string>(); var inked = new List<(TMP_Text text, Rect box)>();
            foreach (var text in content.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrWhiteSpace(text.text)) continue;
                text.ForceMeshUpdate(true, true);
                var glyphs = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                var ink = glyphs.Length > 0 ? GlyphBounds(content, text, glyphs) : default;
                if (glyphs.Length > 0) inked.Add((text, ink));
                bool glyphFit = glyphs.Length > 0 && glyphs.All(c => new[] {c.bottomLeft,c.topLeft,c.topRight,c.bottomRight}.All(p => Inside(text.rectTransform.rect,p)));
                bool fit = glyphFit && !text.isTextOverflowing && !text.enableAutoSizing && text.preferredHeight <= text.rectTransform.rect.height + 1;
                if (!fit) failures.Add(text.name + ": glyph overflow/autosizing");
                // TMP has no Thai dictionary and Thai has no spaces inside words: a wrap between two Thai letters splits a word.
                for (int line = 0; line < text.textInfo.lineCount - 1; line++)
                {
                    int last = text.textInfo.lineInfo[line].lastCharacterIndex;
                    if (last + 1 < text.textInfo.characterCount && Thai(text.textInfo.characterInfo[last].character) && Thai(text.textInfo.characterInfo[last + 1].character))
                        failures.Add(Path2(text) + ": Thai word split across lines at " + last);
                }
                var button = text.GetComponentInParent<Button>();
                var labelBounds = button ? Bounds((RectTransform)button.transform,text.rectTransform) : default;
                bool labelContained = !button || ContainsLabel(((RectTransform)button.transform).rect,labelBounds);
                if (!labelContained) failures.Add(button.name + ": label rect outside button");
                measures.Add(new {name=Path2(text),text=text.text,text.fontSize,lines=text.textInfo.lineCount,inked=glyphs.Length>0?RectData(ink):null,text.preferredHeight,text.isTextOverflowing,glyphFit,rect=RectData(text.rectTransform.rect), button=button?button.name:null,buttonRect=button?RectData(((RectTransform)button.transform).rect):null,labelInButton=button?RectData(labelBounds):null,labelContained, minimumPointSize=glyphs.Length==0?0:glyphs.Min(c=>c.pointSize)});
            }
            // Each label can fit its own rect while two rects still collide (17 Oct JA readiness pills), so compare inked glyph boxes pairwise.
            for (int i = 0; i < inked.Count; i++)
                for (int j = i + 1; j < inked.Count; j++)
                    if (Overlaps(inked[i].box, inked[j].box))
                        failures.Add(Path2(inked[i].text) + " / " + Path2(inked[j].text) + ": text overlaps text");
            var body = (TMP_Text)typeof(ResearchFlowView).GetField("body", flags).GetValue(ownedFixture);
            var title = (TMP_Text)typeof(ResearchFlowView).GetField("title", flags).GetValue(ownedFixture);
            foreach (var button in content.GetComponentsInChildren<Button>(false))
            {
                var b = Bounds(content,(RectTransform)button.transform);
                if (!Contains(panelBounds,b)) failures.Add(button.name + ": hit target outside panel");
                if (body.gameObject.activeInHierarchy && Overlaps(b,Bounds(content,body.rectTransform))) failures.Add(button.name + ": overlaps body");
                if (title.gameObject.activeInHierarchy && Overlaps(b,Bounds(content,title.rectTransform))) failures.Add(button.name + ": overlaps title");
            }
            string json = JsonConvert.SerializeObject(new {scope="Actual Unity/TMP geometry only; not pointer, Quest, optical comfort or language approval",state=ownedFixture.controller.Flow?.State.ToString()??"setup",content=RectData(content.rect),hitTargetPanel=RectData(panelBounds),measurements=measures,failures,pass=failures.Count==0},Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,json);return json;
        }
        static bool Thai(char c)=>c>='฀'&&c<='๿';
        static object RectData(Rect r)=>new {x=r.x,y=r.y,width=r.width,height=r.height};
        static string Path2(TMP_Text t)=>(t.transform.parent?t.transform.parent.name+"/":"")+t.name;
        static Rect GlyphBounds(RectTransform root,TMP_Text text,TMP_CharacterInfo[] glyphs)
        {var p=glyphs.SelectMany(c=>new[]{c.bottomLeft,c.topRight}).Select(v=>root.InverseTransformPoint(text.transform.TransformPoint(v))).ToArray();return Rect.MinMaxRect(p.Min(x=>x.x),p.Min(x=>x.y),p.Max(x=>x.x),p.Max(x=>x.y));}
        static bool Inside(Rect r,Vector3 p)=>p.x>=r.xMin-1&&p.x<=r.xMax+1&&p.y>=r.yMin-1&&p.y<=r.yMax+1;
        static Rect Bounds(RectTransform root,RectTransform target)
        {var p=new Vector3[4];target.GetWorldCorners(p);for(int i=0;i<4;i++)p[i]=root.InverseTransformPoint(p[i]);return Rect.MinMaxRect(p.Min(x=>x.x),p.Min(x=>x.y),p.Max(x=>x.x),p.Max(x=>x.y));}
        static bool Contains(Rect a,Rect b)=>b.xMin>=a.xMin-1&&b.yMin>=a.yMin-1&&b.xMax<=a.xMax+1&&b.yMax<=a.yMax+1;
        static bool ContainsLabel(Rect a,Rect b)=>b.xMin>=a.xMin-.5f&&b.yMin>=a.yMin-.5f&&b.xMax<=a.xMax+.5f&&b.yMax<=a.yMax+.5f;
        static bool Overlaps(Rect a,Rect b)=>a.xMin<b.xMax-1&&a.xMax>b.xMin+1&&a.yMin<b.yMax-1&&a.yMax>b.yMin+1;
    }
}
#endif
