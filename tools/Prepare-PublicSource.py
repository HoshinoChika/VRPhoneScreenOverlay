"""Export an allowlisted, history-free public source snapshot. Never rewrites Git."""
import argparse
import hashlib
import json
import os
import re
import subprocess
import zipfile
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
TOP = {"README.md", "LICENSE", "THIRD_PARTY.md", "VERSION", "global.json", "Directory.Build.props", "Directory.Packages.props", "VRPhoneScreenOverlay.slnx", "action_manifest.json", "build.bat", ".editorconfig", ".gitattributes", ".gitignore", "release.local.example.psd1", "service.config.example.json", "CONTRIBUTING.md", "SECURITY.md"}
DOCS = {"docs/ARCHITECTURE.md","docs/USER_GUIDE.md", "docs/BUILDING.md", "docs/PRIVACY.md", "docs/THIRD_PARTY.md", "docs/RELEASE_NOTES.md", "docs/COMPATIBILITY.md"}
OMIT_TOOLS = {"Capture-Desktop.ps1", "Finish-Task.ps1", "Test-Device.ps1", "Test-PublicRelease.ps1"}
FORBIDDEN = {".git", "bin", "obj", ".vs", "secrets", "archive", "logs", "diagnostics", "generated-files-retained", "node_modules", "__pycache__"}
RULES = {
    "private-key-block": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[A-Za-z0-9+/=\r\n]{80,}-----END"),
    "github-token": re.compile(rb"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})"),
    "cloud-access-key": re.compile(rb"AKIA[0-9A-Z]{16}"),
    "credential-url": re.compile(rb"https?://[^/\s:@]{2,}:[^/\s@]{4,}@"),
}

def readme_marker_data(name, data, public_links=()):
    if name.removeprefix('VRPhoneScreenOverlay-source/') != 'README.md': return data
    for link in public_links:
        uri = urlsplit(link)
        repository = uri.hostname == 'github.com' and re.fullmatch(r'/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?', uri.path)
        if uri.scheme != 'https' or not uri.hostname or uri.username or uri.password or (uri.path != '/' and not repository) or uri.query or uri.fragment:
            raise ValueError('Only an explicitly approved website root or GitHub repository may be shown in README')
        boundary = rb'(?=[\s\)"<>/]|$)' if repository else rb'(?=[\s\)"<>]|$)'
        data = re.sub(re.escape(link.encode()) + boundary, b'[public link]', data)
    return data

def audit(entries, public_links=()):
    findings=[]
    for name,data in entries:
        parts=Path(name).parts
        basename=Path(name).name.casefold()
        if any(p.casefold() in FORBIDDEN for p in parts) or basename in {"release.local.psd1","service.private.json","service.config.json","service.ini","settings.json","input.json",".env"} or (basename.startswith('.env.') and not basename.endswith('.example')):
            findings.append({"file":name,"rule":"private-path"})
        if name.casefold().endswith((".pem",".key",".pfx",".ppk",".p12")) and name.removeprefix('VRPhoneScreenOverlay-source/') != "src/VRPhoneScreenOverlay.Update/Resources/update-public-key.pem":
            findings.append({"file":name,"rule":"private-key-file"})
        for rule,pattern in RULES.items():
            scanned = readme_marker_data(name, data, public_links) if rule == 'private-profile-marker' else data
            if pattern.search(scanned) or pattern.search(scanned.replace(b"\0",b"")):findings.append({"file":name,"rule":rule})
    return findings

def private_markers(path):
    """Read local identifiers only to reject them; reports never include values."""
    if not path.exists():return []
    config=json.loads(path.read_text(encoding='utf-8-sig'))
    result=[]
    def visit(value,key=''):
        if isinstance(value,dict):
            for name,item in value.items():visit(item,name)
        elif isinstance(value,str) and len(value)>=8:
            if value.startswith(('https://','http://')):
                host=urlsplit(value).hostname
                if host and host not in {'localhost','127.0.0.1'}:
                    result.append(value)
                    if key!='ProjectRepositoryUri':result.append(host)
            elif key.lower() in {'serveraddress','password','token','secret','apikey','credential'} or re.match(r'^[A-Za-z]:[\\/]',value):result.append(value)
    visit(config)
    return result


