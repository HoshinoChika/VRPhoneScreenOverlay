"""Verify a first-commit export is unrelated to the private repository."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('public_source',ROOT/'tools/Prepare-PublicSource.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
original=subprocess.check_output(['git','rev-parse','main'],cwd=ROOT)
parent=ROOT/'artifacts/first-commit-tests';parent.mkdir(parents=True,exist_ok=True)
with tempfile.TemporaryDirectory(dir=parent) as temporary:
    report=Path(temporary)
    source=report/'source';source.mkdir()
    (source/'README.md').write_text('# Synthetic first commit\n',encoding='utf-8')
    result=module.prepare_first_commit(source,report)
    assert result['commit_count']==0 and result['first_commit_prepared'] and not result['committed']
    assert not (source/'.git/objects/info/alternates').exists()
    assert not subprocess.check_output(['git','remote','-v'],cwd=source).strip()
    assert subprocess.run(['git','rev-parse','--verify','HEAD'],cwd=source,capture_output=True).returncode!=0
    assert json.loads((report/'first-commit-preparation.json').read_text())['commit'] is None
    try:module.prepare_first_commit(source,report)
    except ValueError:pass
    else:raise AssertionError('An existing repository was reinitialized')
assert original==subprocess.check_output(['git','rev-parse','main'],cwd=ROOT)
print('First-commit preparation validation passed.')
