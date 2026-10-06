# PowerPal

A native Windows tray companion for battery power, app activity, and gameplay capture. Windows 10/11 x64, .NET Framework 4.8+, no runtime package dependencies.

## Quickstart

**[Download the latest Windows installer](https://github.com/cdibona/PowerPal/releases/latest/download/PowerPal-Setup-win-x64.exe)** · [Release notes and checksums](https://github.com/cdibona/PowerPal/releases/latest)

1. Download and run **PowerPal-Setup-win-x64.exe**. Installation is per-user, requires no administrator access, and opens the full dashboard.
2. Find the mint **lightning bolt + pp** icon in the tray (possibly in the overflow menu). Later launches and sign-in startup begin in the tray.
3. Close or minimize the window to keep recording. Use **Exit and stop recording** in the tray menu to quit.

The installer offers recording at sign-in, enabled by default. Settings controls sign-in startup and automatic updates. An older portable copy should be exited before installation. `--show` explicitly opens the dashboard; automatic upgrades restart quietly in the tray.

PowerPal follows the Windows display scaling for its current monitor, including scaling changes while running. The window fits the available desktop and scrolls when its dashboard cannot fit at the chosen scale. Appearance defaults to **Auto**, following Windows' app light/dark setting. Choose Auto, Light, or Dark in Settings, or click the lightning bolt beside the PowerPal title to cycle **Auto → Light → Dark → Auto**. The choice is saved.

## Power users and gameplay

The dashboard shows source, net battery flow, charge, battery voltage, history charts, a sortable/filterable **Power users** table, a selected app's five-minute CPU/GPU/estimated-watt graphs, and a persistent power/charging event log. The table includes CPU, GPU, memory, I/O, **Est. %**, and **Est. W**. Recording status and last-save times make background operation visible. Amber indicates a measurement or recording failure.

To capture a game, launch it, select its app group in Power users, and click **Capture session**. Minimize PowerPal and play: the selected app's CPU/GPU activity, estimated share/watts, and battery readings are saved about every two seconds. Click **Stop capture** or use **Stop session capture** in the tray menu. **Export CSV → Open session captures** opens the saved files. Each row includes the original battery sample timestamp; battery hardware is sampled about every ten seconds. App disappearance produces a missing-app row, and capture continues until stopped. Automatic installation of a ready update waits until the capture ends. Exit/sign-out ends the capture and leaves the recorded CSV intact.

### What the estimates mean

**Per-app power is a rough activity-based allocation, not a measured or calibrated watt meter.** The model uses equal CPU and GPU utilization weights:

```text
app weight = CPU % + GPU %
estimated share = app weight / sum of all readable app weights
estimated app watts = estimated share × measured battery discharge watts
```

CPU is normalized across all logical processors. GPU is the busiest engine for the app group; usage of the same engine is summed across its processes before choosing the busiest engine. Windows WDDM GPU performance counters are collected without installing a driver. A compatible driver/counter provider is required.

The model allocates **all** reported battery draw—including display, fans, idle draw, and other shared overhead—to readable app groups. Equal utilization does not mean equal physical power across CPU/GPU or different hardware. Protected processes are omitted; percentages describe the readable set. These estimates help rank activity and compare gameplay periods on the same machine; they cannot isolate a game's physical energy use. The formula is versioned in exported data as `cpu-plus-busiest-gpu-v1`.

- **Est. %** can be shown on battery or external power when GPU readings and nonzero activity are available. It is a modeled share, not a measurement of system power.
- **Est. W** is shown only while all batteries report discharge in watts and the battery sample is at most 15 seconds old. External power, charging, unknown/missing GPU or battery readings, and idle samples without sufficient activity show `--`, never invented zero watts.
- Table filtering and sorting do not change the estimate denominator. The default ranking and background top-20 log use estimated share, falling back to CPU when estimates are unavailable.

## Battery measurements

- Battery watts are net flow: positive is charging; negative is discharging. Zero means no net battery flow, even if the computer is using external power.
- Voltage is measured across battery terminals. Charger identity, adapter voltage, USB-C PD contract, and total wall power are unavailable through this implementation.
- I/O includes more than physical-disk traffic. App groups combine processes with the same name; protected/exited processes are omitted and their inaccessible count is reported.
- Missing readings remain missing. Relative-unit batteries are never labeled as watts or watt-hours. Multiple packs retain separate device-keyed history; combined flow needs readings from every pack. Individual voltages are available in CSV.
- Sleep and app downtime leave gaps. No Windows service is installed: recording stops at sign-out or exit. A second ordinary instance is prevented.

## History and privacy

All data stays under `%LOCALAPPDATA%/PowerPal`:

| Folder | Contents |
| --- | --- |
| `History` | Daily battery CSVs, sampled about every 10 seconds: UTC time, source, device, state, %, volts, signed watts, remaining Wh. |
| `Activity` | Top 20 app groups every 10 seconds, including CPU/GPU, estimated share/watts, memory/I/O, measurement interval, inaccessible count, source, battery reading timestamp, and estimate model. |
| `Captures` | One CSV per selected-app session, approximately every 2 seconds. |
| `Events` | Daily JSONL power, charging, battery-level, recording, and capture events. |

Charts use local time; exports use UTC. CPU/GPU/I/O counters describe their most recent measurement interval (usually two seconds), not a ten-second average. Five-minute app graphs are held in memory; CSVs retain older snapshots. New activity files use a `-v2.csv` suffix; exporting combines old and new files with empty fields for metrics that older versions never recorded. Original histories are not rewritten.

Use **Export CSV** for the selected 1-hour, 24-hour, or 7-day range. The event panel displays up to 200 recent events; select an event for its full tooltip. Files remain until manually removed, and uninstall preserves history and preferences. Battery device keys identify slots, not replacement-battery serial numbers.

PowerPal contacts GitHub only for release updates. It sends no telemetry or recorded history. No GitHub login or token is needed or used. A credential file left by an older private-repository build is ignored.

## Automatic updates and GitHub packaging

Automatic updates are enabled by default. PowerPal checks [the latest stable GitHub Release](https://github.com/cdibona/PowerPal/releases/latest) at startup and every six hours. **Check updates** checks immediately and visibly changes to Checking/Downloading while busy. Its result appears below the toolbar. The running version is always visible beside the PowerPal title, in Settings, and in the tray menu. Drafts/prereleases are skipped. A newer numeric version must contain `PowerPal-Setup-MAJOR.MINOR.PATCH-win-x64.exe` with GitHub's SHA-256 asset digest.

A bounded download, allowed-host check, and matching SHA-256/size must pass before installation. Failed checks leave the recorder running. Downloads are staged in `Updates`. A verified update installs when the dashboard is hidden and no session capture is active, briefly stops recording, then restarts in the tray. History and disabled sign-in startup settings are preserved. Settings can disable automatic checks. Manual checks can still stage and install a newer release.

**GitHub Releases is the distribution channel; a separate GitHub Package is not required.** GitHub Packages serves registries such as NuGet, npm, and container images. The Windows installer is a compiled Release asset. Each release includes a versioned installer for the updater and an identical `PowerPal-Setup-win-x64.exe` alias for the permanent quickstart link, both with checksum sidecars. The installer is currently unsigned; download integrity checks are not publisher code signing.

## Build and release

```powershell
./build.ps1 -Version 0.2.2
./package.ps1 -Version 0.2.2 -Compiler 'C:/path/to/ISCC.exe'
```

Build uses the compiler shipped with Windows .NET Framework; no SDK download is needed. Packaging uses Inno Setup 6. Outputs include `bin/v0.2.2/PowerPal.exe` **and `PowerPal.exe.config`** (required for DPI support), versioned/stable installers in `dist`, and SHA-256 sidecars. The installer includes the config and MIT license.

GitHub Actions builds/tests on branches and PRs. Pushing a `vMAJOR.MINOR.PATCH` tag builds and publishes the installers. The tag supplies the version; prerelease tags are rejected. Review and merge source before tagging a production release.

## Verification

- `--self-test`: battery units, missing values, CSV persistence/export and old-file compatibility, GPU engine aggregation, estimate arithmetic, charging/stale/missing-data suppression, capture persistence, theme selection, and updater digest/host validation. Result: `test-result.txt` beside the executable.
- `--runtime-test C:/absolute/path/test-folder`: isolated 20-second test of background logging, session capture, startup visibility, open/minimize/close to tray. Add `--show` for installer-style startup. No update check or sign-in change.
- `--dpi-test C:/absolute/path/test-folder`: confirms real PerMonitorV2 awareness, renders both themes at simulated 100/125/150/200% layout scales, checks control bounds and DPI round trips. Physical movement between differently scaled monitors still requires manual testing.
- `--activity-probe C:/absolute/path/result.txt`: checks real GPU/process counters and reports timings and current estimates.
- `--probe C:/absolute/path/probe.csv`: one real battery sensor reading.
- `--ui-test C:/absolute/path/preview.png`: synthetic dashboard preview.
- `--verify-public-release C:/absolute/path/result.txt`: downloads/checks the published installer through the production updater without installing it.

References: [Windows battery information](https://learn.microsoft.com/en-us/windows/win32/power/battery-information), [GPU utilization and busiest engines](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/), [.NET Framework DPI support](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms), [GitHub release links](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases), [GitHub Packages](https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages).
