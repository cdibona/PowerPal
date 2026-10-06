## PowerPal 0.2.3

- Fix Settings restoring a maximized dashboard to normal size. Opening Settings and returning from the tray now preserve window state and position.
- Keep Settings within the desktop working area, including after changing scale.
- Add **Display scaling**: follow Windows (default), or choose 1x, 1.25x, 1.5x, 1.75x, 2x, 2.25x, 2.5x, or 3x. Manual scales replace Windows scaling and are saved immediately.
- Remove the manual Capture button. Automatically record the busiest CPU/GPU app groups, with a configurable limit of 1-100 in Settings (default 20).
- Reduce tray polling to every five seconds and skip rebuilding the hidden app table; the open dashboard refreshes every two seconds. History saves about every ten seconds.
- Add regression tests for modal Settings, maximized/normal window bounds, every scale preset, tray reopening, saved preferences, and automatic recording limits.

App watts remain a clearly labeled CPU/GPU approximation; this release does not claim per-app hardware wattage. Existing history, preferences, and older capture CSVs are preserved. No reinstall or GitHub login is needed: Check updates, then minimize to apply.

Download **PowerPal-Setup-win-x64.exe** or the versioned installer; they are identical. SHA-256 sidecars are included. The installer remains unsigned.
