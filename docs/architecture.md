# Architecture and invariants

## Components

| Path | Responsibility |
|---|---|
| `src/BridgeApp.cs` | Single-instance lifecycle, preferences, startup controls, keyboard gesture queue, tray menus and UI polling |
| `src/StartupRegistration.cs` | Per-user/common Startup shortcuts, current-user opt-out and installer commands |
| `src/CoreAudioAdapter.cs` | Capture discovery, active audio sessions, target selection, mute application and cached peak meter |
| `src/UiFeedback.cs` | Transparent tray glyphs, fixed corner OSD and optional card |
| `src/Appearance.cs` | App/taskbar theme detection and themed menu renderer |
| `src/Localization.cs` | Six complete catalogs, Windows UI-language selection and English fallback |
| `src/DiagnosticForm.cs` | Localized microphone/button-test wizard, consumer/telephony Raw Input registration and report export |
| `src/DiagnosticSession.cs` | Bounded JSONL event writer, passive capture-state observer and report lifecycle |
| `src/DiagnosticInventory.cs` | Read-only WMI COM inventory of audio/Bluetooth/HID driver metadata |
| `build.ps1`, `test.ps1`, `package.ps1` | Local build, checks and distributable artifacts |

## Constraints

- Only one unambiguous, error-free AirPods capture device with an active Core Audio capture session can be toggled. No fallback recording device may be substituted.
- Capture eligibility is independent of process name, process lifetime, window, browser or URL. Count every `AudioSessionStateActive` session, including system sessions/PID 0. An active session is not proof of a connected call.
- Unknown session states or discovery errors on any AirPods endpoint fail closed. Gesture revalidation must retain global uniqueness even when an endpoint ID is pinned; pinning does not override eligibility.
- Keyboard hooks must not perform COM audio calls or file I/O. Queue gestures, revalidate on the MTA audio worker and apply `SetMute` once.
- Use the same original MMDeviceEnumerator COM coclass in both discovery and meter code. A duplicate class previously caused `E_NOINTERFACE`.
- Read level through `IAudioMeterInformation`; do not open a recording stream. Never persist signal levels or arbitrary keys.
- System mute changes the selected endpoint for every application using it. It does not synchronize an application's own mute UI or enable a disabled browser microphone track. If applications release all capture sessions, no gesture may toggle the endpoint until capture resumes.
- Exiting and presentation changes must preserve actual mute state.
- All menu, window and NotifyIcon updates happen on the WinForms UI thread. Build a replacement icon cache completely, assign a new tray icon, then dispose the old cache. Do not rebuild or resize an open menu during microphone-level updates.
- App theme follows `AppsUseLightTheme`; taskbar contrast follows `SystemUsesLightTheme`. Manual app theme must not make the icon unreadable on the real taskbar.
- Presentation changes must not show a disabled card or restart the green hide timer. Unknown device state hides the OSD; known mute persists.
- The OSD stays 48×48 physical pixels at the primary screen origin. Keep system DPI awareness and click-through, nonactivating behaviour.
- Localized tooltip text must fit NotifyIcon's legacy 63-character limit. Unsupported Windows UI languages fall back to English.
- Production data lives under the per-user app-data directory. Development checks override the entire directory, including preferences and logs.
- The installer's first scope dialog defaults to current-user installation without elevation; all-users installation requires administrator privileges. The following folder page defaults to `%LOCALAPPDATA%\Programs\AirPodsMicBridge` or `%ProgramFiles%\AirPodsMicBridge`, respectively. `/CURRENTUSER` and `/ALLUSERS` explicitly choose scope.
- The installer offers final launch and startup checkboxes, both enabled by default. Launch must start the actual helper. A portable ZIP does not create startup automatically, but existing startup registration can still apply. Uninstall removes the installation's startup shortcut and preserves the separate per-user data directories.
- Startup is represented by an application-owned per-user or common Startup shortcut with `--autostart`. A tray opt-out from common startup writes a per-user marker without elevation and leaves the common shortcut intact; enabling startup clears that marker. Changing the common shortcut itself through `--startup-enable-all-users` or `--startup-disable-all-users` requires elevation.
- Never commit personal device logs, local settings or `.codex-chat-history`. Release assets must match their SHA-256 manifests and the tagged source commit.

## Polling

Audio discovery ordinarily runs every 350 ms; level sampling and UI updates run every 100 ms. Theme registry values are checked once per second. A gesture expires after 1,200 ms and is verified against its original endpoint and control generation before any mute change.

## Capture selection and hardware delivery

