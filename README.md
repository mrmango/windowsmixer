# Classic Volume Mixer

A Windows 7-style volume mixer for Windows 10/11 that lives in the system tray.
One click on the tray speaker opens it — no digging through Settings > System > Sound > Volume mixer.

## Features

- **Device column** – master volume and mute for the output device, with a device picker
  ("Default device" follows whatever Windows is using; or pin a specific output).
- **Applications column** – every app with an audio session is detected automatically
  (System Sounds, browsers, Spotify, Teams, games…), with its own icon, slider and mute button.
  Apps appear and disappear live as they open and close.
- **Live level meters** inside each slider, like the original.
- **Tray icon**
  - Left-click: open / close the mixer (pops up next to the tray, hides when you click away)
  - Middle-click: mute / unmute the device
  - Right-click: options menu
  - Hover: shows current volume; icon changes when muted
- **Win+Alt+V** global hotkey to open it from anywhere (can be turned off).
- **Start with Windows** – right-click the tray icon → *Start with Windows*. Uses the per-user
  Run key, so no admin rights. It starts quietly in the tray.
- **Keep open** – tick it to use the mixer as a normal window (stays open, shows in Alt+Tab, remembers position).
- Follows the Windows light/dark app theme, including the title bar.
- Sliders: drag, click the track, mouse wheel (2% steps), or arrow keys / PgUp / PgDn / Home / End.

## Install

1. Copy `ClassicVolumeMixer.exe` somewhere permanent, e.g. `C:\Program Files\ClassicVolumeMixer\`
   or `%LOCALAPPDATA%\ClassicVolumeMixer\`.
2. Run it. Right-click the tray icon → **Start with Windows**.
3. Windows 11 hides new tray icons in the `^` overflow. To keep it on the taskbar,
   drag the icon out of the overflow, or go to *Settings → Personalization → Taskbar →
   Other system tray icons* and switch **Classic Volume Mixer** on.

Requirements: .NET Framework 4.x, which is built into Windows 10 and 11. Nothing else to install.

### SmartScreen note
The prebuilt exe isn't code-signed, so Windows may show "Windows protected your PC" the first time
(More info → Run anyway). If you'd rather not, build it yourself — see below.

## Build from source

Double-click `build.bat`. It uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), so no Visual Studio or SDK is needed.
Output goes to `dist\ClassicVolumeMixer.exe`.

## Uninstall

1. Right-click the tray icon → untick *Start with Windows* → *Exit*.
2. Delete the exe.
3. Optional: delete the registry key `HKEY_CURRENT_USER\Software\ClassicVolumeMixer` (settings).

## How it works

Talks directly to the Windows Core Audio API (WASAPI): `IMMDeviceEnumerator` for output devices,
`IAudioEndpointVolume` for the master slider, `IAudioSessionManager2` / `ISimpleAudioVolume` for
per-app volume, and `IAudioMeterInformation` for the level meters. Sessions are grouped per
executable, so apps that open several audio streams show as one slider.

| File | Purpose |
|---|---|
| `src/CoreAudio.cs` | COM interop definitions for the Windows audio APIs |
| `src/Mixer.cs` | Devices, app sessions, names and icons |
| `src/Controls.cs` | Slider with level meter, mute button, channel column, group frames |
| `src/MixerForm.cs` | The mixer window, layout, live refresh, popup behaviour |
| `src/Program.cs` | Tray icon, menu, hotkey, settings, start-with-Windows, single instance |

Settings are stored in `HKCU\Software\ClassicVolumeMixer`; the startup entry is
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClassicVolumeMixer`.
