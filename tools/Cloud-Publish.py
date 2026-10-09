"""Server-side release transaction. Invoked only by Publish-BetaRelease.ps1.

Verifies the user-uploaded package through loopback OpenList; never uploads it. no 115 credentials or temporary URLs
are printed. prepare never switches the public version. Activation is atomic.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path('/var/lib/vrphonescreen-service').resolve()
CONFIG = Path('/etc/vrphonescreen-cloud.json')
VERSION = re.compile(r'^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')


def stream_digest(stream, algorithm):
    digest = hashlib.new(algorithm)
    for block in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(block)
    return digest.hexdigest()


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    os.chmod(temporary, 0o640)
    owner = ROOT.stat()
    os.chown(temporary, owner.st_uid, owner.st_gid)
    os.replace(temporary, path)


def version_key(value):
    core, _, pre = value.partition('-')
    parts = tuple(int(x) for x in core.split('.'))
    prerelease = tuple((0, int(x)) if x.isdecimal() else (1, x) for x in pre.split('.'))
    return parts, not bool(pre), prerelease


class PublicationError(RuntimeError):
    pass


class Cloud:
    def __init__(self):
        self.config = json.loads(CONFIG.read_text())
        endpoint = urllib.parse.urlsplit(self.config['endpoint'])
        if endpoint.scheme != 'http' or endpoint.hostname not in ('127.0.0.1', 'localhost', '::1'):
            raise RuntimeError('OpenList endpoint must be loopback')
        self.token = Path(self.config['tokenFile']).read_text().strip()
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def call(self, method, body):
        request = urllib.request.Request(self.config['endpoint'].rstrip('/') + '/api/fs/' + method,
            data=json.dumps(body).encode(), headers={'Authorization': self.token,
            'Content-Type': 'application/json', 'User-Agent': 'VRPhoneScreenOverlay/update-client'})
        with self.http.open(request, timeout=30) as response:
            raw = response.read(262145)
        if len(raw) > 262144:
            raise RuntimeError('OpenList response exceeds limit')
        return json.loads(raw)

    @staticmethod
    def verify_metadata(data, size, sha1):
        raw = data.get('hashinfo') or {}
        if isinstance(raw, str):
            raw = json.loads(raw)
        hashes = {k.lower(): v.lower() for k, v in raw.items()}
        if data.get('size') != size or hashes.get('sha1') != sha1:
            raise RuntimeError('Cloud file conflict or provider hash mismatch')


def prepare(stage, version):
    manifest = json.loads((stage / 'latest.json').read_text())
    payload = json.loads(base64.b64decode(manifest['payload']))
    metadata = json.loads((stage / 'upload-metadata.json').read_text())
    size = metadata.get('size')
    digest = metadata.get('sha256')
    sha1 = metadata.get('sha1')
    asset = {'path': f'releases/{version}/package.zip', 'size': size, 'sha256': digest}
    record = {'manifest': manifest, 'package': asset}
    if (metadata.get('schemaVersion') != 1 or metadata.get('version') != version or
        metadata.get('path') != asset['path'] or type(size) is not int or
        not 0 < size <= 350 * 1024 * 1024 or
        not isinstance(digest, str) or not re.fullmatch(r'[0-9a-f]{64}', digest) or
        not isinstance(sha1, str) or not re.fullmatch(r'[0-9a-f]{40}', sha1) or
        payload.get('schemaVersion') != 1 or payload.get('packageFormat') != 'full-install-v1' or
        payload.get('version') != version or payload.get('channel') != 'beta' or
        payload.get('packageSize') != size or payload.get('packageSha256') != digest):
        raise RuntimeError('Upload metadata does not match signed manifest')
    catalog = ROOT / 'cloud/releases' / version / 'release.json'
    if catalog.exists() and json.loads(catalog.read_text()) != record:
        raise RuntimeError('A different release already occupies this version')
    cloud = Cloud()
    mount = cloud.config['mountPath'].rstrip('/')
    path = f'{mount}/{asset["path"]}'
    # Refresh the parent first so a newly uploaded version directory can be
    # resolved even when OpenList still holds an older releases listing.
    cloud.call('list', {'path': path.rsplit('/', 2)[0], 'page': 1, 'per_page': 5, 'refresh': True})
    cloud.call('list', {'path': path.rsplit('/', 1)[0], 'page': 1, 'per_page': 5, 'refresh': True})
    uploaded = cloud.call('get', {'path': path, 'password': ''})
    if uploaded.get('code') != 200:
        raise PublicationError('CLOUD_PACKAGE_NOT_UPLOADED')
    if not isinstance(uploaded.get('data'), dict):
        raise PublicationError('CLOUD_PACKAGE_METADATA_MISMATCH')
    try:
        cloud.verify_metadata(uploaded['data'], size, sha1)
    except (RuntimeError, KeyError, TypeError, ValueError):
        raise PublicationError('CLOUD_PACKAGE_METADATA_MISMATCH') from None
    print('CLOUD_USER_PACKAGE_VERIFIED', flush=True)
    atomic_json(catalog, record)
    # The service verifies the immutable signature before it issues any redirect.
    print('CLOUD_RELEASE_PREPARED', version, flush=True)


def activate(stage, version):
    if not (ROOT / 'cloud/releases' / version / 'release.json').is_file():
        raise RuntimeError('Release was not prepared')
    active = ROOT / 'updates/beta/active.json'
    previous = json.loads(active.read_text()) if active.exists() else None
    if previous is not None and version_key(previous['version']) >= version_key(version):
        raise RuntimeError('Refusing a non-increasing release activation')
    atomic_json(stage / 'previous-active.json', previous)
    atomic_json(active, {'version': version})
    print('CLOUD_RELEASE_ACTIVATED', version, flush=True)


def rollback(stage, version):
    active = ROOT / 'updates/beta/active.json'
    if not active.exists() or json.loads(active.read_text()).get('version') != version:
        raise RuntimeError('Active release changed; refusing rollback')
    previous = json.loads((stage / 'previous-active.json').read_text())
    if previous is None:
        active.unlink()
    else:
        atomic_json(active, previous)
    print('CLOUD_RELEASE_ROLLED_BACK', flush=True)


def cleanup(stage, version):
    active = json.loads((ROOT / 'updates/beta/active.json').read_text())
    if active.get('version') != version:
        raise RuntimeError('Only the verified active transaction may clean its local packages')
    for name in ('upload-metadata.json', 'latest.json'):
        (stage / name).unlink(missing_ok=True)
    print('CLOUD_STAGING_PACKAGES_REMOVED', flush=True)


def main():
    import fcntl  # Linux-only execution; pure validation helpers remain testable on Windows.
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('prepare', 'activate', 'rollback', 'cleanup'))
    parser.add_argument('stage')
    parser.add_argument('version')
    args = parser.parse_args()
    stage = Path(args.stage).resolve()
    if not stage.is_relative_to(ROOT) or not stage.name.startswith('.release-cloud-') or not VERSION.fullmatch(args.version):
        raise RuntimeError('Unsafe release transaction scope')
    (ROOT / 'cloud').mkdir(exist_ok=True)
    with (ROOT / 'cloud/publish.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        {'prepare': prepare, 'activate': activate, 'rollback': rollback, 'cleanup': cleanup}[args.action](stage, args.version)


if __name__ == '__main__':
    try:
        main()
    except Exception as failure:
        # Provider bodies and URLs may contain tokens; expose only a stable failure type.
        print('CLOUD_PUBLISH_FAILED', str(failure) if isinstance(failure, PublicationError) else type(failure).__name__, flush=True)
        raise SystemExit(1) from None
