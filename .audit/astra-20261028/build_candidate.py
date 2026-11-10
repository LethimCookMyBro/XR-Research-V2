"""Build an exact S1 candidate while preserving an independently captured one-field UI scroll state.
Never saves the canonical scene, discards unknown scene edits, installs, or changes research data.
"""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import shutil
import subprocess
import sys
import unity_mcp

ROOT = Path.cwd()
OUT = Path(sys.argv[1]).resolve()
OUT.mkdir(parents=True, exist_ok=False)
SCENE = 'Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity'
EXPECTED_DISK = '3d5bf49f2bbdbc67609774b429a50915fa22b6eb5b2563cd5f51e21393db3787'
EXPECTED_MEMORY = 'e73bf5809eee271c44c4863d3870ff3e57a9f2313ca3221e99f2d3dc908872b4'

def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(2**20), b''):
            h.update(block)
    return h.hexdigest()

def save(name, value):
    (OUT / name).write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')

def inputs():
    values = {}
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        for path in sorted((ROOT / folder).rglob('*')):
            if path.is_file():
                values[path.relative_to(ROOT).as_posix()] = {'sha256': sha(path), 'bytes': path.stat().st_size}
    return values

if sha(ROOT / SCENE) != EXPECTED_DISK:
    raise SystemExit('Canonical scene changed since bounded preservation review; build refused')
original = inputs()
save('source-inputs.json', {'utc': datetime.now(timezone.utc).isoformat(), 'head': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'branch': subprocess.check_output(['git', 'branch', '--show-current'], text=True).strip(), 'status': subprocess.check_output(['git', 'status', '--porcelain'], text=True), 'files': original})
print('FROZEN', len(original), 'files', flush=True)
build_dir = ROOT / 'Logs/QuestFinalQA/build'
prior = OUT / 'prior-pipeline-reports'
prior.mkdir()
for path in build_dir.glob('*.json'):
    shutil.copy2(path, prior / path.name)
critical = ['ProjectSettings/ProjectSettings.asset', 'ProjectSettings/UnityConnectSettings.asset', 'Assets/Plugins/Android/QuestProfile.androidlib/AndroidManifest.xml', SCENE,
'Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_English.asset', 'Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Thai.asset', 'Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Japanese.asset', 'Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset']
for relative in critical:
    target = OUT / 'preserved-inputs' / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / relative, target)

code = r'''
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(UnityEditor.EditorApplication.isPlaying||UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorApplication.isUpdating) throw new System.InvalidOperationException("Editor busy; nothing changed");
if(scene.path!="Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity") throw new System.InvalidOperationException("Unexpected scene; nothing changed");
var clean=typeof(UnityEditor.SceneManagement.EditorSceneManager).GetMethod("ClearSceneDirtiness",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static,null,new System.Type[]{typeof(UnityEngine.SceneManagement.Scene)},null);
if(clean==null) throw new System.MissingMethodException("No bounded dirtiness restore API; nothing changed");
var bar=UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Scrollbar>().Single(b=>b.gameObject.scene==scene&&UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(b).targetObjectId==1226385197);
if(bar.value!=0||!scene.isDirty) throw new System.InvalidOperationException("Reviewed UI scroll state changed; nothing changed");
System.Func<string,string> hash=p=>{using(var f=System.IO.File.OpenRead(p))using(var h=System.Security.Cryptography.SHA256.Create())return System.BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();};
string directory=@@OUT@@;
string before=System.IO.Path.Combine(directory,"scene-memory-before.unity");
string prepared=System.IO.Path.Combine(directory,"scene-memory-build-input.unity");
string after=System.IO.Path.Combine(directory,"scene-memory-after.unity");
if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,before,true))throw new System.IO.IOException("Could not preserve unsaved scene");
if(hash(before)!=@@MEMORY@@||hash(scene.path)!=@@DISK@@)throw new System.InvalidOperationException("Unreviewed scene edits; preserve and stop");
VRMaintenanceResearch.ResearchFlow.Editor.ResearchBuildIntegrityChecks.RequireLocalServicesDisabled();
float value=bar.value; bool wasDirty=scene.isDirty; string build=null; string failure=null; bool restored=false;
try {
    bar.SetValueWithoutNotify(1);
    if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,prepared,true)||hash(prepared)!=hash(scene.path))throw new System.InvalidOperationException("Prepared scene differs from canonical disk; no build");
    // All serialized scene bytes now equal the canonical disk; no unverified edits are hidden from the build guard.
    clean.Invoke(null,new object[]{scene});
    build=VRMaintenanceResearch.ResearchFlow.Editor.ResearchBuildIntegrityChecks.BuildWithOriginalServicesDisabled();
} catch(System.Exception e){failure=e.ToString();}
finally {
    var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    var originalBar=UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Scrollbar>().Single(b=>b.gameObject.scene==current&&UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(b).targetObjectId==1226385197);
    originalBar.SetValueWithoutNotify(value);
    if(wasDirty)UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(current);else clean.Invoke(null,new object[]{current});
    if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(current,after,true))throw new System.IO.IOException("After-state capture failed; before copy remains available");
    restored=hash(before)==hash(after)&&hash(current.path)==@@DISK@@&&current.isDirty==wasDirty;
    if(!restored)failure=(failure??"")+"\nScene preservation verification failed";
}
return new {build=build,error=failure,scene_preserved=restored,canonical_scene_sha256=hash(scene.path),memory_before_sha256=hash(before),memory_after_sha256=hash(after)};
'''.replace('@@OUT@@', json.dumps(str(OUT))).replace('@@MEMORY@@', json.dumps(EXPECTED_MEMORY)).replace('@@DISK@@', json.dumps(EXPECTED_DISK))
response = unity_mcp.request('tools/call', {'name':'execute_code', 'arguments':{'action':'execute','code':code,'safety_checks':True,'compiler':'auto'}}, timeout=900)
save('unity-build-response.json', response)
print(json.dumps(response,ensure_ascii=True), flush=True)
structured=response.get('structuredContent', {})
result=structured.get('data',{}).get('result',{})
after=inputs()
changes=[p for p in sorted(set(original)|set(after)) if original.get(p)!=after.get(p)]
save('source-preservation.json', {'unchanged':not changes,'changed_paths':changes,'files_before':len(original),'files_after':len(after)})
for path in build_dir.glob('*.json'):
    shutil.copy2(path, OUT/path.name)
if not structured.get('success') or not result.get('scene_preserved') or result.get('error') or changes:
    raise SystemExit('BUILD_NOT_ACCEPTED: see recorded error/preservation evidence; no installation attempted')
build_result=json.loads((OUT/'build_result.json').read_text('utf-8-sig'))
print('BUILD_RESULT',json.dumps(build_result,ensure_ascii=True),flush=True)
print('SOURCE_AND_UI_STATE_PRESERVED',flush=True)
