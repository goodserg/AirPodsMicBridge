# Installer development

Build the application first, then pass the path to an Inno Setup 6 compiler:

```powershell
./build-installer.ps1 -CompilerPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The verified compiler is Inno Setup 6.7.3. The build rejects compiler warnings,
including missing translations. It produces `dist/AirPodsMicBridge-1.3.0-Setup.exe`.
The Indonesian translation is vendored; its source and changes are recorded in
[`languages/README.md`](languages/README.md).

The first dialog selects current-user or all-users installation; current-user is
the default, while all-users requests administrator privileges. The next page
selects the application folder. Default locations are LocalAppData Programs and
Program Files respectively. The final page offers startup and launching the app.
Silent installs enable startup by
default; `/STARTUP=no` disables it. Language codes for `/LANG=` are
`ru`, `en`, `es`, `ptBR`, `pt`, and `id`. `/CURRENTUSER` and `/ALLUSERS` select the
installation scope without its dialog. Upgrade and uninstall request a graceful
shutdown through the application's named stop event and keep its data directory.

Run isolated installation checks without modifying production shortcuts:

```powershell
./test-installer.ps1 -CompilerPath '<ISCC.exe>' -BuildRoot '<temporary task directory>'
```

`build-installer.ps1 -SmokeRoot <temporary fixture directory>` builds a developer
fixture with a different AppId and a `-Smoke.exe` filename. The fixture redirects
installed files, Start Menu links and startup links into that directory and preserves
its `data` directory. Its final Launch checkbox starts a real isolated helper
instance with a separate mutex and stop event. For manual UI checks, set
`AIRPODS_MIC_BRIDGE_DATA` in the invoking process to `<SmokeRoot>\data` before
launching Setup, including its original-user launch broker. `SmokeRoot` must be below `BuildRoot`
or the caller's temporary directory. Tests create transient uninstall registry
entries for the fixture and remove them on successful uninstall.

Automated installer checks use current-user scope for all six languages and verify
a custom installation folder. They do not perform a machine-wide installation or
automate the Windows administrator consent prompt.
