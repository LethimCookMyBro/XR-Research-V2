"""Recheck the saved isolated baseline; does not run Unity or edit scenes."""
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import zlib

A = Path(__file__).resolve().parent
ROOT = A.parents[3]
SCENE = ROOT / 'Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity'
ENV = ROOT / 'Assets/VRMaintenanceResearch/Assets/V2/Environment'

def read(name):
    return json.loads((A / name).read_text(encoding='utf-8-sig'))

def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    final = read('final_verification.json')
    report = read('environment_v1_unity_import_report.json')
    play = read('playmode_verification.json')
    assert final['status'] == 'PASS' and final['compile_errors'] == 0
    assert digest(SCENE) == final['scene_sha256']
    protected = {
        'Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity': 'be657e90ee0bcea1095e9a3d9418c574a9dd4559f42d69c1d06cd6ac52cd3bc8',
        'Assets/XRI_Examples/Scenes/XRI_Examples_Main.unity': '5702122dcac5497d96d4a508ebeba89b603c2b49636b36da1d25eff8d3edaa73',
    }
    for p, sha in protected.items():
        assert digest(ROOT / p) == sha, p
    assert len(report['source_files']) == 7
    for source in report['source_files']:
        local = ENV / source['name']
        assert digest(local) == digest(Path(source['source'])) == source['sha256']
        data = local.read_bytes()
        assert struct.unpack_from('<4sII', data) == (b'glTF', 2, len(data))
    assert play['status'] == 'PASS' and play['natural_loop_observed']
    assert play['object_count_stable'] and len(play['persistent_parts']) == 8
    assert play['robot_roots'] == 2 and play['xr_all_positions_pass']
    assert abs(play['duration_seconds'] - 48) < .01
    shots = read('screenshots.json')
    assert len(shots) == 8
    for shot in shots:
        data = (ROOT / shot['path']).read_bytes()
        assert data[:8] == b'\x89PNG\r\n\x1a\n'
        offset = 8
        while offset < len(data):
            length = struct.unpack_from('>I', data, offset)[0]
            chunk = data[offset+4:offset+8+length]
            crc = struct.unpack_from('>I', data, offset+8+length)[0]
            assert zlib.crc32(chunk) & 0xffffffff == crc
            offset += length + 12
        assert offset == len(data)
    assert digest(A/'Environment_V2_BASELINE.unity') == digest(A/'Rollback_Test/Restored_Empty_Baseline.unity')
    assert final['baseline_roots'] == final['rollback_roots'] == 0
    assert final['modified_groups'] == 7 and final['modified_retained']
    assert not subprocess.check_output(['git','diff','--name-only'], cwd=ROOT).strip(), 'Unexpected tracked changes'
    for name in ('environment_scene_diff.json', 'VERIFICATION.txt', 'ROLLBACK.sh'):
        assert (A/name).read_bytes(), name
    print('BASELINE_PASS roots=0 protected_scenes=2')
    print('MODIFIED_PASS groups=7 glbs=7 screenshots=8 robots=2 workpieces=8 loop_seconds=48 compile_errors=0')
    print('ROLLBACK_PASS roots=0 exact=1 modified_retained=1')

if __name__ == '__main__':
    main()
