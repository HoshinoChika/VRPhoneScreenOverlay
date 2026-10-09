"""Offline checks for source-export privacy boundaries, using synthetic data only."""
import importlib.util
import json
import tempfile
from pathlib import Path

spec = importlib.util.spec_from_file_location('public_source', Path(__file__).with_name('Prepare-PublicSource.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
assert module.audit([('service.private.json', b'{}')])
assert module.audit([('src/service.config.json', b'{}')])
assert module.audit([('secrets/ssh.key', b'synthetic')])
assert module.audit([('src/Service.Private.JSON', b'{}')])
assert module.audit([('src/.env.production', b'synthetic')])
assert module.audit([('src/signing.P12', b'synthetic')])
assert not module.audit([('service.config.example.json', b'{}')])
assert not module.audit([('src/VRPhoneScreenOverlay.Update/Resources/update-public-key.pem', b'-----BEGIN PUBLIC KEY-----')])
assert not module.audit([('VRPhoneScreenOverlay-source/src/VRPhoneScreenOverlay.Update/Resources/update-public-key.pem', b'-----BEGIN PUBLIC KEY-----')])
assert module.audit([('src/other/Resources/update-public-key.pem', b'synthetic')])
with tempfile.TemporaryDirectory(prefix='source-audit-') as directory:
    config = Path(directory)/'service.private.json'
    config.write_text(json.dumps({'Client': {'UpdateManifestUri': 'https://private-example.invalid/manifest',
        'ProjectRepositoryUri': 'https://github.com/example/project'}, 'Deployment': {'ServerAddress': '203.0.113.7', 'Token': 'synthetic-maintenance-secret'}}))
    markers = module.private_markers(config)
    assert 'private-example.invalid' in markers
    assert '203.0.113.7' in markers
    assert 'synthetic-maintenance-secret' in markers
    assert 'github.com' not in markers
print('Public source privacy-boundary validation passed.')
assert module.readme_marker_data('README.md', b'[site](https://site.invalid/)', ['https://site.invalid/']) == b'[site]([public link])'
assert b'https://site.invalid/api' in module.readme_marker_data('README.md', b'https://site.invalid/api', ['https://site.invalid/'])
assert b'https://site.invalid/' in module.readme_marker_data('src/client.cs', b'https://site.invalid/', ['https://site.invalid/'])
assert b'github.com' not in module.readme_marker_data('README.md', b'https://github.com/example/project/releases', ['https://github.com/example/project'])
