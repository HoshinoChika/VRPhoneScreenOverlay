# Customized scrcpy server

The bundled Android server is built from upstream scrcpy `v4.1` at commit
`2926c06c5dc3064ae6d8db706f1a98a37cfcf3f0` under the Apache License 2.0.

`vrphonescreen-user-activity.patch` adds two empty control messages:

- `127` refreshes Android user activity through `Device.keepActive(displayId)`.
- `128` handles an explicit guarded wake on the existing control thread. It reads
  PowerManager interaction state, wakes only a sleeping device, refreshes activity,
  and reasserts physical-panel-off in a finally block. It never requests panel-on
  and does not schedule a delayed off that could outlive disabling screen guard.

PowerManager interaction state is separate from the physical panel power override.
An already interactive phone needs no wake key when grabbed. Restoring a truly
sleeping phone still involves Android and device-specific display transitions;
zero visible flash during that transition requires real-device validation.

No video, audio, clipboard, normal input, timer, thread or queue is added by message
128. The existing PC guard loop remains responsible for its 100 ms panel-off pulse;
message 127 is stateless, so stopping it restores Android's normal timeout behavior.
Normal session cleanup retains the upstream physical display restoration.

Build with `tools/Build-Custom-ScrcpyServer.ps1`, supplying a clean checkout of the
pinned commit, a JDK and the Android SDK. The helper explicitly selects the source
project directory and runs server unit tests and release assembly.
This change was built with JDK 21, the base Android 36 platform (not an SDK extension
platform), build-tools 36.0.0 and the pinned Gradle 9.3.1 wrapper. Build caches stay in
ignored `artifacts/android-build/`; they are not distributed.

The resource manifest records both the patch SHA256 and the built server SHA256.
`Verify.ps1` checks these and both extension ids. Runtime validation rejects a
manifest without message 128 even if its old server matches its own old hash.
