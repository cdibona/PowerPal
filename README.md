# PowerPal

A native Windows tray companion for battery power, automatic app activity and NVIDIA GPU power sensors. Windows 10/11 x64, .NET Framework 4.8+, no runtime package dependencies.

## Quickstart

**[Download the latest Windows installer](https://github.com/cdibona/PowerPal/releases/latest/download/PowerPal-Setup-win-x64.exe)** · [Release notes and checksums](https://github.com/cdibona/PowerPal/releases/latest)

1. Download and run **PowerPal-Setup-win-x64.exe**. Installation is per-user, requires no administrator access, and opens the full dashboard.
2. Find the mint **lightning bolt + pp** icon in the tray (possibly in the overflow menu). Later launches and sign-in startup begin in the tray.
3. Close or minimize the window to keep recording. Use **Exit and stop recording** in the tray menu to quit.

The installer offers recording at sign-in, enabled by default. Settings controls sign-in startup and automatic updates. An older portable copy should be exited before installation. `--show` explicitly opens the dashboard; automatic upgrades restart quietly in the tray.

By default, PowerPal follows Windows display scaling for its current monitor. In **Settings > Appearance > Display scale**, choose **Follow Windows** or an absolute **1x, 1.25x, 1.5x, 1.75x, 2x, 2.25x, 2.5x, or 3x** scale. Changes apply immediately and are saved; manual scales replace Windows scaling rather than multiplying it. Settings preserves the dashboard's maximized state and normal window position. Minimize/close and tray reopen preserve the prior window state. Content scrolls when it cannot fit, and Settings stays within the desktop working area. Appearance defaults to **Auto**, following Windows' app light/dark setting. Choose Follow Windows, Light, or Dark in Settings, or click the lightning bolt beside the PowerPal title to cycle **Auto → Light → Dark → Auto**. The choice is saved.

Settings uses compact **Appearance**, **Recording**, and **Updates** pages. Its 440 × 300 logical-pixel layout uses 9-point text at 100% scale, scales once with the selected display setting, and shows the effective Windows/PowerPal percentages. Save/Close stay visible; short screens scroll only the current page. Appearance and display scale save immediately; use **Save settings** for recording and update preferences.

Text fields, table rows and Settings descriptions reserve space using measured Windows font metrics. Sortable column headers show both up/down arrows, with the active direction highlighted. Column proportions stay stable after scale changes; event and process lists show whole rows where possible.

Ordinary controls and table text use the Windows message font (normally Segoe UI 9 pt), scaled once to the selected display setting. DPI awareness starts before tray-icon/font creation so GDI and GDI+ use the same conversion. Large dashboard readings retain their visual hierarchy.

## Automatic power-user recording

The dashboard shows source, net battery flow, charge, battery voltage, history charts, a sortable/filterable **Power users** table, a selected app's five-minute CPU/GPU/estimated GPU-watt graphs, and a persistent power/charging event log. The table includes CPU, GPU, memory, I/O, **Load %**, and **GPU W~**. Recording status and last-save times make background operation visible. Amber indicates a measurement or recording failure.

PowerPal automatically records the busiest **CPU + GPU** app groups, including during gameplay while the dashboard is in the tray. **Settings > Recording > Top apps to log** selects 1-100 groups (default 20). The ranking uses combined CPU/GPU activity, so GPU-heavy games are included even when their CPU use is low. There is no Capture button or manual session to start.

Sampling runs approximately every **2 seconds with the dashboard open** and **5 seconds in the tray**; history snapshots are saved about every **10 seconds**. Hidden process tables are not rebuilt. Use **Export CSV > App activity history** to export the selected 1-hour, 24-hour, or 7-day range. Each row keeps the underlying battery sample timestamp, source, and model alongside resource usage and estimates. The table still shows all readable app groups; the setting controls how many are written to history. Legacy manual-capture CSVs are preserved in `Captures`.

### GPU sensors and app estimates

PowerPal reads **NVIDIA GPU board power** through the NVML library already installed with a compatible NVIDIA driver. It adds no driver, service, runtime package, or administrator requirement. The dashboard shows measured board watts, observed energy, and an **unassigned** amount. These readings work on external power as well as battery. CPU package watts and Intel/AMD GPU power sensors are not implemented; CPU/GPU utilization, memory and I/O still work independently.

**Settings > Recording > Read GPU power sensors** enables/disables readings (default on). PowerPal checks Windows GPU engine activity first. It queries NVML about every five seconds (normally six seconds with the two-second UI timer), releases NVML when NVIDIA activity drops below 0.1 summed engine percentage points, and retries unavailable sensors no more than once a minute. There is no continuous ETW trace or injected code. Activity gating reduces unnecessary queries; it is not proof that monitoring has zero effect on GPU sleep states.

Where supported, changes in the cumulative energy counter give average board watts over the sensor interval. Otherwise the driver power reading is used; energy is then approximated by trapezoidal integration between consecutive power readings. CSV labels distinguish `nvml-energy`, `nvml-power`, and `nvml-power-integrated`. First samples, resets, driver failures, idle pauses and gaps longer than 20 seconds do not invent energy. Driver sensor accuracy still depends on hardware and firmware.

**GPU W~ is an estimate of the GPU component only, not total app power.** DXGI PCI IDs match the NVIDIA sensor to its WDDM adapter LUID. Integrated and discrete GPU engines are kept separate; ambiguous identical-board mappings are left unavailable. Windows engine activity is averaged over the same interval as the energy measurement:

```text
busy fraction = busiest engine utilization / 100 (capped at 1)
app weight = app engine-time / all known engine-time on that GPU
estimated app GPU watts = measured board watts × busy fraction × app weight
```

Protected processes remain in the WDDM denominator when their counters are readable, so their power is not reassigned to visible apps. Unallocated power remains **unassigned**. That remainder includes the model's allowance for idle/shared work; it is not a measured idle baseline. Engine time is an imperfect proxy: different workloads, frequencies, memory traffic and simultaneous engines can use different power. The allocation model is versioned as `nvml-engine-allocation-v1`.

The selected app also shows estimated **GPU Wh observed this run**. This total includes only captured intervals and resets when PowerPal restarts; it is not a complete lifetime/game-session total. The total is updated once per sensor interval, independently of screen refreshes. Existing history files retain earlier runs. Missing, stale (over 12 seconds), warming-up, unsupported and unmapped readings show `--` rather than invented zeros.

**Load %** is the app's fraction of combined CPU + busiest-GPU activity across readable app groups; it is not a percentage of system power. CPU is normalized across all logical processors. The old battery-based total-app watt allocation is retired: display, fan and other battery draw are no longer distributed across apps. Legacy CSV watt fields remain intact in old history; new rows leave `estimated_app_watts` empty.

Table filtering/sorting do not affect measurement or automatic history selection. Automatic history ranks CPU + GPU activity, using known CPU activity if GPU counters are missing. I/O and memory remain resource measurements rather than being converted to watts.

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
| `Activity` | Configurable top app groups every 10 seconds, including CPU/GPU, activity share, estimated GPU watts/Wh, sensor timestamp/model, memory/I/O, measurement interval, inaccessible count, source, battery reading timestamp, and estimate model. |
| `Sensors` | Per-adapter GPU board watts, interval Wh, observed Wh this run, unassigned watts, sensor source and attribution model, saved for each new sensor reading. |
| `Captures` | Preserved manual-capture CSVs from older releases; new versions use automatic activity history. |
| `Events` | Daily JSONL power, charging, battery-level, recording events (and any older capture events). |

Charts use local time; exports use UTC. CPU/GPU/I/O counters describe their most recent measurement interval (usually two seconds while open or five seconds in the tray), not a ten-second average. Five-minute app graphs are held in memory; CSVs retain older snapshots. New activity files use a `-v3.csv` suffix; exporting combines old and new files with empty fields for metrics that older versions never recorded. Original histories are not rewritten. GPU watts use their own 5-6 second sensor interval; the CSV includes its timestamp. `Export CSV > GPU sensor history` exports component readings independently of the top-app limit.

Use **Export CSV** for the selected 1-hour, 24-hour, or 7-day range. The event panel displays up to 200 recent events; select an event for its full tooltip. Files remain until manually removed, and uninstall preserves history and preferences. Battery device keys identify slots, not replacement-battery serial numbers.

PowerPal contacts GitHub only for release updates. It sends no telemetry or recorded history. No GitHub login or token is needed or used. A credential file left by an older private-repository build is ignored.

## Automatic updates and GitHub packaging

Automatic updates are enabled by default. PowerPal checks [the latest stable GitHub Release](https://github.com/cdibona/PowerPal/releases/latest) at startup and every six hours. **Check updates** checks immediately and visibly changes to Checking/Downloading while busy. Its result appears below the toolbar. The running version is always visible beside the PowerPal title, in Settings, and in the tray menu. Drafts/prereleases are skipped. A newer numeric version must contain `PowerPal-Setup-MAJOR.MINOR.PATCH-win-x64.exe` with GitHub's SHA-256 asset digest.

A bounded download, allowed-host check, and matching SHA-256/size must pass before installation. Failed checks leave the recorder running. Downloads are staged in `Updates`. A verified update installs when the dashboard is hidden, briefly stops recording, then restarts in the tray. History and disabled sign-in startup settings are preserved. Settings can disable automatic checks. Manual checks can still stage and install a newer release.

**GitHub Releases is the distribution channel; a separate GitHub Package is not required.** GitHub Packages serves registries such as NuGet, npm, and container images. The Windows installer is a compiled Release asset. Each release includes a versioned installer for the updater and an identical `PowerPal-Setup-win-x64.exe` alias for the permanent quickstart link, both with checksum sidecars. The installer is currently unsigned; download integrity checks are not publisher code signing.

## Build and release

```powershell
./build.ps1 -Version 0.3.3
./package.ps1 -Version 0.3.3 -Compiler 'C:/path/to/ISCC.exe'
```

Build uses the compiler shipped with Windows .NET Framework; no SDK download is needed. Packaging uses Inno Setup 6. Outputs include `bin/v0.3.3/PowerPal.exe` **and `PowerPal.exe.config`** (required for DPI support), versioned/stable installers in `dist`, and SHA-256 sidecars. The installer includes the config and MIT license.

GitHub Actions builds/tests on branches and PRs. Pushing a `vMAJOR.MINOR.PATCH` tag builds and publishes the installers. The tag supplies the version; prerelease tags are rejected. Review and merge source before tagging a production release.

## Verification

- `--self-test`: battery units, missing values, CSV persistence/export and old-file compatibility, GPU engine aggregation and adapter matching, energy delta/reset/gap arithmetic, conservation of attributed/unassigned power, polling/idle/failure behavior, stale/missing-data suppression, automatic CPU/GPU ranking, scale preference persistence, theme selection, and updater digest/host validation. Result: `test-result.txt` beside the executable.
- `--runtime-test C:/absolute/path/test-folder`: isolated 20-second test of automatic background logging with a configured top-three limit, startup visibility, open/minimize/close to tray. Add `--show` for installer-style startup. No update check or sign-in change.
- `--window-test C:/absolute/path/test-folder`: opens real modal Settings from normal/maximized dashboards, changes every scale option, saves isolated preferences, and verifies window bounds, tray restoration, duplicate-dialog prevention, and top-app selection. Runs in CI.
- `--dpi-test C:/absolute/path/test-folder`: confirms real PerMonitorV2 awareness, checks startup with a 125% override, and renders both themes at 100/125/150/175/200/225/250/300% layout scales. Verifies native text fit, all three Settings pages, open menus, horizontal text fit, fixed footer, compact dimensions, filter text, row heights, column-width round trips, and both sort directions in all seven headers, including painted arrow pixels after live refresh. Runs in release CI. Physical movement between differently scaled monitors still requires manual testing.
- `--font-test C:/absolute/path/test-folder`: initializes drawing before the first control and creates the dashboard handle while hidden, reproducing tray startup. Compares live control/table text measurements and text-entry HFONTs against Windows' native message font at system scale and every override from 100–300%. Runs in release CI; high-DPI hardware is needed to reproduce the original startup mismatch.
- `--sensor-probe C:/absolute/path/result.txt`: a bounded diagnostic with three direct NVIDIA samples followed by a 30-second automatic sampler/CPU-cost check. The diagnostic deliberately queries available NVIDIA sensors even when idle; normal recording uses activity gating.
- `--activity-probe C:/absolute/path/result.txt`: checks real GPU/process counters and reports timings and current estimates.
- `--probe C:/absolute/path/probe.csv`: one real battery sensor reading.
- `--ui-test C:/absolute/path/preview.png`: synthetic dashboard preview.
- `--verify-public-release C:/absolute/path/result.txt`: downloads/checks the published installer through the production updater without installing it.

Sensor references: [NVIDIA NVML](https://docs.nvidia.com/deploy/pdf/NVML_API_Reference_Guide.pdf), [DXGI adapter identity](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc).

References: [Windows battery information](https://learn.microsoft.com/en-us/windows/win32/power/battery-information), [GPU utilization and busiest engines](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/), [.NET Framework DPI support](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms), [GitHub release links](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases), [GitHub Packages](https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages).
