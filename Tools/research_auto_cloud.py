"""Unattended Quest -> PC -> Google Drive loop for DEV/TEST/PILOT data (Route A without a cable).

Each pass, for every Quest that adb reaches (USB or Wi-Fi `adb connect <ip>:5555`): pull-queue verifies and ingests
finished exports, then research_cloud_sync copies them to the pinned Drive folder and provider-verify reads them back
from Google. A Quest that is off or out of range is simply skipped; its exports stay queued on the headset.
Study (TH/JP) data is never pulled automatically (pull-queue refuses it).

usage: research_auto_cloud.py --adb ADB --data-root DIR --config drive.json [--interval 60] [--once]
"""
import argparse,json,subprocess,sys,time
from datetime import datetime,timezone
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import research_cloud_sync as cs
import research_export as rx

def quests(adb):
    out=subprocess.run([adb,'devices'],capture_output=True,text=True,timeout=30).stdout.splitlines()[1:]
    return [line.split()[0] for line in out if line.strip().endswith('device')]

def one_pass(adb,data_root,config):
    pulled={}
    for serial in quests(adb):
        r=rx.pull_queue(adb,serial,data_root)
        pulled[serial]=dict(status=r.get('status'),delivered=len(r.get('delivered',[])),failed=len(r.get('failed',[])),reason=r.get('reason'))
    synced=cs.sync(data_root,config)['bundles']
    verified=cs.provider_verify(data_root,config)['bundles']
    return dict(utc=cs.stamp(cs.now()),quests=pulled,
                cloud={b.get('bundle_id',b.get('bundle')):b['status'] for b in verified},
                pending=[dict(bundle=b.get('bundle_id'),status=b['status'],reason=b.get('reason')) for b in synced+verified
                         if b['status'] not in ('provider_verified','already_provider_verified','already_cloud_verified','cloud_folder_verified')])

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--adb',required=True);p.add_argument('--data-root',required=True);p.add_argument('--config',required=True)
    p.add_argument('--interval',type=int,default=60);p.add_argument('--once',action='store_true')
    a=p.parse_args(argv);log=Path(a.data_root)/'.cloud-sync'/'auto_cloud.jsonl';log.parent.mkdir(parents=True,exist_ok=True)
    while True:
        try:result=one_pass(a.adb,a.data_root,a.config)
        except Exception as ex:result=dict(utc=datetime.now(timezone.utc).isoformat(),status='pass_failed',reason=repr(ex))
        line=json.dumps(result,ensure_ascii=False);print(line,flush=True)
        with log.open('a',encoding='utf-8') as f:f.write(line+'\n')
        if a.once:return 0
        time.sleep(a.interval)

if __name__=='__main__':sys.exit(main())
