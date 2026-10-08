## PowerPal 0.3.3

- Fix oversized control text caused by GDI+ caching desktop DPI before Windows Forms enabled display scaling during tray startup. Establish PerMonitorV2 awareness before drawing or font creation.
- Match buttons, Settings and process-table text to the Windows message font, normally Segoe UI 9 pt. Follow Windows applies the monitor scale once; manual display scales remain absolute overrides.
- Correct a one-pixel rounding discrepancy between native text fields and Windows Forms text rendering.
- Add a release regression that compares live text and text-entry font handles against Windows' own font at system scale and all 100–300% overrides, including hidden startup. Recheck Settings, menus, sorting, both themes and tray/window restoration.

Use **Check updates**, wait for verification, then minimize to apply. Choose **Settings > Appearance > Display scale > Follow Windows** to match the system scale.
