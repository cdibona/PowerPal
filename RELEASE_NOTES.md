## PowerPal 0.3.0

- Read real NVIDIA GPU board power and cumulative energy through the existing driver, on AC or battery. No added driver or administrator requirement.
- Show measured GPU watts and observed energy, estimated per-app **GPU W~** and GPU Wh this run, and unassigned GPU power. CPU watts remain unavailable.
- Match sensors to Windows GPU adapters so integrated-GPU activity is not charged to the discrete GPU. Ambiguous mappings stay unavailable.
- Sample sensors about every five seconds, pause/release NVML when the NVIDIA GPU is idle, and back off after failures. Disable sensors in Settings if desired.
- Add GPU sensor CSV history/export and sensor fields to automatic top-app history. Keep all earlier history compatible.
- Rename the activity ranking to **Load %** and retire the old allocation of whole-battery draw to apps. GPU watts are estimates of the GPU component, not total application wattage.
- Test energy counter resets, sleep gaps, stale data, adapter separation, unassigned power, history compatibility, and Settings/scaling regressions.

GPU energy totals cover observed intervals in the current PowerPal run; gaps and first samples are excluded. Sensor support depends on the installed NVIDIA driver/GPU. AMD/Intel GPU power and CPU package power are not implemented. All app resource monitoring remains available without GPU sensors.

Use **Check updates**, then minimize to apply. Installers and SHA-256 sidecars are below; no reinstall or GitHub login is needed.
