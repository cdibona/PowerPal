## PowerPal 0.3.1

- Fix clipped text by measuring Windows font heights and reserving padding in the process filter, table rows, event log, dropdowns and numeric entry.
- Let Settings descriptions determine their own height, with consistent spacing between fields at every display scale.
- Restore visible up/down indicators in every sortable Power users header. Highlight the active direction and keep it through live refreshes.
- Keep column proportions stable when switching scales, align numeric values, and avoid partial rows where possible.
- Give dashboard headings, large readings and resource details adequate line height and spacing.
- Reapply explicit pixel fonts after monitor DPI changes, including when a manual scale overrides Windows.
- Add release regression checks for 100-300% scaling in light/dark themes, text fitting, Settings overlap, filtering, sorting and rendered arrows. Retain maximized/normal window and tray-state checks.

GPU sensors and automatic background recording continue as before. App GPU watts remain estimates of the GPU component, not total application wattage.

Use **Check updates**, then minimize to apply. No reinstall or GitHub login is needed.
