<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="branding/logo-dark.png">
    <img src="branding/logo-light.png" alt="&lt;div&gt;" width="520">
  </picture>
</p>

<p align="center">Lay out your screen like a page: divide it into zones, and windows stay in them.</p>

---

`<div>` is a Windows tray app in the spirit of PowerToys FancyZones. You carve each
monitor into zones, and it keeps windows in them:

- **Snap by dragging.** Hold **Shift** while dragging a window to see the zones, then drop it into
  one. Near the edge two zones share, both light up; hold **Ctrl** to stretch across several.
  Without Shift, drags are left alone; turn drag-snapping off entirely in Settings.
- **New windows land in place.** Most apps remember where they were and get it slightly wrong;
  `<div>` nudges them the last few pixels into their zone. Apps that don't remember at all
  are put back where they last sat, and each app remembers a few spots, so a second browser window
  goes to its own place instead of on top of the first. Windows that were already open when
  `<div>` started (at sign-in, say) get the same last-few-pixels nudge.
- **Maximize to zone.** Maximizing a window (button, title bar double-click, Win+Up) fills its zone
  instead, keeping its rounded corners, and maximizing again puts it back; hold Shift for the
  whole screen.
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

Everything lives in `%AppData%\Div`:

| File | What |
| --- | --- |
| `settings.json` | Options and layouts. Safe to hand-edit; use *Reload settings* afterwards. |
| `history.json` | Where each app's windows last sat. Delete it to forget. |
| `div.log` | Every move, and why: *nudged*, *restored*, *snapped* or *keyboard*. |

## Building

Needs the .NET 10 SDK. In VS Code press F5, or:

```
dotnet run --project src/Div
dotnet test
```

`publish.cmd` makes a self-contained release build.

Maximize-to-zone needs a small native helper, `native/DivHook.c`, which `<div>`
loads into the apps whose windows it manages (never into everything, and never into games). The
built DLL is committed; after changing the C file, rebuild it with `pwsh native/build.ps1`, which
fetches its compiler (zig) into `.tools/` on first run.

The icon and logos are drawn by `branding/make_branding.py` (needs Pillow):
`python branding/make_branding.py`.

## Known limits

- Windows of apps running as administrator are left to Windows (normal maximize and snapping)
  unless `<div>` runs as administrator too: tick *Run as administrator* in Settings. With
  *Start with Windows* on, that uses a scheduled task, so it starts at sign-in without a prompt.
- Maximize-to-zone skips 32-bit apps, and apps that don't support display scaling on a scaled
  monitor; those maximize to the whole screen.
- Don't run it alongside FancyZones; two window managers fighting over the same drag isn't pretty.
