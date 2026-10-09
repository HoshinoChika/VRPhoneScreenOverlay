"""Metadata-only privacy audit of working files and optionally reachable Git blobs.

Never prints matching values or rewrites history. Reports belong in artifacts/.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('public_source', ROOT / 'tools/Prepare-PublicSource.py')
source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source)


def inspect(name, data, markers):
    findings = source.audit([(name, data)])
    normalized = data.replace(b'\0', b'')
    for rule, marker in markers:
        if marker.search(data) or marker.search(normalized):
            findings.append({'file': name, 'rule': rule})
    return findings


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--history', action='store_true')
    parser.add_argument('--marker-file', action='append', default=[])
    args = parser.parse_args()
    output = Path(args.output).resolve()
    if not output.is_relative_to((ROOT / 'artifacts').resolve()):
        raise SystemExit('Report must be under repository artifacts')
    values = source.private_markers(ROOT / 'service.private.json')
    for path in args.marker_file:
        # Explicit local marker inputs are private and are never copied to output.
        values.extend(Path(path).read_text(encoding='utf-8-sig').splitlines())
    username = os.environ.get('USERNAME', '')
    if len(username) >= 4:
        values.append(username)
    markers = [('configured-private-marker', re.compile(b'|'.join(re.escape(value.encode())
        for value in sorted(set(values)) if len(value) >= 4), re.I))] if values else []
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    current = []
    for name in sorted(set(filter(None, names))):
        path = ROOT / name
        if path.is_file():
            current.extend(inspect(name, path.read_bytes(), markers))
    history = []
    blobs = 0
    if args.history:
        objects = subprocess.check_output(['git', 'rev-list', '--objects', '--all'], cwd=ROOT).decode().splitlines()
        # One streaming process; binary object bodies never enter tool output.
        with subprocess.Popen(['git', 'cat-file', '--batch'], cwd=ROOT,
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
            for entry in objects:
                oid, _, name = entry.partition(' ')
                if not name:
                    continue
                process.stdin.write((oid + '\n').encode())
                process.stdin.flush()
                header = process.stdout.readline().decode().strip().split()
                size = int(header[2])
                data = process.stdout.read(size)
                process.stdout.read(1)
                if header[1] == 'blob':
                    blobs += 1
                    history.extend(dict(finding, object=oid) for finding in inspect(name, data, markers))
            process.stdin.close()
            if process.wait() != 0:
                raise SystemExit('Git object audit failed')
    report = {'current_files': len(set(filter(None, names))), 'current_findings': current,
              'history_blobs': blobs, 'history_findings': history,
              'history_rewritten': False,
              'scope': 'Configured private markers and known secret formats; loopback protocol addresses and synthetic examples are not private deployment values.'}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'current_files': report['current_files'], 'current_findings': len(current),
                      'history_blobs': blobs, 'history_findings': len(history)}))
    return bool(current)


if __name__ == '__main__':
    raise SystemExit(main())
