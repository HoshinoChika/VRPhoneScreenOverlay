import base64
from contextlib import contextmanager
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('cloud_publish', Path(__file__).with_name('Cloud-Publish.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CloudStub:
    config = {'mountPath': '/synthetic'}
    verify_metadata = staticmethod(module.Cloud.verify_metadata)

    def __init__(self, data):
        self.data = data
        self.calls = []

    def call(self, method, body):
        self.calls.append((method, body))
        if method == 'list':
            return {'code': 200}
        if method == 'get':
            return self.data
        raise AssertionError('Publishing must never upload, mkdir, or modify the cloud package')


@contextmanager
def fixture():
    parent = Path(__file__).resolve().parent.parent / 'artifacts/manual-cloud-publication-tests'
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=parent) as directory:
        root = Path(directory)
        stage = root / '.release-cloud-test'
        stage.mkdir()
        content = b'synthetic-user-uploaded-package'
        version = '1.0.0'
        metadata = {'schemaVersion': 1, 'version': version, 'path': f'releases/{version}/package.zip',
                    'size': len(content), 'sha256': hashlib.sha256(content).hexdigest(),
                    'sha1': hashlib.sha1(content).hexdigest()}
        payload = {'schemaVersion': 1, 'packageFormat': 'full-install-v1', 'channel': 'beta',
                   'version': version, 'packageSize': metadata['size'], 'packageSha256': metadata['sha256']}
        manifest = {'payload': base64.b64encode(json.dumps(payload).encode()).decode(), 'signature': 'synthetic'}
        (stage / 'latest.json').write_text(json.dumps(manifest), encoding='utf-8')
        (stage / 'upload-metadata.json').write_text(json.dumps(metadata), encoding='utf-8')
        cloud = CloudStub({'code': 200, 'data': {'size': len(content), 'hashinfo': {'sha1': metadata['sha1']}}})

        def atomic(path, value):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(value), encoding='utf-8')

        with patch.object(module, 'ROOT', root), patch.object(module, 'Cloud', return_value=cloud), patch.object(module, 'atomic_json', atomic):
            yield root, stage, version, metadata, cloud


class PublishingTests(unittest.TestCase):
    def test_user_uploaded_package_without_any_server_zip_or_cloud_write(self):
        with fixture() as (root, stage, version, metadata, cloud):
            self.assertFalse((stage / 'package.zip').exists())
            module.prepare(stage, version)
            self.assertEqual(['list', 'list', 'get'], [method for method, _ in cloud.calls])
            self.assertEqual('/synthetic/releases', cloud.calls[0][1]['path'])
            self.assertEqual('/synthetic/releases/1.0.0', cloud.calls[1][1]['path'])
            self.assertTrue(all(body['refresh'] for method, body in cloud.calls if method == 'list'))
            self.assertEqual('/synthetic/releases/1.0.0/package.zip', cloud.calls[-1][1]['path'])
            record = json.loads((root / 'cloud/releases/1.0.0/release.json').read_text())
            self.assertEqual({'manifest', 'package'}, set(record))
            self.assertEqual(metadata['sha256'], record['package']['sha256'])

    def test_missing_upload_does_not_publish_catalog_or_change_active_version(self):
        with fixture() as (root, stage, version, metadata, cloud):
            cloud.data = {'code': 500}
            active = root / 'updates/beta/active.json'
            active.parent.mkdir(parents=True)
            active.write_text('{"version":"0.9.0"}')
            with self.assertRaisesRegex(module.PublicationError, 'CLOUD_PACKAGE_NOT_UPLOADED'):
                module.prepare(stage, version)
            self.assertFalse((root / 'cloud/releases/1.0.0/release.json').exists())
            self.assertEqual({'version': '0.9.0'}, json.loads(active.read_text()))

    def test_wrong_user_upload_is_rejected_before_catalog(self):
        for data in [{'size': 1, 'hashinfo': {}}, {'size': 31, 'hashinfo': {'sha1': 'wrong'}}, None]:
            with self.subTest(data=data), fixture() as (root, stage, version, metadata, cloud):
                cloud.data = {'code': 200, 'data': data}
                with self.assertRaises(module.PublicationError):
                    module.prepare(stage, version)
                self.assertFalse((root / 'cloud/releases/1.0.0/release.json').exists())

    def test_metadata_cannot_redirect_to_another_version_or_change_signed_digest(self):
        for name, value in [('path', 'releases/other/package.zip'), ('sha256', 'a' * 64), ('sha1', 'invalid'), ('size', 0)]:
            with self.subTest(name=name), fixture() as (root, stage, version, metadata, cloud):
                metadata[name] = value
                (stage / 'upload-metadata.json').write_text(json.dumps(metadata))
                with self.assertRaises(RuntimeError):
                    module.prepare(stage, version)
                self.assertEqual([], cloud.calls)
                self.assertFalse((root / 'cloud/releases/1.0.0/release.json').exists())

    def test_existing_different_catalog_is_not_overwritten(self):
        with fixture() as (root, stage, version, metadata, cloud):
            catalog = root / 'cloud/releases/1.0.0/release.json'
            catalog.parent.mkdir(parents=True)
            catalog.write_text('{"different":true}')
            with self.assertRaises(RuntimeError):
                module.prepare(stage, version)
            self.assertEqual({'different': True}, json.loads(catalog.read_text()))
            self.assertEqual([], cloud.calls)

    def test_rollback_restores_only_the_recorded_active_test(self):
        with fixture() as (root, stage, version, metadata, cloud):
            active = root / 'updates/beta/active.json'
            active.parent.mkdir(parents=True)
            active.write_text(json.dumps({'version': '0.2.8-download-test.1'}))
            previous = {'version': '0.2.6-beta.13'}
            (stage / 'previous-active.json').write_text(json.dumps(previous))
            with patch.object(module, 'atomic_json', lambda path, data: path.write_text(json.dumps(data))):
                module.rollback(stage, '0.2.8-download-test.1')
            self.assertEqual(previous, json.loads(active.read_text()))

    def test_rollback_cannot_remove_another_active_release(self):
        with fixture() as (root, stage, version, metadata, cloud):
            active = root / 'updates/beta/active.json'
            active.parent.mkdir(parents=True)
            original = {'version': '0.2.8'}
            active.write_text(json.dumps(original))
            (stage / 'previous-active.json').write_text('{"version":"0.2.6-beta.13"}')
            with self.assertRaises(RuntimeError):
                module.rollback(stage, '0.2.8-download-test.1')
            self.assertEqual(original, json.loads(active.read_text()))

    def test_version_order(self):
        versions = ['0.2.6-beta.9', '0.2.6-beta.10', '0.2.6-beta.11', '0.2.6', '0.2.7']
        self.assertEqual(versions, sorted(reversed(versions), key=module.version_key))

    def test_version_path_boundary(self):
        for value in ['../x', '0.2.6/../x', '0.2.6;rm', '0.2.6 beta']:
            self.assertIsNone(module.VERSION.fullmatch(value))

    def test_provider_hash_formats(self):
        for value in [{'SHA1': 'ABC'}, '{"sha1":"ABC"}']:
            module.Cloud.verify_metadata({'size': 12, 'hashinfo': value}, 12, 'abc')

    def test_conflicts_reject(self):
        for data in [{'size': 13, 'hashinfo': {'sha1': 'abc'}}, {'size': 12, 'hashinfo': {}}, {'size': 12, 'hashinfo': {'sha1': 'wrong'}}]:
            with self.assertRaises(RuntimeError):
                module.Cloud.verify_metadata(data, 12, 'abc')


if __name__ == '__main__':
    unittest.main()
