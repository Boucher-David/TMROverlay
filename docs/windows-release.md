# Windows Releases

TmrOverlay publishes Windows tester builds from GitHub Actions. Release tags such as `v1.0.0` produce a public GitHub Release with Velopack installer/update assets plus the existing portable zip fallback.

Expected release assets include:

- `TMROverlay-v<version>-win-x64.msi`
- a Velopack full `.nupkg` package
- `releases.win-x64.json`
- delta `.nupkg` packages when prior Velopack releases exist
- `TmrOverlay-v<version>-win-x64.zip`
- `TmrOverlay-v<version>-win-x64.zip.sha256`
- `TmrOverlay-v<version>-win-x64-manifest.txt`
- generated GitHub release notes

The Velopack MSI installer and the portable zip are self-contained for Windows x64. Testers do not need to install the .NET runtime.

## One-Page Teammate Guide

The current teammate-facing install/support handoff image is:

![Tech Mates Racing Overlay Windows tester install and feedback guide](assets/windows-release-teammate-tutorial.png)

Regenerate the teammate guide and installer artwork after release-package, installer, or Support-tab flow changes:

```bash
swift tools/render_windows_installer_splash.swift
swift tools/render_release_tutorial.swift
python3 tools/validate_overlay_screenshots.py --profile release-tutorial --root docs/assets
```

## Publishing

1. Merge the release branch to `main` after the Windows validation gate passes.
2. Create an annotated tag on the merge commit:

   ```bash
   git tag -a vX.Y.Z -m "vX.Y.Z - Short release title" -m "Short release summary."
   git push origin vX.Y.Z
   ```

3. GitHub Actions runs `.github/workflows/windows-dotnet.yml`.
4. The PR/main validation job restores, builds, tests, validates active screenshot contracts, generates/validates browser-review, localhost, Windows-rendered overlay, and installer screenshots as workflow artifacts, compares screenshot manifests for browser/localhost/Windows parity, runs a self-contained publish dry run with the same package audit used by release packaging, and dry-runs `vpk pack` including the MSI and update feed.
5. The tag workflow publishes `src/TmrOverlay.App` for `win-x64`, audits the publish folder, writes a package manifest, zips it, generates a SHA-256 checksum, packs Velopack MSI/update assets, uploads workflow artifacts, and creates or updates the GitHub Release assets.

Manual workflow dispatch can still produce package artifacts for a branch test run, but it does not create a GitHub Release unless the run is for a `vMAJOR.MINOR.PATCH` tag.

## Local Branch Installer Build

On Windows, a branch can produce an MSI without waiting for a tag by using the same Velopack path as the workflow. Use a unique prerelease/test version so the package identity does not collide with an installed release being validated:

```powershell
$version = "1.0.0-local.$(Get-Date -Format yyyyMMddHHmmss)"
$publishPath = ".\artifacts\local-publish"
$velopackPath = ".\artifacts\local-velopack"

dotnet publish .\src\TmrOverlay.App\TmrOverlay.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=$version `
  -p:AssemblyVersion="1.0.0.0" `
  -p:FileVersion="1.0.0.0" `
  -p:InformationalVersion="$version+local" `
  -p:ContinuousIntegrationBuild=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $publishPath

dotnet tool install --global vpk --version "0.0.1589-ga2c5a97"
$vpk = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
New-Item -ItemType Directory -Force -Path $velopackPath | Out-Null

& $vpk pack `
  --packId "TMROverlay" `
  --packVersion $version `
  --packDir $publishPath `
  --mainExe "TMROverlay.exe" `
  --packTitle "Tech Mates Racing Overlay" `
  --packAuthors "Tech Mates Racing" `
  --outputDir $velopackPath `
  --channel "win-x64" `
  --shortcuts "Desktop,StartMenu" `
  --msi `
  --instLocation "Either" `
  --splashImage ".\assets\brand\TMRInstallerSplash.png" `
  --msiBanner ".\assets\brand\TMRMsiBanner.bmp" `
  --msiLogo ".\assets\brand\TMRMsiLogo.bmp" `
  --icon ".\src\TmrOverlay.App\Assets\TmrOverlay.ico"
