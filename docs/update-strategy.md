# Update Notification and Self-Update Strategy

Last updated: 2026-05-17

## Current State

Velopack is the canonical Windows installer/update path while the portable zip remains a fallback/support artifact. The PR/main workflow restores, builds, tests, validates browser review and localhost screenshots as separate jobs, checks Windows screenshot expectations, runs a publish dry run with package audit, and dry-runs `vpk pack` including the MSI and update feed. A `vMAJOR.MINOR.PATCH` tag publishes the app as a self-contained `win-x64` package, audits the publish folder, zips it, writes a package manifest and SHA-256 checksum, packs Velopack MSI/update assets, uploads workflow artifacts, and attaches both the portable zip artifacts and Velopack assets to the public GitHub Release.

The public GitHub Release is the Velopack feed. Installed builds use Velopack `GithubSource` against `https://github.com/Boucher-David/TMROverlay` with no embedded token. The active package id is `TMROverlay`; it intentionally replaces the first tester id, `TechMatesRacing.TmrOverlay`, so installed identity uses the shorter app name. The MSI uses branded WiX welcome/splash/banner/logo assets and creates Desktop plus Start Menu shortcuts. Portable/dev runs skip update checks because they do not have a Velopack install identity. Startup also removes stale legacy `TechMatesRacing.TmrOverlay` package folders and shortcuts so an updated install does not keep launching an old package identity.

Still pending before broader distribution:

- release signing before broader distribution
- deciding whether to keep the portable zip long term or only as a support fallback
- validating the final pre-v1 installer/update flow on real Windows teammate machines

## Recommendation

Use Velopack directly rather than building a custom zip updater.

### Velopack Baseline

The release channel should provide:

1. `VelopackApp.Build().SetAutoApplyOnStartup(false)` with an uninstall cleanup hook at the start of app startup.
2. A public GitHub Releases update source with no client token.
3. A non-blocking startup update check once per app launch.
4. A manual `Check for Updates` command from the tray menu and Support tab.
5. User-initiated download/install and restart-to-apply controls from the tray menu and Support tab.
6. Settings banner states for update available, download progress, pending restart, apply/restart, and failure.
7. Support/diagnostics metadata for current version, latest version, source, check time, download/apply timestamps, progress, failure state, installed/portable state, release URL, and available actions.
8. CI dry-run Velopack packaging on PRs and publish Velopack MSI/update assets on release tags.
9. Installed-build uninstall cleanup removes the default `%LOCALAPPDATA%\TmrOverlay` app-data root, including settings, history, logs, diagnostics, captures, runtime state, Garage Cover imports, and user-generated track maps.
10. Startup/update cleanup removes only stale legacy package identity folders/shortcuts and reports the last cleanup result in diagnostics bundle metadata.

The app still avoids surprise updates: it checks passively, then waits for the user to choose install and restart. It never auto-downloads, auto-applies, or restarts over the simulator.

### Active Apply Later

After teammates have installed through Velopack and at least one update has been validated:

- consider whether apply-on-exit should be offered alongside the explicit restart action
- keep update prompts out of active sessions
- keep failure state visible in Support and diagnostics bundles

Microsoft-native option: MSIX with App Installer.

- Gives Windows a package identity and automatic update settings through `.appinstaller`.
- Supports update checks on launch, prompts, and activation blocking on supported Windows versions.
- Requires accepting MSIX packaging constraints and signing/distribution setup.

Legacy option: Squirrel.Windows.

- Still relevant historically for Windows desktop self-updating, but for a new .NET 8 WinForms app Velopack looks like the cleaner path.

## UI Behavior

- Tray menu: update status, `Check for Updates`, and one primary update action.
- Tray menu: `Install Update` is enabled only when a newer release is available; the same action changes to `Restart to Apply Update` after the update is downloaded.
- Settings window: show update-available, downloading, pending-restart, applying, or update-warning states as a yellow banner above the tabs and below the main app title area.
- Startup behavior: check once per app launch, record success/failure quietly, and never block startup.
- Support tab diagnostics: include update check state, last failure, selected channel/source, current app version, latest version, download progress, and apply/restart timestamps.
- Never put an update prompt above the sim during an active session.

## Release Requirements

Before broader production distribution:

- sign release artifacts
- validate at least one installed Velopack update from GitHub Releases
- decide whether to keep the portable zip as a normal release asset
