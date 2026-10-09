const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const script = fs.readFileSync('website/hoshinochika/vrphone-download.js', 'utf8');
async function check(payload, ok = true) {
  let handler, destination;
  const attributes = {};
  const link = {textContent:'VRPhoneScreenOverlay', addEventListener: (_, fn) => handler=fn,
    setAttribute:(key,value)=>attributes[key]=value, removeAttribute:key=>delete attributes[key]};
  vm.runInNewContext(script, {document:{querySelectorAll:()=>[link]}, window:{location:{assign:url=>destination=url}},
    fetch:async()=>({ok,json:async()=>({payload:Buffer.from(JSON.stringify(payload)).toString('base64')})}),
    AbortController, setTimeout, clearTimeout, Uint8Array, TextDecoder, atob});
  await handler({preventDefault(){}});
  assert.equal(attributes['aria-busy'], undefined);
  return {destination,link};
}
(async()=>{
  const release={schemaVersion:1,packageFormat:'full-install-v1',channel:'beta',version:'0.2.7',releaseNotes:'更新说明'};
  assert.equal((await check(release)).destination,'/vrphonescreen/api/v1/updates/download/0.2.7');
  for(const changed of [{...release,version:'../secret'},{...release,packageFormat:'unknown'}]) {
    const result=await check(changed); assert.equal(result.destination,undefined); assert.equal(result.link.textContent,'重试下载');
  }
  assert.equal((await check(release,false)).destination,undefined);
  console.log('Website download checks passed: current package, invalid version/format and unavailable server.');
})().catch(error=>{console.error(error);process.exitCode=1;});
