@echo off
chcp 65001 >nul
setlocal
powershell.exe -NoProfile -Command "if (Get-Process -Name VRPhoneScreenOverlay -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
  echo 请先退出 VRPhoneScreen Overlay，再使用此诊断启动脚本。
  pause
  exit /b 1
)
set "VRPSO_VIDEO_UPLOAD=direct"
start "" "%~dp0..\VRPhoneScreenOverlay.exe"
endlocal
