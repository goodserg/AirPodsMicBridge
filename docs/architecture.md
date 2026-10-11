# Architecture and invariants

## Components

| Path | Responsibility |
|---|---|
| `src/BridgeApp.cs` | Single-instance lifecycle, preferences, keyboard gesture queue, tray menus and UI polling |
| `src/CoreAudioAdapter.cs` | Capture discovery, R7 sessions, mute application and cached peak meter |
| `src/UiFeedback.cs` | Transparent tray glyphs, fixed corner OSD and optional card |
| `src/Appearance.cs` | App/taskbar theme detection and themed menu renderer |
| `src/Localization.cs` | Six complete catalogs, Windows UI-language selection and English fallback |
| `build.ps1`, `test.ps1`, `package.ps1` | Local build, checks and distributable artifacts |

## Constraints

- Only a single unambiguous AirPods capture device actively used by `r7-team` can be toggled. No fallback recording device may be substituted.
- Keyboard hooks must not perform COM audio calls or file I/O. Queue gestures, revalidate on the MTA audio worker and apply `SetMute` once.
- Use the same original MMDeviceEnumerator COM coclass in both discovery and meter code. A duplicate class previously caused `E_NOINTERFACE`.
- Read level through `IAudioMeterInformation`; do not open a recording stream. Never persist signal levels or arbitrary keys.
- Muting does not end the R7 session. Exiting and presentation changes must preserve actual mute state.
- All menu, window and NotifyIcon updates happen on the WinForms UI thread. Build a replacement icon cache completely, assign a new tray icon, then dispose the old cache.
- App theme follows `AppsUseLightTheme`; taskbar contrast follows `SystemUsesLightTheme`. Manual app theme must not make the icon unreadable on the real taskbar.
- Presentation changes must not show a disabled card or restart the green hide timer. Unknown device state hides the OSD; known mute persists.
- The OSD stays 48×48 physical pixels at the primary screen origin. Keep system DPI awareness and click-through, nonactivating behaviour.
- Localized tooltip text must fit NotifyIcon's legacy 63-character limit. Unsupported Windows UI languages fall back to English.
- Production data lives under the per-user app-data directory. Development checks override the entire directory, including preferences and logs.
- Never commit personal device logs, local settings or `.codex-chat-history`. Release assets must match their SHA-256 manifests and the tagged source commit.

## Polling

Audio discovery ordinarily runs every 350 ms; level sampling and UI updates run every 100 ms. Theme registry values are checked once per second. A gesture expires after 1,200 ms and is verified against its original endpoint and control generation before any mute change.

## Public access

The personal GitHub repository is public. Publication does not grant collaborators write permission. Binaries are attached to GitHub Releases; build output and local chat archives remain ignored by Git.