```

The generated MSI is written under `artifacts\local-velopack`.

If the `vpk` tool is already installed, replace the install command with
`dotnet tool update --global vpk --version "0.0.1589-ga2c5a97"`.

## Package Contents

The portable zip should contain the runtime app only. The release workflow fails if top-level repo/development folders such as `.github`, `captures`, `docs`, `history`, `local-mac`, `mocks`, `skills`, `tests`, or `tools` appear in the publish folder.

The expected package shape is intentionally small:

- `TMROverlay.exe`
- `appsettings.json`
- `Assets/TMRLogo.png`
- optional `Assets/TrackMaps/*.json` bundled derived track maps

The executable icon is embedded from `src/TmrOverlay.App/Assets/TmrOverlay.ico`. Installer branding is generated from `tools/render_windows_installer_splash.swift` into `assets/brand/TMRInstallerSplash.png`, `assets/brand/TMRMsiBanner.bmp`, and `assets/brand/TMRMsiLogo.bmp`. The release manifest asset lists the exact published files and sizes for each build, so package review can happen without downloading and unzipping the app.

Velopack package assets are generated from that audited publish folder. The package id is `TMROverlay`, the title is `Tech Mates Racing Overlay`, and the current channel is `win-x64`. The MSI installs Desktop and Start Menu shortcuts. Velopack shortcut locations are package-time options in the generated MSI path, so this branch creates both shortcuts by default rather than exposing per-shortcut wizard checkboxes. The package id intentionally changed from the early `TechMatesRacing.TmrOverlay` tester identity so installed package identity matches the shorter executable name.

## Tester Download

1. Open the repository's GitHub Releases page.
2. Open the latest `vMAJOR.MINOR.PATCH` release.
3. Download and run `TMROverlay-v<version>-win-x64.msi`.
4. Use the MSI's standard Windows Installer dialog flow for the install location.
5. Launch TmrOverlay from the Desktop shortcut or from the Start Menu shortcut under the Tech Mates Racing folder. The installed app executable is `TMROverlay.exe`, but teammates should normally use a shortcut created by the MSI.
6. Use the portable zip only as a fallback or support artifact. For the zip fallback, download `TmrOverlay-v<version>-win-x64.zip` and the matching `.sha256` asset when you want to verify the file.
7. Unzip the portable package into a normal user-writable folder, for example:

   ```text
   %LOCALAPPDATA%\Programs\TmrOverlay
   ```

8. Run `TMROverlay.exe`.

The app stores settings, history, logs, diagnostics bundles, and captures under `%LOCALAPPDATA%\TmrOverlay` by default. Replacing the portable application folder or updating an installed Velopack build does not delete that user data. Uninstalling an installed Velopack build removes this app-data root.

## User Data And Compatibility

Installed and portable builds keep user data separate from application binaries by default:

```text
%LOCALAPPDATA%\TmrOverlay
```

That app-data root contains settings, user history, logs, events, diagnostics, runtime state, imported Garage Cover images, user-generated track maps, and optional captures. A new install path or a Velopack-updated app folder should still find the same app-data root unless the user explicitly overrides storage through configuration or `TMR_` environment variables.

Installed-build uninstall cleanup removes the default `%LOCALAPPDATA%\TmrOverlay` root and any configured app-data root that is still safely inside LocalAppData. Unsafe broad paths, such as LocalAppData itself or a configured path outside LocalAppData, are skipped rather than recursively deleted.

Normal startup/update cleanup is intentionally narrower than uninstall cleanup. On startup, the app removes stale pre-0.18.4 `TechMatesRacing.TmrOverlay` package folders under LocalAppData and Desktop/Start Menu shortcuts that still point at that legacy package identity. It does not delete the current `%LOCALAPPDATA%\TmrOverlay` user-data root. Diagnostics bundles include `metadata/installer-cleanup.json` so support can see whether this cleanup ran, which paths were removed, and which paths were skipped.

Settings are versioned in `settings/settings.json` and loaded through `AppSettingsMigrator`, which normalizes older readable settings and writes them back at the current settings version. If an existing settings file cannot be read, startup uses in-memory defaults for that run but leaves the file on disk untouched so an update does not silently destroy user settings. Session history has explicit summary, collection-model, aggregate, and analysis version constants. On startup, `HistoryMaintenanceService` scans user history in the background, backs up and normalizes compatible legacy summaries, skips incompatible/future/corrupt summaries into a maintenance manifest, and rebuilds aggregates. History-backed overlays reject incompatible aggregate versions while maintenance is still running.

If a release changes a durable user-data schema, the branch must update the matching version constants, migrations or compatible readers, schema-compatibility tests, versioned snapshots under `fixtures/data-contracts/`, and docs before publishing. For car/track history specifically, changing the stored JSON shape should bump `summaryVersion` and add an ordered summary migration; changing the meaning of a derived metric or confidence rule should bump `collectionModelVersion` and either provide a compatible adapter or intentionally exclude old summaries from current aggregates. Unsupported future or incompatible old history must remain on disk and be excluded from overlays instead of causing startup or parsing failures.

## Checksum Verification

From PowerShell in the folder containing the downloaded zip:

```powershell
$version = "X.Y.Z"
$zip = "TmrOverlay-v${version}-win-x64.zip"
$expected = (Get-Content "$zip.sha256").Split(" ")[0]
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$actual -eq $expected
```

The command should print `True`.

## Upgrade And Rollback

Velopack-installed builds check the public GitHub Release feed once on app startup. Startup checks remain passive, but the tray menu and Support tab now expose explicit user actions to download an available update and restart to apply it. The app does not auto-download, auto-apply, or restart without the user choosing those actions.

To upgrade a Velopack-installed build:

1. Open TmrOverlay from the tray and use `Check for Updates`, or open the latest GitHub Release directly.
2. If a newer release is available in the app, choose `Install`.
3. After the download finishes, the primary action changes to `Restart`; choose it to apply the update.
4. If the in-app update flow is unavailable or fails, close TmrOverlay and run the latest MSI from the GitHub Release.
5. Launch TmrOverlay from the Desktop or Start Menu shortcut.

Because the early tester package id moved from `TechMatesRacing.TmrOverlay` to `TMROverlay`, installed builds that used the older package id should be treated as a fresh installer transition: close the old app, run the new MSI, confirm `%LOCALAPPDATA%\TmrOverlay` settings/history are still present, and check the diagnostics bundle installer-cleanup metadata if a stale shortcut or old launch path is suspected. If Windows still shows both package entries, uninstall the old tester package from Windows settings.

To move from a portable zip to the Velopack installer:

1. Close the portable copy of TmrOverlay.
2. Run the latest MSI.
3. Launch TmrOverlay from the Desktop or Start Menu shortcut.
4. Keep the old portable folder until the installed app opens correctly, then remove it when no longer needed.

Installed and portable builds both use `%LOCALAPPDATA%\TmrOverlay` by default, so settings, history, logs, diagnostics, runtime state, and captures should carry forward unless the user explicitly overrides storage through configuration or `TMR_` environment variables.

To upgrade a portable release:

1. Exit TmrOverlay from the settings window or tray menu.
2. Keep or rename the old unzipped application folder.
3. Unzip the new release into a fresh folder, or replace the old application files while the app is closed.
4. Start `TMROverlay.exe`.

To roll back a portable release, close the app and run the previous unzipped folder again. To roll back an installed release during tester validation, close TmrOverlay, keep a diagnostics bundle from the problem build, then reinstall the older release MSI or use that older release's portable zip fallback. User settings/history remain in `%LOCALAPPDATA%\TmrOverlay`; if a future release changes a durable schema, the branch must document migration and compatibility before release.

## Signing And SmartScreen

Current tester builds may remain unsigned until signing is added. Windows SmartScreen or antivirus tools may warn on first launch because the executable or MSI is new and unsigned. Broader distribution should not rely on unsigned builds; choose executable/package signing before publishing beyond private testers.

## Diagnostics For Feedback

For teammate feedback, ask testers to include:

- the release tag they installed
- the app version shown in diagnostics or support output
- a diagnostics bundle from `%LOCALAPPDATA%\TmrOverlay\diagnostics`
- if the Settings UI is frozen and a bundle cannot be created, the `%LOCALAPPDATA%\TmrOverlay\logs` folder plus the latest `%LOCALAPPDATA%\TmrOverlay\captures` folder from the diagnostic patch build
- notes about whether iRacing was live, replaying, or not running

Diagnostics bundles include app version/runtime metadata, local logs, and live-overlay expected-state metadata. Rolling live-window overlay crops are included only when explicitly enabled for a diagnostic run. Bundles intentionally exclude raw `telemetry.bin` and source `.ibt` payloads.

Diagnostics bundles also include release update state: whether the app is installed through Velopack, whether the current run is portable, the update source, current/latest versions, last checked time, download/apply timestamps, download progress, available update actions, and last failure if any. Installer cleanup metadata records the last startup/update legacy cleanup result so stale shortcuts or old package identity residue can be reviewed from the same bundle. Performance metadata records settings-driven overlay form recreation counts, so a unit toggle should show updated unit-aware forms and zero recreated forms.
