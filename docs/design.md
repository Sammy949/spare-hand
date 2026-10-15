# Design

Spare Hand is meant to look like it shipped in PowerToys. Values below come from reading the PowerToys source.

## Surfaces

The **tray**, the **Hold library** and the **LocalSend picker** are small pop-up cards:

- The window is invisible; the card draws everything (PowerToys `TransparentWindow` + `TransientSurface`).
- Card: corner radius 8, 1 px `SurfaceStrokeColorDefaultBrush`, `ThemeShadow`, 24 px transparent margin so the shadow
  is never clipped.
- Material: Desktop Acrylic kept active even when the window isn't focused (PowerToys'
  `AlwaysActiveDesktopAcrylicBackdrop`, included as is). `Default` kind rather than `Thin`: Thin let saturated
  backgrounds wash out secondary text.
- No frame, no Windows 11 border, no system rounding, out of Alt+Tab.

The **settings window** uses Mica and the Windows Community Toolkit `SettingsCard` / `SettingsExpander`, pinned to the
version PowerToys uses.

## Tiles

84 × 76, radius 6, a real shell icon at 32 px (or a 24 px Segoe Fluent glyph), 12 px label, up to two lines with
ellipsis. Three states:

- **Normal**: transparent.
- **Hot** (the file is over it): accent fill, on-accent text. Nothing else in Spare Hand uses the accent.
- **Dimmed**: 38% opacity, refuses the drop (e.g. the folder the file already lives in).

The cursor caption always says what the drop will do: "Move to Downloads", "Copy to D:\Backups", "Send with Quick
Share", "Hold".

## Behaviour

- **Never steals focus mid-drag.** The tray is a no-activate tool window; stealing focus breaks Explorer's drag loop.
- **Opens near the cursor, never under it**: above, else below, else right or left, clamped to the cursor's monitor.
- **Content is visible from the first frame.** The only motion is PowerToys' "pop" (scale 0.96 → 1, 150 ms), and none
  at all when Windows animations are off.
- **Closes when you click outside**, like Peek, using a global mouse check rather than focus (focus isn't guaranteed
  right after a drop).
- Light, dark and high contrast follow Windows. Elements built in code use theme-aware styles, so colours resolve
  against the window's own theme.

## Window model

Borrowed from PowerToys Command Palette: one long-lived window per surface, created once. The tray is created at
startup (its drop target must exist before a drag starts), shown once, then cloaked and parked off-screen; opening it
is a move plus an uncloak. The library and picker are created the first time they're needed.

## Memory

Like PowerToys Quick Access: 2 s after a surface hides (and after background work like a LocalSend scan), garbage
collect twice and call `SetProcessWorkingSetSize(-1, -1)`, unless something opened again meanwhile. Measured idle:
6–9 MB in Task Manager.

## Principles

- Nothing appears unless you ask for it.
- Hold never copies, moves, modifies or expires your files.
- No nags, rating prompts, upsells or recurring welcome screens.
- Offline, no telemetry, no account.