def prepare_first_commit(source, report_directory):
    """Stage an unrelated new repository without creating a commit or copying Git storage."""
    source=Path(source).resolve()
    report_directory=Path(report_directory).resolve()
    if (source/'.git').exists():raise ValueError('Initial repository already exists')
    empty_template=report_directory/'empty-git-template'
    empty_template.mkdir(exist_ok=True)
    git_env={key:value for key,value in os.environ.items() if not key.startswith('GIT_')}
    git_env.update({'GIT_CONFIG_NOSYSTEM':'1','GIT_CONFIG_GLOBAL':os.devnull})
    subprocess.run(['git','init','--initial-branch=main','--template='+str(empty_template),str(source)],env=git_env,check=True,stdout=subprocess.DEVNULL)
    def git(*arguments):
        return subprocess.check_output(['git','-C',str(source),'-c','core.hooksPath='+str(empty_template),
            '-c','commit.gpgsign=false',*arguments],env=git_env).decode().strip()
    git('add','--all')
    head=subprocess.run(['git','-C',str(source),'rev-parse','--verify','HEAD'],env=git_env,capture_output=True)
    if head.returncode==0 or git('rev-list','--all') or git('remote') or git('diff','--name-only'):
        raise RuntimeError('First-commit repository containment check failed')
    if (source/'.git/objects/info/alternates').exists():raise RuntimeError('Shared Git object storage is forbidden')
    result={'commit':None,'commit_count':0,'remotes':0,'staged_file_count':len(git('ls-files').splitlines()),
        'unstaged_changes':False,'first_commit_prepared':True,'committed':False}
    (report_directory/'first-commit-preparation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",required=True)
    parser.add_argument("--private-marker",action="append",default=[])
    parser.add_argument("--marker-file",action="append",default=[])
    parser.add_argument("--prepare-first-commit",action="store_true")
    parser.add_argument("--public-readme-link",action="append",default=[])
    args=parser.parse_args()
    markers=args.private_marker+[os.environ.get("USERNAME","")]+private_markers(ROOT/'service.private.json')
    for marker_file in args.marker_file:
        markers.extend(Path(marker_file).read_text(encoding='utf-8-sig').splitlines())
    markers=[m.encode("utf-8") for m in markers if len(m)>=4]
    if markers:RULES["private-profile-marker"]=re.compile(b"|".join(re.escape(m) for m in markers),re.I)
    dest=Path(args.output).resolve()
    if not dest.is_relative_to((ROOT/"artifacts").resolve()):raise SystemExit("Output must be inside repository artifacts")
    dest.mkdir(parents=True,exist_ok=False)
    source=dest/"VRPhoneScreenOverlay-source";source.mkdir()
    raw=subprocess.check_output(["git","ls-files","-z","--cached","--others","--exclude-standard"],cwd=ROOT)
    names=sorted(set(n for n in raw.decode("utf-8").split("\0") if n))
    selected=[]
    for name in names:
        rel=Path(name)
        if any(p in FORBIDDEN for p in rel.parts):continue
        allowed=name in TOP or name in DOCS or name.startswith(("src/","tests/","third_party/","bindings/","website/",".github/","docs/assets/")) or (name.startswith("tools/") and rel.name not in OMIT_TOOLS)
        if not allowed:continue
        origin=(ROOT/rel).resolve()
        if not origin.is_relative_to(ROOT):raise SystemExit("Unsafe source path")
        if not origin.is_file():continue # Tracked deletions during local preparation.
        data=origin.read_bytes();selected.append((name,data))
    selected.append(("CHANGELOG.md",(ROOT/"docs/RELEASE_NOTES.md").read_bytes()))
    findings=audit(selected, args.public_readme_link)
    report={"version":(ROOT/"VERSION").read_text().strip(),"file_count":len(selected),"old_history_included":False,"rules":list(RULES),"findings":findings,
            "intentional_public_files":["src/VRPhoneScreenOverlay.Update/Resources/update-public-key.pem","third-party licenses and notices"],
            "scope":"Allowlisted current files only. No claim to detect every possible secret format."}
    (dest/"source-audit.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    if findings:raise SystemExit("Source audit failed; see metadata-only report")
    inventory=[]
    for name,data in selected:
        target=source/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
        inventory.append({"path":name,"bytes":len(data),"sha256":hashlib.sha256(data).hexdigest()})
    (dest/"source-files.json").write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding="utf-8")
    archive=dest/("VRPhoneScreenOverlay-"+report["version"]+"-source.zip")
    with zipfile.ZipFile(archive,"w",zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for name,_ in selected:z.write(source/name,"VRPhoneScreenOverlay-source/"+name)
    with zipfile.ZipFile(archive) as z:
        if z.testzip() is not None:raise SystemExit("Invalid source archive")
        if audit(((i.filename,z.read(i)) for i in z.infolist()), args.public_readme_link):raise SystemExit("Archive audit failed")
    first=prepare_first_commit(source,dest) if args.prepare_first_commit else None
    print(json.dumps({"source":str(source),"zip":str(archive),"files":len(selected),"findings":0,"first_commit_preparation":first}))

if __name__=="__main__":main()
