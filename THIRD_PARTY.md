# Third-party notices

## PICO microphone workaround references

- PicoStreamingMicrophoneKeeper, by Kill11: https://github.com/Kill11/PicoStreamingMicrophoneKeeper
- PICO Connect Microphone Latency Accumulation in VRChat, by fz6m: https://gist.github.com/fz6m/337aa44c27a7bde449ccff0e352f3040
- Usage: reference behavior and analysis for the optional PICO microphone keep-alive feature. This repository implements its own C# session lifecycle; the upstream executable and C++ source are not bundled.

## OpenVR Advanced Settings

- Project: OpenVR Advanced Settings
- Source: https://github.com/OpenVR-Advanced-Settings/OpenVR-AdvancedSettings
- License: GNU General Public License v3.0
- Material used: Space Drag behavior and algorithmic structure, controller binding conventions, bundled OpenVR SDK files.
- Changes: removed the Qt and VR overlays and all unrelated settings; implemented a small desktop-only Windows Forms host with translation drag, enable/disable, reset, multiplier, binding UI, and SteamVR auto-launch.

## Valve OpenVR SDK

- Source: https://github.com/ValveSoftware/openvr
- Bundled version: 2.15.6.
- New implementation files: `src/VRPhoneScreenOverlay.SteamVR/OpenVR/openvr_api.cs` and `src/VRPhoneScreenOverlay.SteamVR/Resources/OpenVR/openvr_api.dll`.
- The generated binding has only `#nullable disable` added; both upstream and packaged SHA256 values are fixed in the adjacent manifest.
- License: BSD-3-Clause; see
  `src/VRPhoneScreenOverlay.SteamVR/Resources/OpenVR/LICENSE-VALVE`.

## Vortice.Windows

- Source: https://github.com/amerkoleci/Vortice.Windows
- Bundled NuGet version: 3.8.3 (`Vortice.MediaFoundation` and `Vortice.Direct3D11`).
- Usage: typed Windows Media Foundation, DXGI and D3D11 interop for direct H.264 decode and GPU texture conversion.
- License: MIT; see the bundled `Vortice-LICENSE`.

## scrcpy

- Project: scrcpy
- Source: https://github.com/Genymobile/scrcpy
- Bundled release: 4.1, official Windows x64 package
- Files: `src/VRPhoneScreenOverlay.Android/Resources/Scrcpy/`
- License: Apache License 2.0; see the adjacent `THIRD_PARTY_NOTICE.txt`.
- Usage: USB ADB device discovery, Android video/audio transport, background video decoding, and phone control.
- VRPhoneScreen Overlay uses the matching scrcpy 4.1 server protocol directly and does not package, start, or capture the scrcpy desktop client window.

## Concentus

- Source: https://github.com/lostromb/concentus
- NuGet version: 2.2.2.
- Usage: managed Opus decoding for Android internal audio.
- License: BSD-3-Clause.

## NAudio

- Source: https://github.com/naudio/NAudio
- NuGet version: 3.0.0.
- Usage: bounded PCM buffering and Windows WASAPI default-device playback.
- License: MIT.

Controller reference images were removed when the guide changed to text instructions. Historical source information remains in src/VRPhoneScreenOverlay.App/Assets/Controllers/SOURCES.md; no controller image or controller-image license inventory is distributed in the current application.
