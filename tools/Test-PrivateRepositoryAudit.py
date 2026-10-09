"""Offline checks for the complete repository audit, without real credentials."""
import importlib.util
from pathlib import Path
import re

spec = importlib.util.spec_from_file_location('repository_audit', Path(__file__).with_name('Audit-PrivateRepository.py'))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)
markers = [('configured-private-marker', re.compile(rb'synthetic-private-host\.invalid', re.I))]
for content in (b'https://synthetic-private-host.invalid/api', 'synthetic-private-host.invalid'.encode('utf-16-le')):
    result = audit.inspect('src/example.cs', content, markers)
    assert result and all(set(item) == {'file', 'rule'} for item in result)
    assert not any('synthetic-private-host' in str(item) for item in result)
assert not audit.inspect('src/example.cs', b'http://127.0.0.1:27062/', markers)
assert audit.inspect('src/service.private.json', b'{}', markers)
print('Repository privacy audit validation passed.')
