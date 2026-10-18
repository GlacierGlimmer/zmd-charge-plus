"""Copy the same offline language resources to each ECP source checkout; no publishing."""
import argparse
import hashlib
import shutil
from pathlib import Path

def sync(workspace):
    source=workspace/'zmd-charge-plus/Localization'
    assert workspace.is_absolute() and (source/'reviewed-language-catalog.json').is_file(), 'Choose the absolute ECP workspace root.'
    assert all((workspace/repo).is_dir() for repo in ['zmd-charge-plus-for-linux','zmd-charge-plus-for-macos','zmd-charge-plus-for-android'])
    files=[p for p in source.glob('*') if p.is_file()]
    desktop=['zmd-charge-plus-for-linux','zmd-charge-plus-for-macos']
    for repo in desktop:
        dest=workspace/repo/'Localization';dest.mkdir(exist_ok=True)
        for path in files:shutil.copyfile(path,dest/path.name)
        for path in (workspace/'zmd-charge-plus/Assets/Fonts').glob('*'):
            target=workspace/repo/'Assets/Fonts'/path.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,target)
    android=workspace/'zmd-charge-plus-for-android/core/src/main/resources/localization';android.mkdir(parents=True,exist_ok=True)
    for path in files:
        if path.suffix=='.json' and not path.name.startswith(('reviewed-','manual.')):shutil.copyfile(path,android/path.name)
    shutil.copyfile(source/'reviewed-language-catalog.json',android/'catalog.json')
    for path in files:
        if path.suffix!='.json' or path.name.startswith(('reviewed-','manual.')):continue
        digest=hashlib.sha256(path.read_bytes()).digest()
        for target in [workspace/repo/'Localization'/path.name for repo in desktop]+[android/path.name]:
            assert hashlib.sha256(target.read_bytes()).digest()==digest,target
    print('Language resources synchronized and byte-verified across four source checkouts.')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('workspace',type=Path);args=parser.parse_args();sync(args.workspace)
