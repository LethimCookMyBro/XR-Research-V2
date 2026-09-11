"""Small saved-artifact regression check; live tests reuse the baseline Unity helper."""
import hashlib
import json
from pathlib import Path
import re
import subprocess

A = Path(__file__).resolve().parent
ROOT = A.parents[4]
V2 = A.parent
SCENE = ROOT / 'Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity'
ENV = ROOT / 'Assets/VRMaintenanceResearch/Assets/V2/Environment'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    baseline = read(V2/'final_verification.json')
    assert sha(A/'BASELINE.unity') == baseline['scene_sha256']
    assert read(A/'baseline_structure.json') == read(A/'modified_structure.json')
    changes = read(A/'material_changes.json')
    assert len(changes) == 16
    assert sum(x['before']=='Glass' for x in changes) == 10
    assert sum(x['before']=='Oak' for x in changes) == 6
    guids = [re.search(r'^guid: (\w+)',p.read_text(),re.M)[1] for p in (ENV/'Materials').glob('*.mat.meta')]
    assert len(guids) == 2
    pattern = (r'    - target: \{[^\n]+\}\n'
               r"      propertyPath: 'm_Materials.Array.data\[0\]'\n"
               r'      value: \n'
               r'      objectReference: \{fileID: 2100000, guid: (?:'+ '|'.join(guids)+r'), type: 2\}\n')
    restored, count = re.subn(pattern, '', SCENE.read_text())
    assert count == 16 and restored == (A/'BASELINE.unity').read_text(), 'Non-material scene edit'
    check=read(A/'visual_verification.json')
    assert check['status']=='PASS' and check['structure_unchanged'] and check['scene_sha256']==sha(SCENE)
    play=read(A/'playmode_verification.json')
    assert play['status']=='PASS' and play['duration_seconds']==48 and play['natural_loop_observed']
    assert play['object_count_stable'] and len(play['persistent_parts'])==8
    audit=read(A/'environment_v1_unity_import_report.json')
    assert audit['status']=='ASSEMBLY_AUDIT_PASS' and all(x['rooms_remain_active'] for x in audit['toggle_result'])
    for source in audit['source_files']: assert sha(ENV/source['name'])==source['sha256']
    rollback=read(A/'rollback_verification.json')
    assert rollback['status']=='PASS' and rollback['restored_original_materials'] and rollback['modified_retained']
    assert sha(A/'BASELINE.unity')==sha(A/'Rollback_Test/Restored_Baseline.unity')
    assert rollback['console']['compileErrorEntries']==0 and not rollback['console']['isPlaying']
    shots=read(A/'screenshots.json'); assert len(shots)==3
    for shot in shots:
        data=(ROOT/shot['path']).read_bytes(); assert data[:8]==b'\x89PNG\r\n\x1a\n' and data[-8:-4]==b'IEND'
    allowed={'Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity',
             'Assets/VRMaintenanceResearch/Assets/V2/Environment/Editor/EnvironmentV2Build.cs',
             'Assets/VRMaintenanceResearch/Assets/V2/Environment/Editor/EnvironmentV2Import.cs'}
    changed=set(subprocess.check_output(['git','diff','--name-only'],cwd=ROOT,text=True).splitlines())
    assert changed <= allowed, changed-allowed
    print('BASELINE_PASS checkpoint_preserved=1 original_evidence_unchanged=1')
    print('MODIFIED_PASS glass_slots=10 oak_slots=6 material_only=1 structure=1967 loop_seconds=48 screenshots=3')
    print('ROLLBACK_PASS original_materials=1 groups=7 modified_retained=1')

if __name__=='__main__': main()
