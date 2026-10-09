"use strict";
fetch("release.json", {cache:"no-store"}).then(r => {if(!r.ok)throw new Error();return r.json()}).then(data => {
  document.getElementById("version").textContent = data.version ? "当前版本 · " + data.version : "版本待发布";
  const link = (id,url,label) => {const el=document.getElementById(id);if(!url)return; const parsed=new URL(url,location.href); if(!["https:","http:","mailto:"].includes(parsed.protocol))return; el.href=parsed.href;el.removeAttribute("aria-disabled");if(label)el.textContent=label;};
  link("download-link",data.downloadUrl); if(data.downloadUrl)document.getElementById("download-info").textContent="完整压缩包 · 保留 EXE 与 app 文件夹";
  link("github-link",data.githubUrl,"在 GitHub 查看源码 ↗");link("contact-link",data.contactUrl,data.contactLabel || "联系我们 ↗");
}).catch(() => {document.getElementById("version").textContent="发布信息准备中";});
