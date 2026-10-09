"""Check deterministic UI states and produce compact private visual-review sheets."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
GROUPS = {
    'pages': ['home-running.png', 'settings.png', 'video-pending.png', 'bindings-current.png', 'about.png', 'diagnostic-report.png'],
    'connections': ['connection-usb.png', 'wireless-ready.png', 'wireless-code.png', 'wireless-manual-connect.png', 'connection-device-history.png', 'connection-device-name-long.png'],
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', required=True)
    args = parser.parse_args()
    root = Path(args.directory).resolve()
    if not root.is_relative_to(ROOT/'artifacts'):raise SystemExit('UI review must stay inside repository artifacts.')
    expected = set(json.loads((root/'manifest.json').read_text())['files'])
    errors, profiles, checks = [], [], 0
    review = root/'review';review.mkdir(exist_ok=True)
    profile_names = [''] + sorted(folder.name for folder in root.iterdir() if folder.is_dir() and (folder/'manifest.json').is_file())
    for profile in profile_names:
        folder = root/profile
        manifest = json.loads((folder/'manifest.json').read_text())
        if set(manifest['files']) != expected:errors.append({'profile': profile, 'rule': 'state-inventory'})
        for name in sorted(expected):
            with Image.open(folder/name) as image:
                if image.size != (manifest['width'], manifest['height']):errors.append({'profile': profile, 'file': name, 'rule': 'image-size'})
                image.verify()
            controls = json.loads((folder/(name+'.layout.json')).read_text())
            if not controls:errors.append({'profile': profile, 'file': name, 'rule': 'empty-control-audit'})
            for control in controls:
                checks += 1
                if not control['contained'] or not control['textFits']:
                    errors.append({'profile': profile, 'file': name, 'control': control['name'], 'rule': 'bounds-or-text-clipping'})
                if not control.get('horizontalContained',True):
                    errors.append({'profile': profile, 'file': name, 'control': control['name'], 'rule': 'horizontal-scroll-clipping'})
                if not control.get('captionFits',True):
                    errors.append({'profile': profile, 'file': name, 'control': control['name'], 'rule': 'button-caption-clipping'})
            siblings={}
            for control in controls:
                siblings.setdefault(control.get('parent',control['name'].rsplit('/',1)[0]),[]).append(control)
            for values in siblings.values():
                for index,left in enumerate(values):
                    for right in values[index+1:]:
                        if left.get('intentionalOverlay') or right.get('intentionalOverlay'):continue
                        a,b=left['Bounds'],right['Bounds']
                        if min(a['Width'],a['Height'],b['Width'],b['Height'])<=1:continue # Decorative separators.
                        if min(a['Right'],b['Right'])>max(a['Left'],b['Left']) and min(a['Bottom'],b['Bottom'])>max(a['Top'],b['Top']):
                            errors.append({'profile':profile,'file':name,'controls':[left['name'],right['name']],'rule':'sibling-overlap'})
        profiles.append({'profile': profile or '1080p-100', 'scale': manifest['scale'], 'width': manifest['width'], 'height': manifest['height'], 'states': len(expected)})
        for group, names in GROUPS.items():
            if not set(names).issubset(expected):continue # Focused snapshot sets retain the same layout checks.
            width, height = manifest['width'], manifest['height']
            sheet = Image.new('RGB', (width*2, (height+24)*3), '#edf0f3')
            draw = ImageDraw.Draw(sheet)
            for index, name in enumerate(names):
                x, y = (index%2)*width, (index//2)*(height+24)
                draw.text((x+6,y+4), (profile or '1080p-100')+' / '+name, fill='#182430')
                with Image.open(folder/name) as image:sheet.paste(image.convert('RGB'), (x,y+24))
            sheet.save(review/((profile or '1080p-100')+'-'+group+'.png'))
    report = {'profiles': profiles, 'totalStates': len(expected)*len(profile_names), 'controlChecks': checks, 'findings': errors,
        'scope': 'All generated states: image integrity, visible bounds and label height. Scrollable content is intentional; visual review also checks custom controls.'}
    (root/'layout-audit-summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'states': report['totalStates'], 'controlChecks': checks, 'findings': len(errors)}))
    if errors:raise SystemExit('UI review has unresolved layout findings.')

if __name__ == '__main__':main()
