# Roadmap

The job: **a spare hand for moving and sharing files on Windows.** Everything here serves that, or is rejected.

## Done

- Real-drag detection and a configurable double-tap trigger (Alt, Ctrl or Shift)
- The tray at the cursor, a 2 × 2 beside Hold: Quick Share, LocalSend (device picker after the drop, with cancel),
  Desktop, and More… (a folder picker after the drop, starting in the last Explorer window); Explorer-exact move/copy
  with undo. The Windows Share sheet lives in the Hold library's Send with menu.
- The Hold library: thumbnails, filter, multi-select drag-out, context menu, missing-file state with Locate,
  clear with undo, click outside to close
- Notification-area icon, settings page, start with Windows, quiet in fullscreen apps and excluded apps
- Idle memory 6–9 MB (Task Manager), PowerToys-style trimming
- A per-user installer (SpareHand-Setup.exe, 24 MB, no admin), trimmed to 90 MB installed
- A first-run guide that has you do the gesture for real, reopenable from the tray menu
- New screenshots land in Hold quietly, on by default (Screenshots folder: Snipping Tool, Win+PrtScn); only today's stay
- Opening Spare Hand from Start shows settings; the notification-area icon opens the library

## v1: before a first release

- Outlook attachments and browser images (virtual files)
- Folders to LocalSend
- Keep the LocalSend picker open after a send, for a second device
- Paste (Ctrl+V) files or a copied screenshot into the library
- Hook watchdog: re-arm the hooks if Windows drops them
- Multi-monitor, mixed-scaling and elevated-window pass
- Reliability pass: survive reboot and sleep, 100-item stress test, multi-file drag-out lands as separate files
- The "lands in Hold" moment (no wobble)
- Code signing, so SmartScreen stops warning
- Optional monochrome notification-area icon (like PowerToys' "theme adaptive" setting)

## Next

- Opt-in: new downloads (and Game Bar captures) land in Hold automatically
- Space opens the item in PowerToys Peek
- A browsable "Recently removed" list
- Pinned items
- Any app with a "Send with …" Explorer verb as an extra Send tile

## Other platforms

Windows first, because that's where the gap is: the built-in tray is gone and the polished shelf apps are Mac-only.

- **macOS** is possible (drag-triggered shelves exist there) but crowded, and AirDrop already covers sending to your
  own devices.
- **Linux** is hard on Wayland: apps can't watch input or drags in other apps by design, so the mid-drag trigger
  would need a GNOME Shell extension or a KWin script per desktop. X11 is workable.

To keep the door open, the parts that could be shared (the Hold store, the LocalSend protocol client) should move
into a library with no Windows dependencies. Today the LocalSend client still touches the registry and Windows memory
APIs, so that split is real work, not just a rename.

## Later

Text and link items · actions on drop (zip, convert) · touch and pen trigger · optional shake trigger (off by
default) · a PowerToys module or Command Palette integration

## Not doing

| Idea | Why not |
|---|---|
| Media, calendar, weather widgets | The main source of breakage in notch apps, and not this job |
| An always-visible bar or island | Covers tabs and games and costs battery; gesture-only is the point |
| Clipboard history | Win+V exists |
| Cloud sync or upload links | Unreliable everywhere it exists; Spare Hand stays offline |
| Auto-expiring Hold items | Trust is the product |
