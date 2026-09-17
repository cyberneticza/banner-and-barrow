# Distributing Banner & Barrow

Goal: a player downloads one file, double-clicks it, and the game installs into their own Windows profile. No
admin prompt, no .NET install, Start Menu and Desktop shortcuts, a normal uninstall, and ideally updates.

## What works today

`pwsh scripts/publish.ps1 [-Version 0.2.0] [-Zip]` builds a **self-contained** release in
`artifacts/publish/win-x64` (about 75 MB). It runs on a clean Windows machine without .NET installed:

```
BannerAndBarrow.exe   the game (.NET runtime bundled in)
SDL2.dll, openal.dll native libraries MonoGame loads by path; they must stay next to the exe
Content/             compiled fonts and art
config/              balance, map and AI tuning
Assets/              sprite mapping
```

Two things were found while testing this:

- Bundling the native libraries into the exe (`IncludeNativeLibrariesForSelfExtract=true`) breaks MonoGame
  DesktopGL with `Failed to load library: SDL2.dll`. The script keeps them as loose files.
- The game only reads from its install folder. Player settings go to `%APPDATA%\BannerAndBarrow\settings.json`, so
  a per-user install folder that gets replaced on update is fine.

`-Zip` produces a portable zip, which is the simplest thing to hand to a tester: unzip anywhere and run.

## Options for an installer

| Option | Per-user, no admin | Shortcuts and uninstall | Updates | Effort |
|---|---|---|---|---|
| **Velopack** | Yes: `%LocalAppData%\BannerAndBarrow` | Start Menu and Desktop, Apps & Features | Built in (delta updates from a folder, GitHub Releases or any static host) | Low: one NuGet package, one line in `Program.cs`, one `vpk pack` command |
| Inno Setup | Yes, with `PrivilegesRequired=lowest` and `{userpf}` (`%LocalAppData%\Programs`) | Yes, fully customisable wizard | None built in | Medium: maintain an `.iss` script |
| MSIX | Yes | Yes | Via App Installer | High: needs a trusted signing certificate even for sideloading |
| Portable zip | Yes (no install at all) | No | No | Done (`-Zip`) |
| Steam / itch.io | Handled by the store client | Yes | Yes | Store accounts and review; itch.io's app can install the zip as-is |

## Velopack (in use)

The game uses Velopack. Its `Setup.exe` is a one-click installer that installs per user to
`%LocalAppData%\BannerAndBarrow` without elevation, adds Start Menu and Desktop shortcuts, and launches the game. The
same release folder serves updates, so a later "check for updates" is a few lines of code rather than a new system.

How it is wired:

- `BannerAndBarrow.Game` references the `Velopack` package, and `VelopackApp.Build().Run();` is the first line of
  `Program.cs`. It handles install, uninstall and update hooks, then returns immediately on a normal start.
- `vpk` is a local dotnet tool (`.config/dotnet-tools.json`), so `dotnet tool restore` is all a new machine needs.
  Keep its version equal to the `Velopack` package version.

To build a release:

```
pwsh scripts/publish.ps1 -Version 0.2.0 -Pack
```

This publishes to `artifacts/publish/win-x64`, then writes `artifacts/releases/`:

- `BannerAndBarrow-win-Setup.exe`: what players download and run (about 41 MB).
- `BannerAndBarrow-win-Portable.zip`: runs from any folder, no install.
- `BannerAndBarrow-<version>-full.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json`: used for updates. Keep
  the previous releases folder when building the next version so Velopack can also make delta packages.

### Shipping a new version

1. `pwsh scripts/publish.ps1 -Version 0.3.0 -Pack` (the version must go **up** each time).
2. Send whoever wants it `artifacts/releases/BannerAndBarrow-win-Setup.exe`, or attach it to a GitHub release:
   `gh release create v0.3.0 --title "Banner & Barrow 0.3.0" --notes-file notes.md`, then
   `gh release upload v0.3.0 <file>` for each asset (upload them one at a time: uploading several at once has
   failed with an HTTP 500 and rolled the whole release back).
3. Players run the new `Setup.exe`. It replaces the installed version in place, keeps `%APPDATA%\BannerAndBarrow\settings.json`,
   and relaunches. They do not uninstall first.

Keep the previous `artifacts/releases` folder when building the next version and Velopack also writes a small
delta package, which is what an in-game updater would download instead of the full 36 MB.

Verified for 0.2.0: `Setup.exe --silent` installed to `%LocalAppData%\BannerAndBarrow` (with `current`,
`packages`, a launcher stub and `Update.exe`), created Start Menu and Desktop shortcuts named "Banner and
Barrow", and registered an uninstall entry ("Banner and Barrow", 0.2.0) that Windows Settings can remove.

Note: `--packTitle` must not contain an `&` — it breaks the package metadata XML, which is why the installer
calls the game "Banner and Barrow" while the title screen shows "BANNER & BARROW".

Next steps when wanted: upload `artifacts/releases` to GitHub Releases (`dotnet vpk upload github ...`) and call
`UpdateManager.CheckForUpdatesAsync()` from the main menu to offer updates.

### Things to plan for with any installer

- **SmartScreen.** An unsigned `Setup.exe` shows "Windows protected your PC" until it builds download reputation.
  A code-signing certificate (or Azure Trusted Signing) removes most of that. Velopack signs during `vpk pack` when
  given signing parameters. Current builds are unsigned (`vpk pack` warns about this).
- **Version numbers.** Pass the same version to `publish.ps1 -Version` and `vpk pack --packVersion`. Updates need
  increasing SemVer versions.
- **Config edits.** `config/` ships inside the install folder and is replaced on update. That is right for balance
  data. Anything a player should keep belongs in `UserSettings`.
- **Other platforms.** MonoGame DesktopGL and Velopack both support Linux and macOS (`-Runtime linux-x64` /
  `osx-arm64`), but those builds have not been tried.

Sources: [Velopack .NET quick start](https://docs.velopack.io/getting-started/csharp),
[Velopack installers](https://docs.velopack.io/packaging/installer),
[Velopack on Windows](https://docs.velopack.io/packaging/operating-systems/windows),
[Inno Setup PrivilegesRequired](https://documentation.help/Inno-Setup/topic_setup_privilegesrequired.htm).
