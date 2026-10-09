# /vrpso 官网准备稿

纯 HTML/CSS/JS，无外部字体、分析脚本或第三方请求。仅本地预览，尚未部署。

运行仓库 tools/Prepare-Website.ps1，传入已验证的用户 ZIP，生成 artifacts/public-website/vrpso。它从 VERSION 生成 release.json，并将 ZIP 放在 downloads/，无需在网页代码里手填版本。

发布前填写 release.json 中 githubUrl、contactUrl、contactLabel；删除“发布前预览”提示并按需要调整 robots/noindex。没有提供的入口保持不可点击占位，不使用假联系方式。先检查所有下载链接与 SHA256，再另行安排部署。不要把私有发布配置或密钥放进本站。
