"""Check the built APK's resolved launcher icon, not just Unity PlayerSettings.

Usage: python Tools/check_quest_launcher_icon.py --aapt2 PATH APK
This proves packaging only; confirm the icon visually in Quest Library separately.
"""
import argparse
import hashlib
import re
import struct
import subprocess
import zipfile


def check(aapt2, apk):
    result = subprocess.run([aapt2, 'dump', 'badging', apk], capture_output=True,
                            text=True, encoding='utf-8', errors='replace', check=True)
    app = re.search(r"^application: label='([^']*)' icon='([^']*)'", result.stdout, re.M)
    activity = re.search(r"^launchable-activity: name='([^']*)'\s+label='([^']*)' icon='([^']*)'", result.stdout, re.M)
    assert app and activity, 'Application or launchable activity missing'
    paths = re.findall(r"^application-icon-\d+:'([^']+)'", result.stdout, re.M)
    assert paths, 'No resolved application icons'
    paths += [app[2], activity[3]]
    assert app[1] == activity[2] == 'VR Maintenance Lab', 'Launcher label changed'
    assert activity[1] == 'com.unity3d.player.UnityPlayerGameActivity', 'Launcher activity changed'
    manifest = subprocess.run([aapt2, 'dump', 'xmltree', apk, '--file', 'AndroidManifest.xml'],
                              capture_output=True, text=True, encoding='utf-8',
                              errors='replace', check=True).stdout
    icon_ids = re.findall(r'android:icon\([^)]*\)=(@0x[0-9a-f]+)', manifest)
    round_ids = re.findall(r'android:roundIcon\([^)]*\)=(@0x[0-9a-f]+)', manifest)
    assert icon_ids and round_ids and len(set(icon_ids + round_ids)) == 1, 'Round/application/activity icons differ'
    with zipfile.ZipFile(apk) as archive:
        for path in set(paths):
            assert path.endswith('.png'), 'Launcher resolves to non-PNG: ' + path
            data = archive.read(path)
            assert data[:8] == b'\x89PNG\r\n\x1a\n', 'Not a PNG: ' + path
            width, height = struct.unpack('>II', data[16:24])
            assert width == height and width >= 512, f'Icon too small/non-square: {width}x{height}'
            print(f'PNG_ICON path={path} size={width}x{height} sha256={hashlib.sha256(data).hexdigest()}')
    print('QUEST_LAUNCHER_ICON_PASS application+activity+roundIcon+all_densities=PNG')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--aapt2', required=True)
    parser.add_argument('apk')
    args = parser.parse_args()
    try:
        check(args.aapt2, args.apk)
    except (AssertionError, subprocess.CalledProcessError, KeyError, zipfile.BadZipFile) as error:
        print('QUEST_LAUNCHER_ICON_FAIL ' + str(error))
        raise SystemExit(1)