`CoreAudioAdapter.Read` enumerates active Windows capture endpoints and their Core Audio sessions. `HasActiveCapture` and `ActiveSessionCount` reflect session activity without looking up processes. The pure `SelectTarget(rows, pinned)` decision applies the same fail-closed selection rules to discovery and gesture revalidation. AirPods identification uses the Windows device name; the default communications endpoint is not a fallback target.

The media-key hook accepts Windows play/pause commands, rather than identifying which headset or application sent them. A cached eligible state can suppress one command immediately after capture ends; the worker must recheck and cancel the mute change. Separate endpoint observation can keep the muted OSD accurate outside active capture without arming gestures.

The verified delivery setup is AirPods 4 with the MagicAAP driver enabled. The utility does not call or modify that driver. A different Bluetooth path still has to deliver the media command while the microphone is active. Standard audio-session detection does not establish compatibility with every application, driver, exclusive-mode or ASIO capture path; exclusive paths can require device hardware mute.

## Distribution and user data

The Windows 11 x64 installer is the recommended distribution and supports the application's six interface languages. Its compact wizard logo is designed for 100% scaling; the Start menu shortcut uses the brighter application icon. The portable ZIP remains available. Program files are separate from each user's `%LOCALAPPDATA%\AirPodsMicBridge`, which contains preferences and local diagnostics and is retained on uninstall.

Current-user startup uses `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`; all-users startup uses `%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup`. Installer and tray actions share `StartupRegistration` and avoid competing entries. The tray reflects effective startup for the current account, including its opt-out from a common shortcut. The common shortcut passes `--autostart`, allowing the helper to honor that opt-out before opening its normal UI.

Startup checks override both per-user and common Startup directories with isolated fixtures. Installer smoke uses temporary program, shortcut and data directories plus a separate per-user uninstall entry; it does not perform a production all-users installation.

## Headset diagnostics

The tray item or `--diagnostics` opens the same localized wizard in the running instance. The selected microphone may belong to any headset. Six button prompts occur five seconds apart; the UI stops capture at 30 seconds or when the user stops/closes the wizard. Bridge gesture control is paused while diagnostic capture is active, but native headset/driver/application actions still apply.

The keyboard callback records only its fixed media-key whitelist and enqueues events without file I/O. Raw Input is registered only for consumer control and telephony collections; ordinary keyboard/mouse reports are excluded. Consumer/telephony payload hex is bounded to 4,096 bytes. A separate MTA worker reads capture snapshots every 350 ms without opening an audio stream or changing mute; snapshots include device identity, mute and active-session state, never peak levels. Unchanged snapshots are deduplicated.

`DiagnosticInventory.Capture` uses the local `root\cimv2` WMI scripting COM API, requiring no additional managed assembly. `Win32_PnPSignedDriver` is filtered to `MEDIA`, `Bluetooth` and `HIDClass`, plus devices whose name contains `MagicAAP` or whose identifier starts with `ROOT\MAGICAAP`. The same case-insensitive class/device whitelist is rechecked before accepting a row; other `SYSTEM` drivers are excluded. Up to 128 rows contain only DeviceName, Manufacturer, DriverProviderName, DriverVersion, DriverDate, DeviceID, InfName, DeviceClass and IsSigned. Strings are limited to 512 characters. Forward-only, return-immediately query flags reduce retained WMI objects; connection uses its maximum-wait flag. Enumeration has no guaranteed per-step timeout, so inventory runs on an independent background worker that Stop never joins. Unavailable WMI produces a fixed explanatory error and HRESULT instead of failing the diagnostic session.

The JSONL writer has a bounded queue, a 10 MiB output limit and a session-duration safety limit; the wizard uses the shorter 30-second test. Stop requests a bounded writer wait and export becomes available only after the writer closes. Neither a pending audio COM read nor WMI enumeration may delay writer shutdown. Late worker results are discarded after stopping. Tests use isolated data directories and injected state readers without audio recording or mute changes.

Session reports live under the user's data directory in `diagnostics`; the Save dialog copies a completed report to the chosen path. Reports can include personal device names or address-bearing identifiers. No automatic upload occurs, and reports, settings and chat archives must stay outside Git and release assets. These observations describe what Windows exposes to applications; they do not attribute an event to a listed driver, decode private driver traffic or sniff Bluetooth protocol packets. Testing other headsets does not change the AirPods-only control selection rule.

## Public access

The personal GitHub repository is public. Publication does not grant collaborators write permission. Binaries are attached to GitHub Releases; build output and local chat archives remain ignored by Git.
