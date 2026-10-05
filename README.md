# PowerPal

A native Windows tray companion for battery power, local history, and current process activity. Windows 10/11 x64, .NET Framework 4.8+, no runtime package dependencies.

## Install and use

Run `dist/PowerPal-Setup-0.2.1-win-x64.exe`. The per-user installer needs no administrator access, creates a Start Menu shortcut, and offers to start recording at sign-in (enabled by default). Installation opens the full dashboard. Later normal launches and sign-in startup begin in the tray; automatic upgrades also restart quietly in the tray. Click the mint `pp` icon to open the dashboard. Use `--show` to explicitly open the window. Windows may initially put it in the tray overflow menu.

Both **close and minimize hide to the tray**. Recording continues every ten seconds while the app runs. Use **Exit and stop recording** in the tray menu to quit. Settings can toggle startup and automatic updates. Exit an older portable copy before installing this version; first-time installation cannot replace a copy running from a different directory.

The dashboard shows live source, net battery flow, charge, voltage, battery charts, a sortable/filterable Power users table, selectable five-minute CPU/memory/I/O histories, and a scrolling power/battery/charging event log. Recording and app-log status show last-save times. Amber indicates measurement or recording trouble. Hover over the tray icon for source, percentage, and battery watts.

## What the numbers mean

- Battery watts are net flow: positive is charging; negative is discharging. Zero means no net battery flow, even if the computer is consuming external power.
- Voltage is measured across the battery terminals. Charger identity, adapter voltage, negotiated USB-C PD contract, and total wall power are unavailable through this implementation.
- Current consumers are ranked by CPU usage, normalized across all logical processors, with memory and process I/O throughput. I/O includes more than physical-disk traffic. These are activity counters, **not measured per-app watts**. Protected/exited processes are omitted and the UI reports the inaccessible count. Process names are grouped; GPU activity is not collected.
- Missing readings remain missing. Relative-unit batteries are never labeled as watts or watt-hours. Multiple batteries retain separate device-keyed history; combined battery flow is shown only when all reported batteries have a watt reading. The main voltage card says “Multiple packs” for multiple batteries; individual voltage readings are in CSV.
- Sleep and application downtime leave gaps. No Windows service is installed, so recording stops at sign-out or exit. The app prevents a second ordinary instance.

## History and privacy

Daily CSV files live in `%LOCALAPPDATA%/PowerPal/History`. They contain UTC timestamps, source, device key, battery state, percentage, battery volts, signed battery watts, and remaining Wh. Charts display local time. Export saves the selected 1-hour, 24-hour, or 7-day range. Files remain until manually removed; uninstall preserves history and preferences. The top 20 app groups ranked by CPU (then I/O and memory) are now recorded every 10 seconds in `%LOCALAPPDATA%/PowerPal/Activity`, including process names, rank, CPU percentage, memory MB, I/O MB/s, the measurement interval, and inaccessible-process count. CPU and I/O rates describe the most recent measurement interval, usually two seconds, rather than a ten-second average. Recording continues in the tray. The table displays all readable app groups; its selection and sorting do not affect what gets recorded. Use **Export CSV > App activity history** to export the selected time range.

Power, battery-level, and charging transitions plus recording start/stop events persist in daily JSONL files under `%LOCALAPPDATA%/PowerPal/Events`. The event panel shows the latest 200 events in the selected range. Select an event to see its full details in a tooltip. The selected app's five-minute mini charts are kept in memory for the current session; activity CSVs retain older snapshots. Battery device keys identify slots, not a replacement battery serial number.

The application makes GitHub requests only for release updates. It sends no telemetry or measurement history. Private-release credentials are encrypted with Windows DPAPI for the current user, in `%LOCALAPPDATA%/PowerPal/github.dat`; **Forget the saved GitHub connection** removes them. Importing an existing Git login copies its saved credential, which may have wider permissions than a dedicated read-only token. A fine-grained Contents: read token scoped to this repository is also supported.

## Automatic updates

The app checks `cdibona/PowerPal`'s latest stable GitHub release at startup and every six hours. It skips drafts and prereleases. An update must have a higher numeric `vMAJOR.MINOR.PATCH` version and a `PowerPal-Setup-MAJOR.MINOR.PATCH-win-x64.exe` asset with GitHub's SHA-256 digest.

Downloads use the GitHub release-assets API for private repositories. Credentials are sent only to api.github.com; signed storage redirects receive no Authorization header. A size limit and SHA-256 check must pass before the installer is launched. Failed checks leave recording running. Downloads are staged under `%LOCALAPPDATA%/PowerPal/Updates`.

With automatic updates enabled, a verified installer runs when the dashboard is hidden; if the dashboard is open, installation waits until you minimize or close it. Recording briefly stops while the installer replaces the app, then restarts in the tray. Settings disable automatic checks. The manual Check updates command also downloads and stages a newer release. Updates preserve history and respect a disabled sign-in startup setting. The installer is currently unsigned; checksum verification provides release download integrity, not publisher code signing.

The now-public repository can be checked and downloaded without a GitHub login. Saved credentials are used only after an unauthenticated request cannot find a release. For private releases, Settings provides **Use existing GitHub login** (Git Credential Manager) or a password-masked token field. Saving settings retries the update check. Expired credentials can be replaced there. Release assets are compiled on GitHub Actions, and the updater pulls the installer binary from the published release.

## Build and release

```powershell
./build.ps1 -Version 0.2.1
./package.ps1 -Version 0.2.1 -Compiler 'C:/path/to/ISCC.exe'
```

Build uses the compiler shipped with Windows .NET Framework. Packaging uses Inno Setup 6. Outputs are `bin/v0.2.1/PowerPal.exe` and `dist/PowerPal-Setup-0.2.1-win-x64.exe` plus a SHA-256 sidecar. No SDK download is needed. The repository's MIT license is included in the installer.

The GitHub Actions workflow builds/tests on branches and PRs. Pushing a `vMAJOR.MINOR.PATCH` tag builds an installer and publishes the release and checksum. The tag is the version source; prerelease tags are rejected. Review and merge the source before tagging a production release.

## Verification

- `bin/v0.2.1/PowerPal.exe --self-test`: conversion, unknown readings, relative-unit handling, CSV culture/round trip, file persistence and interrupted append recovery, CPU normalization, update versions, allowed download origins, and digest/size verification. Result goes to `bin/v0.2.1/test-result.txt`.
- `--probe C:/absolute/path/probe.csv`: one real sensor reading.
- `--runtime-test C:/absolute/path/test-folder`: isolated 20-second recorder test including startup visibility, opening, minimize, close, and persisted battery and process samples. Add `--show` to test installer-style visible startup. No update check or sign-in change occurs.
- `--ui-test C:/absolute/path/preview.png`: render the dashboard with synthetic data and counters without touching real history.

The local installer was exercised through install, reinstall/upgrade, installed-app tests, and uninstall in an isolated installation identity. `--verify-public-release C:/absolute/path/result.txt` downloads and verifies the latest public installer with the production updater, without credentials or installation.

Windows API references: [battery information](https://learn.microsoft.com/en-us/windows/win32/power/battery-information), [battery status units](https://learn.microsoft.com/en-us/windows/win32/power/battery-status-str), [relative capacity](https://learn.microsoft.com/en-us/windows/win32/power/battery-information-str). Updater reference: [GitHub release assets](https://docs.github.com/en/rest/releases/assets).
