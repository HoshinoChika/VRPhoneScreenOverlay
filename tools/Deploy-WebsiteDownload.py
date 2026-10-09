"""Patch only the existing site's download link and its small browser script."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys


def patched_html(html, version, script_hash):
    pattern = re.compile(r'<a\b([^>]*\bclass="download-link"[^>]*)>(.*?)</a>', re.S)
    if len(pattern.findall(html)) != 1:
        raise ValueError('Expected exactly one download link')
    def replace(match):
        attrs = re.sub(r'\s+(?:target|rel|title)="[^"]*"', '', match[1])
        attrs = re.sub(r'\bhref="[^"]*"', f'href="/vrphonescreen/api/v1/updates/download/{version}"', attrs)
        if 'data-vrphone-download' not in attrs:
            attrs += ' data-vrphone-download'
        return f'<a{attrs} title="下载最新版 VRPhoneScreenOverlay">{match[2]}</a>'
    html = pattern.sub(replace, html)
    tag = f'<script src="/photo-assets/vrphone-download.js?v={script_hash[:12]}" defer></script>'
    existing = re.compile(r'<script\b[^>]*src="/photo-assets/vrphone-download\.js[^"\s]*"[^>]*></script>')
    if existing.search(html):
        return existing.sub(tag, html)
    if html.count('</body>') != 1:
        raise ValueError('Expected one closing body')
    return html.replace('</body>', tag + '\n</body>')


def main():
    action, stage_text, version, expected_hash = sys.argv[1:]
    stage = Path(stage_text).resolve()
    base = Path('/opt/photo-wall')
    if stage.parent != base or not re.fullmatch(r'\.vrphone-download-[0-9a-f]{32}', stage.name):
        raise ValueError('Unsafe deployment stage')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', version):
        raise ValueError('Invalid version')
    static = (base / 'current').resolve() / 'static'
    if not static.is_relative_to(base / 'releases'):
        raise ValueError('Unexpected site root')
    index = static / 'index.html'
    asset = static / 'vrphone-download.js'
    if index.is_symlink() or asset.is_symlink():
        raise ValueError('Unexpected symbolic link')
    record_path = stage / 'deployment.json'
    if action == 'rollback':
        record = json.loads(record_path.read_text())
        if hashlib.sha256(index.read_bytes()).hexdigest() != record['newIndexSha256']:
            raise ValueError('Site changed after deployment; refusing rollback')
        shutil.copy2(stage / 'original-index.html', index)
        if (stage / 'original-script.js').exists():
            shutil.copy2(stage / 'original-script.js', asset)
        else:
            asset.unlink(missing_ok=True)
        print('WEBSITE_DOWNLOAD_ROLLED_BACK')
        return
    if action != 'install':
        raise ValueError('Invalid operation')
    data = (stage / 'vrphone-download.js').read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != expected_hash:
        raise ValueError('Browser script hash mismatch')
    original = index.read_bytes()
    result = patched_html(original.decode('utf-8'), version, digest).encode('utf-8')
    shutil.copy2(index, stage / 'original-index.html')
    if asset.exists():
        shutil.copy2(asset, stage / 'original-script.js')
    record_path.write_text(json.dumps({'newIndexSha256': hashlib.sha256(result).hexdigest()}))
    for destination, content in ((asset, data), (index, result)):
        temporary = destination.with_name(destination.name + '.vrphone-new')
        temporary.write_bytes(content)
        os.chmod(temporary, 0o644)
        os.replace(temporary, destination)
    print('WEBSITE_DOWNLOAD_INSTALLED', version)


if __name__ == '__main__':
    main()
