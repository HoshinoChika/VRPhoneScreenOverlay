"""One-time root-run setup for a configured OpenList instance.
Creates only /VRPSO/releases and /VRPSO/diagnostics and its own /vrpso mount.
Never prints provider credentials. Does not restart the production service.
"""
import sqlite3,json,pathlib,urllib.request,urllib.parse,os,subprocess,argparse
parser=argparse.ArgumentParser()
parser.add_argument('--configuration',required=True)
args=parser.parse_args()
private=json.loads(pathlib.Path(args.configuration).read_text())['Deployment']['CloudSetup']
endpoint=private['OpenListEndpoint'].rstrip('/')
address=urllib.parse.urlsplit(endpoint)
if address.scheme!='http' or address.hostname!='127.0.0.1' or address.username or address.password:
 raise RuntimeError('Configure a private local OpenList endpoint')
base=pathlib.Path('/var/lib/vrphonescreen-service')
backup=base/'cloud/setup-backup';backup.mkdir(parents=True,exist_ok=True)
owner=base.stat();os.chown(base/'cloud',owner.st_uid,owner.st_gid);os.chmod(base/'cloud',0o700)
c=sqlite3.connect('file:'+private['DatabasePath']+'?mode=ro',uri=True);c.row_factory=sqlite3.Row
source=dict(c.execute("select * from x_storages where mount_path=?",(private['SourceMount'],)).fetchone())
addition=json.loads(source['addition']);token=c.execute("select value from x_setting_items where key='token'").fetchone()[0]
op=urllib.request.build_opener(urllib.request.ProxyHandler({}))
headers={'Cookie':addition['cookie'],'User-Agent':'Mozilla/5.0'}
def provider(url,data=None):
 return json.load(op.open(urllib.request.Request('https://webapi.115.com/'+url,data=None if data is None else urllib.parse.urlencode(data).encode(),headers=headers),timeout=25))
listing=provider('files?aid=1&cid=0&limit=100&show_dir=1')
assert listing.get('state')
roots=[x for x in listing['data'] if x.get('n')=='VRPSO' and 'fid' not in x]
if len(roots)>1:raise RuntimeError('Ambiguous VRPSO roots')
if roots:cid=roots[0]['cid']
else:
 created=provider('files/add',{'pid':'0','cname':'VRPSO'});assert created.get('state');cid=created['cid']
def api(route,data):
 return json.load(op.open(urllib.request.Request(endpoint+'/api/'+route,data=json.dumps(data).encode(),headers={'Authorization':token,'Content-Type':'application/json'}),timeout=30))
existing=c.execute("select addition from x_storages where mount_path='/vrpso'").fetchone()
if existing:
 assert str(json.loads(existing[0])['root_folder_id'])==str(cid)
else:
 addition['root_folder_id']=str(cid);addition['limit_rate']=1
 source.update(id=0,mount_path='/vrpso',addition=json.dumps(addition),disabled=False,disable_index=True,web_proxy=False,enable_sign=True,remark='VRPSO software releases and monthly diagnostic archives')
 source.pop('modified',None);source.pop('status',None)
 for field in ['disabled','disable_index','web_proxy','enable_sign','proxy_range','disable_proxy_sign']:source[field]=bool(source.get(field,False))
 result=api('admin/storage/create',source)
 if result.get('code')!=200:raise RuntimeError('Storage creation rejected: '+str(result.get('message',''))[:160])
for directory in ['/vrpso/releases','/vrpso/diagnostics']:
 assert api('fs/mkdir',{'path':directory}).get('code')==200
secret=pathlib.Path('/etc/vrphonescreen-openlist.token');secret.write_text(token);os.chmod(secret,0o600)
config=pathlib.Path('/etc/vrphonescreen-cloud.json');config.write_text(json.dumps({'endpoint':endpoint+'/','tokenFile':str(secret),'mountPath':'/vrpso'}));os.chmod(config,0o600)
dropin=pathlib.Path('/etc/systemd/system/vrphonescreen.service.d/30-cloud-storage.conf');dropin.parent.mkdir(exist_ok=True)
dropin_content=('[Service]\nLoadCredential=cloud-token:/etc/vrphonescreen-openlist.token\nEnvironment=VRPSO_OPENLIST_URL='+endpoint+'/\nEnvironment=VRPSO_OPENLIST_TOKEN_FILE=%d/cloud-token\nEnvironment=VRPSO_OPENLIST_MOUNT=/vrpso\nUMask=0077\n')
if dropin.exists() and dropin.read_text()!=dropin_content:raise RuntimeError('Existing cloud drop-in differs; review before overwriting')
dropin.write_text(dropin_content)
subprocess.run(['systemctl','daemon-reload'],check=True)
print('115 directories ready: /VRPSO/releases and /VRPSO/diagnostics')
print('Private service configuration prepared; running service has not been restarted')
