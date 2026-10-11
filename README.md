# AirPods Mic Bridge

Windows tray utility that turns the AirPods play/pause gesture into a system microphone mute toggle while **R7 Team** uses the AirPods microphone.

[Русская документация](README.ru.md) · [Download releases](https://github.com/goodserg/AirPodsMicBridge/releases) · [Changes](CHANGELOG.md)

## Use

1. Download a release ZIP, extract it, and run `AirPodsMicBridge.exe`. Windows 10/11 x64 and .NET Framework 4.x are required. Installation and administrator privileges are not required.
2. Connect AirPods and select their microphone in an R7 Team call.
3. Press the AirPods stem once to mute; press again to unmute. The gesture must arrive in Windows as a play/pause command.

The tray microphone has a transparent background. Its capsule fills with the current microphone signal level; no audio is recorded. Red with an X means muted, an outline means enabled, and grey means unavailable or control paused.

A red **48×48 physical pixel** indicator stays at the top-left corner of the primary screen while the observed AirPods microphone is muted. After unmuting, a green indicator appears for 2.2 seconds. Indicators pass clicks through and do not take focus.

Right-click the tray icon to choose:

- Microphone control on/off.
- Optional notification card, **off by default**.
- Theme: **Automatic (Windows)**, Light, Dark.
- Language: **Automatic (Windows)**, Русский, English, Español, Português (Brasil), Português, Bahasa Indonesia.
- Exit.

Automatic language follows the Windows UI language, using English for unsupported languages. Region-specific Brazilian Portuguese takes precedence over Portuguese. Changes apply immediately and are saved. Automatic menu/card theme follows the Windows app theme; tray contrast follows the Windows taskbar theme even when they differ. Windows theme changes are checked once per second. The red/green corner indicator retains its fixed state colours.

![Light and dark appearance](assets/theme-preview.png)

Appearance examples; the optional card is enabled for this preview.

## Behaviour and compatibility

- Mute affects the selected Windows recording device and every app using it. R7 Team's own mute button may display a different state.
- Control is armed only while a process named `r7-team` actively uses an AirPods capture device. The R7 microphone settings test can also activate it. Another microphone is never selected as a fallback.
- All Windows play/pause commands are eligible while control is armed, including commands from another keyboard, headset or app. Outside this condition, media playback works normally. A recent cached state can suppress one command immediately after microphone use ends; the worker rechecks and cancels that toggle.
- If several AirPods microphones match, the utility waits for an unambiguous active target.
- If R7's own mute button releases its capture device, enable that button first, then use AirPods for system mute.
- The muted corner indicator also follows external mute changes outside an active R7 call, whenever one connected AirPods microphone can be identified.
- Exiting or updating the utility preserves the microphone's current mute state. No automatic startup entry is added.

Tested with AirPods 4, R7 Team desktop 0.1.289, and MagicAAP. Operation with the MagicPods app closed was verified; MagicAAP remained enabled. The utility calls standard Windows APIs and does not call MagicAAP directly. Operation without that driver depends on the Bluetooth path delivering a detectable play/pause command during microphone use; that case has **not** been verified. The utility does not modify MagicPods, Bluetooth drivers or R7 Team files.

## Settings and diagnostics

Preferences: `%LOCALAPPDATA%\AirPodsMicBridge\settings.json`.

Local diagnostic logs: `%LOCALAPPDATA%\AirPodsMicBridge\logs`. They contain audio-device names, identifiers and state changes, but no recordings, signal-level history or arbitrary keyboard input. No data is uploaded. `AIRPODS_MIC_BRIDGE_DATA` can override the whole data directory for development/testing.

Stop a running copy:

```powershell
.\AirPodsMicBridge.exe --stop
```

## Build and checks

```powershell
.\build.ps1
.\test.ps1
.\package.ps1
```

The build uses the Windows .NET Framework C# compiler (C# 5, x64), without external packages or network downloads. Default output is `dist\AirPodsMicBridge`. Temporary build files use the operating system's temporary directory; `-BuildRoot` overrides it. Tests run without microphone changes or recording. Packaging creates the ZIP and SHA-256 manifests under `dist`.

The system DPI-aware manifest prevents Windows from enlarging the 48×48 corner indicator on the primary monitor. See [architecture](docs/architecture.md) for threading and device-selection rules.

This repository is public for viewing and downloading. Direct write access is limited to its owner and explicitly authorised maintainers; no additional collaborators are granted access by publication. The corner microphone drawing was supplied as a UI reference and reproduced for this utility. This project is independent of Apple, MagicPods and R7 Office.
