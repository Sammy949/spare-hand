# Architecture

C# / .NET 10, WinUI 3 (Windows App SDK 2.2), unpackaged and self-contained. Versions track PowerToys `main`.

## Map

```
src/SpareHand/
  App.xaml(.cs)            startup, single instance, notification icon, settings window, self-test flags
  Input/DragWatcher.cs     low-level mouse + keyboard hooks on their own thread: real-drag detection, double-tap
  Input/TriggerGuard.cs    stays quiet in fullscreen apps, games, presentations and excluded apps
  Tray/TrayWindow          the tray: placement, tiles, drag payload, drop dispatch
  Tray/TrayTile            one drop target (Normal / Hot / Dimmed)
  Tray/Placement.cs        near-cursor placement, clamped to the monitor's work area
  Tray/PopupChrome.cs      PowerToys-style frameless pop-up window chrome
  Targets/                 what each tile does with a drop
  Hold/HoldStore.cs        the held items (paths only) + undoable removals, in %LOCALAPPDATA%\SpareHand
  Hold/HoldLibrary         the library: grid, filter, multi-select, drag-out, context menu
  LocalSend/               protocol client, persisted identity certificate, device picker
  Shell/                   shell icons, open Explorer folders, file moves, Quick Share verb, Share sheet, drop host,
                           notification-area icon
  MemoryTrim.cs            idle working-set trim (PowerToys Quick Access pattern)
  SettingsWindow           settings page (Community Toolkit settings controls)
```

## How a drop works

1. **Detect.** A `WH_MOUSE_LL` hook sees the left button go down and the cursor move past the system drag threshold.
   The drag counts as real only once the shell's `SysDragImage` window exists, which rules out window moves and text
   selection.
2. **Prefetch.** On drag start, open Explorer folders are read (typed shell COM on an STA thread) to seed the "More…"
   folder picker, and LocalSend discovery is refreshed.
3. **Trigger.** A `WH_KEYBOARD_LL` hook watches for two taps of the trigger key (each under 250 ms, the gap within the
   double-click time). Any other key resets it, so typing never fires it.
4. **Show.** The tray window already exists, cloaked and parked off-screen with its drop target registered. It's
   moved next to the cursor and uncloaked without activation.
5. **Drop.** WinUI `DragEnter` resolves the dragged storage items once; `DragOver` asks the tile's target for a plan
   (accept + caption); `Drop` hides the tray first, then runs the target.
   The tray always reports **Copy** to the drag source, even for moves it performs itself: a source that sees "Move"
   may delete the original after the drop.

## Lessons that shaped the code

These cost real time; please don't reintroduce them.

- **Declare PerMonitorV2 in the manifest.** Without it, Windows DPI-virtualises the app on scaled displays and its
  coordinates disagree with every other process, so drops land on whatever is underneath.
- **Native callbacks must never throw.** An exception escaping a window procedure or hook into Windows fail-fasts the
  whole process (`0x80131623`). All of them are wrapped.
- **Keep callback delegates alive for the process.** The notification icon once registered its window class with a
  per-instance delegate; re-adding the icon reused the class after that delegate was collected, and the process died
  on the next message. Now there's one static window procedure. `--gc-stress` exists to catch this class of bug.
- **`DispatcherQueueTimer`s must be referenced** (fields, not locals), or they can be collected before firing.
- **Settings pages: ignore change events while loading.** One handler read several controls and wrote half-loaded
  values back.
- **Maximised windows are not fullscreen.** Windows draws them a few pixels past the monitor edge; check `IsZoomed`.
- **Quick Share needs foreground rights** (`AllowSetForegroundWindow`) before invoking its verb, or it opens behind.
- **The Share sheet needs a visible, activated owner window**; a cloaked one gets nothing, silently.
- **LocalSend needs a TLS client certificate** on Windows (see research).
- **Opening WinUI windows during app startup** (before the first frame) crashed the runtime once; self-tests wait.

## Data

`%LOCALAPPDATA%\SpareHand\`: `settings.json`, `hold.json` (paths), `hold-removed.json` (undo batches),
`localsend-identity.pfx` (the self-signed LocalSend identity), `logs\`.
