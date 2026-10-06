## PowerPal 0.2.2

- Follow Windows display scaling with per-monitor DPI support and scrollable layouts on smaller desktops.
- Follow Windows light/dark appearance by default. Settings offers Auto, Light, and Dark; the dashboard lightning bolt cycles the same modes.
- Keep the pp lettering and add a lightning bolt to the tray icon.
- Add live GPU activity, clearly labeled estimated app power shares, and estimated watts when running on battery. Estimates allocate whole-system battery draw using equal CPU/GPU utilization weights; they are not calibrated per-app measurements. External power shows shares only.
- Capture a selected game's CPU/GPU and power estimates every two seconds while PowerPal stays in the tray. Stop from the dashboard or tray menu, and open CSVs from Export CSV. Update installation waits for active captures to finish.
- Show the running version in the dashboard title, Settings, and tray menu. Check updates shows progress and a nearby result.
- Simplify Settings for the public repository: automatic GitHub Releases updates need no login.
- Add a permanent latest-installer quickstart link and matching stable-name installer asset.

Battery/app histories and preferences are preserved. Older activity CSVs remain readable. Installation opens the dashboard; ordinary launches and automatic upgrades start in the tray.

Download **PowerPal-Setup-win-x64.exe** (or the versioned installer). The two binaries are identical. SHA-256 sidecars are included. The installer remains unsigned.
