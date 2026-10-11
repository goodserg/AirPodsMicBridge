# Changelog

## 1.3.0 — 2026-10-11

- Application-independent microphone control: any active Windows capture session on an unambiguous AirPods device can arm the play/pause gesture, including browser and system sessions.
- No process-name lookup, window detection or website allowlist. Active capture is the condition; it does not prove a connected call.
- Device selection and gesture revalidation reject ambiguous active devices, unknown session states and AirPods discovery errors. A pinned endpoint does not bypass these checks.
- Updated English/Russian documentation with system-wide mute effects, application/site mute differences, capture-release behaviour and hardware-delivery limitations.
- Existing tray level, persistent 48×48 muted indicator, optional card, themes and languages are preserved.
- Recommended Windows 11 x64 installer in six languages, with a compact wizard logo at 100% scaling and a brighter Start menu icon. The first dialog offers current-user installation by default or all-users installation with elevation; the next page selects the folder.
- Final launch and Start with Windows checkboxes are enabled by default. `/CURRENTUSER` and `/ALLUSERS` select the installer scope from the command line.
- Startup uses a per-user or common Startup shortcut with `--autostart`. The tray can opt out the current user from common startup without elevation, while retaining the shortcut for other users; enabling startup clears that opt-out. Privileged commands manage the common shortcut directly.
- The portable ZIP does not create startup automatically; an existing registration can remain active. Uninstall removes the installation's startup shortcut and preserves user settings and diagnostics.
- Tray menus remain stable while microphone-level icons update, avoiding menu jitter.
- Six-language headset diagnostics wizard: microphone selection, six button prompts five seconds apart, automatic stop after 30 seconds, manual stop and JSONL export to a chosen path. Microphone control is paused during capture.
- Local reports record exposed Windows media/consumer-telephony HID events, microphone state and bounded audio/Bluetooth/HID driver metadata, including a separate MagicAAP device match across other classes. No audio, signal-level history or ordinary keyboard input is recorded; reports are never uploaded automatically or included in release assets. Testing another headset does not enable microphone control for it.

The verified hardware delivery path remains AirPods 4 with MagicAAP enabled. Operation without that driver and exclusive/ASIO capture paths are not verified; application-independent selection is not a claim that every application has been physically tested.

## 1.2.0 — 2026-10-11

- Light and dark menu/card themes, with automatic Windows app-theme detection and manual selection.
- Tray contrast follows the taskbar theme, including mixed Windows themes.
- Windows UI-language detection and immediate selection of Russian, English, Spanish, Brazilian Portuguese, Portuguese and Indonesian.
- Saved language/theme choices; existing notification-card settings are preserved.
- Portable per-user settings and diagnostic storage; no fixed drive or machine-specific path is required.
- Public repository, build/check/package scripts, versioned release ZIP and SHA-256 checksums.

The microphone selection, call gating, mute toggle, level sampling, persistent red 48×48 indicator and default-disabled card retain the behaviour verified in 1.1.1.

## 1.1.1 — 2026-10-11

- Transparent outline microphone tray icon with signal-level fill inside its capsule.
- Flat 48×48 corner indicator matching the supplied reference, with no screen margin.
- System DPI awareness to prevent additional Windows scaling.

## 1.1.0 — 2026-10-11

- Live microphone-level tray indication without opening a recording stream.
- Persistent muted indicator and temporary unmuted indicator.
- Optional notification card, disabled by default.
