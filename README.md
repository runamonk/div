<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="branding/logo-dark.png">
    <img src="branding/logo-light.png" alt="Window Wrangler" width="520">
  </picture>
</p>

<p align="center">Zones for your windows, and a lasso for the ones that wander off.</p>

---

Window Wrangler is a Windows tray app in the spirit of PowerToys FancyZones. You carve each
monitor into zones, and it keeps windows in them:

- **Snap by dragging.** Hold **Shift** while dragging a window to see the zones, then drop it into
  one. Near the edge two zones share, both light up; hold **Ctrl** to stretch across several.
- **New windows land in place.** Most apps remember where they were and get it slightly wrong;
  Window Wrangler nudges them the last few pixels into their zone. Apps that don't remember at all
  are put back where they last sat, and each app remembers a few spots, so a second browser window
  goes to its own place instead of on top of the first.
- **Win+arrow** (optional) moves the active window to the next zone that way, across monitors.
- **Drag a window out of its zone** and it goes back to the size it was before it was snapped.

## Layouts

Click the tray icon or press **Win+Shift+`** to open the layouts window. Pick a monitor at the top,
then click a layout to apply it.

- **Templates:** Focus, Columns, Rows, Grid and Priority Grid. The pencil makes an editable copy.
- **Grid layouts:** click a zone to split it (Shift+click for horizontal), drag across zones to
  merge them, drag the handles to resize.
- **Canvas layouts:** free-floating zones that can overlap. Drag to move, drag edges to resize.
- **Quick keys:** give a layout a number in the editor, then **Win+Ctrl+Alt+number** applies it to
  the active window's monitor.
- **Default layout:** right-click a layout and choose *Make default*. Monitors you haven't picked a
  layout for use it; it's marked with a folded corner.

Layouts are stored as proportions, not pixels, so one layout fits any monitor.

## Tray menu

Edit layouts, Settings, Tidy all windows (snaps every open window that's nearly in a zone), Pause,
Start with Windows, Reload settings, Open settings folder.

## Files

Everything lives in `%AppData%\WindowWrangler`:

| File | What |
| --- | --- |
| `settings.json` | Options and layouts. Safe to hand-edit; use *Reload settings* afterwards. |
| `history.json` | Where each app's windows last sat. Delete it to forget. |
| `wrangler.log` | Every move, and why: *nudged*, *restored*, *snapped* or *keyboard*. |

## Building

Needs the .NET 10 SDK. In VS Code press F5, or:

```
dotnet run --project src/WindowWrangler
dotnet test
```

`publish.cmd` makes a self-contained release build.

The icon and logos are drawn by `branding/make_branding.py` (needs Pillow):
`python branding/make_branding.py`.

## Known limits

- Windows running as administrator can't be moved unless Window Wrangler runs as administrator too.
- Don't run it alongside FancyZones; two window managers fighting over the same drag isn't pretty.
