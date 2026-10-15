# Roadmap

The job: **a spare hand for moving and sharing files on Windows.** Everything here serves that, or is rejected.

## Done

- Real-drag detection and a configurable double-tap trigger (Alt, Ctrl or Shift)
- The tray at the cursor: Hold, Quick Share, LocalSend (device picker after the drop, with cancel), the Windows Share
  sheet, Desktop and open Explorer folders (Explorer-exact move/copy with undo)
- The Hold library: thumbnails, filter, multi-select drag-out, context menu, missing-file state with Locate,
  clear with undo, click outside to close
- Notification-area icon, settings page, start with Windows, quiet in fullscreen apps and excluded apps
- Idle memory 6–9 MB (Task Manager), PowerToys-style trimming

## v1: before a first release

- Outlook attachments and browser images (virtual files)
- Folders to LocalSend
- Keep the LocalSend picker open after a send, for a second device
- Paste (Ctrl+V) files or a copied screenshot into the library
- Hook watchdog: re-arm the hooks if Windows drops them
- Multi-monitor, mixed-scaling and elevated-window pass
- Reliability pass: survive reboot and sleep, 100-item stress test, multi-file drag-out lands as separate files
- The "lands in Hold" moment (no wobble)
- One-time first-run tip, never repeated
- App icon and identity, installer, signing, CI releases

## Next

- Opt-in: new screenshots and downloads land in Hold automatically
- Space opens the item in PowerToys Peek
- A browsable "Recently removed" list
- Pinned items
- Any app with a "Send with …" Explorer verb as an extra Send tile

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
