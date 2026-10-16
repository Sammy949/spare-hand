# Research

What we looked at before and while building Spare Hand, and what it changed. Counts are what was actually read;
anything estimated is marked ≈.

## 1. Windows 11's Drag Tray (later "Drop Tray")

A strip that slid down from the top-centre of the screen when you dragged a local file, offering share targets
(WhatsApp, Outlook, Teams, Phone Link) and "More…" for the Share sheet.

| When | What |
|---|---|
| Jan 2025 | Hidden in Insider Beta build 22635.4805 |
| Feb 2025 | Announced in Beta build 22635.4950 |
| Late 2025 | "Move to folder" and multi-file sharing added |
| Mar 2026 | Renamed Drop Tray; settings moved to System › Multitasking |
| Apr 2026 | Made smaller after complaints |
| Aug 2026 | Removed from Insider builds: *"Based on feedback, we're removing the experience in this build."* |

**Why it failed:** it appeared on proximity alone, so ordinary drags near the top edge (tabs, address bars) got
hijacked; it was on by default for everyone; and it was mostly about sharing, while moving files is the common job.
The parts people liked: move-to-folder, and touch-friendliness.

**What Spare Hand does differently:** it never appears unless you double-tap the trigger during a real drag, it opens
at the cursor instead of a fixed edge, and moving and holding sit beside sharing.

Sources: [Windows Insider blog, 22635.4950](https://blogs.windows.com/windows-insider/2025/02/21/announcing-windows-11-insider-preview-build-22635-4950/),
[Windows Latest](https://www.windowslatest.com/2026/08/20/microsoft-is-killing-the-windows-11-feature-that-hijacked-your-drag-and-drop-its-called-drag-tray/),
[Pureinfotech](https://pureinfotech.com/microsoft-removes-drop-tray-windows-11/).

## 2. Shelf apps: 2,968 App Store reviews

Every review the App Store review feed exposes (US, UK, CA, AU, IN; de-duplicated) for 13 Mac shelf apps: Yoink (857),
Dropover (623), NotchBox (1,079), Unclutter (244), Dropzone 4 (89), DesktopShelves (48), and a few smaller ones.
2,678 are 4–5★, 119 are 3★, 171 are 1–2★. Every 1–2★ review and every "I wish…" sentence was read by hand.

**Why people love them** (4–5★ mentions): daily use (445), "just works" (408), moving between apps and spaces (81),
"should be built into the OS" (68). One Yoink reviewer: *"If you know what it feels like to wish you had an extra hand
to hold things with this is just like that."*

**Why people give 1–2★** (≈ hand counts of 171):

| Cause | ≈ |
|---|---|
| Money tactics: undisclosed trials, countdown delays, subscriptions, rating prompts, upgrade nags | 45 |
| Won't open, crashes, broke after an OS update | 30 |
| Gets in the way: won't hide, covers the drop target | 9 |
| No sync to other devices | 8 |
| Lost or corrupted files; a deleted item still on the shelf got sent by mistake | 7 |
| Drag-out fails, or several files drop "stacked as one" | 6 |
| CPU, battery, boot time | 5 |

**The most common wish among happy users** (≈12) was getting files to their *other* devices.

**What it changed:** sharing (Quick Share, LocalSend) is the centre, not an add-on; Spare Hand stays free and never
nags; reliability outranks features (survive reboot and sleep, never lose a held file, separate files on drag-out).

## 3. Notch apps (macOS)

NotchNook, Boring Notch, NotchDrop, Atoll, Droppy, Dropover's notch mode and others put a file shelf in the MacBook
notch. Their users mostly install them for media controls and looks: Boring Notch's most-reacted issue is Now Playing
breaking (54 reactions). Most of the next ones ask to turn off the wobbly animation (26, 21, 21, 16) or hide it in
fullscreen (14).

Ideas worth taking: reopen recently cleared items (Dropover), "nothing leaves the shelf on its own" (Perch), select
several then act on them (Boring Notch). Ideas to avoid: copying files in and deleting them after 24 hours (NotchDrop's
default), moving the original on drag-out (reported for NotchNook).

**What it changed:** no widgets, no always-visible bar, no wobble; quiet in fullscreen apps and games; Hold keeps
references forever until you remove them. On Windows there is no notch, so the library opens at the cursor, on
whichever monitor you're using.

## 4. PowerToys

Requests for a temporary file shelf have been open since 2019
([#128](https://github.com/microsoft/PowerToys/issues/128), [#37136](https://github.com/microsoft/PowerToys/issues/37136)),
and an "OmniTray" proposal ([#50126](https://github.com/microsoft/PowerToys/issues/50126)) was closed by its author after
a maintainer asked whether it should be a PowerToy or a standalone app. Spare Hand ships standalone first, built to
PowerToys' structure and design language so it could move upstream later.

## 5. Sharing integrations

- **Quick Share** registers an Explorer verb (`IExplorerCommand`, CLSID `AD54D7CA-052D-4730-B0F2-2BA34D3438DC`).
  Invoking it with a shell item array does exactly what right-click → *Send with Quick Share* does, as long as the
  caller first grants foreground rights (`AllowSetForegroundWindow`). Without that, Quick Share loads the file but
  stays behind other windows.
- **LocalSend** has a public protocol (multicast announce on `224.0.0.167:53317`, HTTPS REST on port 53317).
  Newer receivers request a TLS client certificate; Windows' TLS stack fails the handshake without one, so Spare Hand
  presents a persisted self-signed identity whose SHA-256 is its fingerprint. Discovery uses multicast plus an HTTPS
  scan of the local /24, because some phones don't answer multicast.
- **Windows Share sheet** via `IDataTransferManagerInterop::ShowShareUIForWindow`. The sheet attaches to its owner
  window and shows nothing if that window is hidden, so it gets its own small activated host window.

## Screenshots into Hold

**Where they go:** Windows' *Screenshots* known folder (`FOLDERID_Screenshots`, normally `Pictures\Screenshots`; under
OneDrive when it backs up Pictures). Win+PrtScn saves there, and so does Snipping Tool (Win+Shift+S, and the PrtScn
key by default on Windows 11), whose auto-save is on by default. On the test PC, 381 of 427 screenshots were Snipping
Tool's and 45 were Win+PrtScn's. Game Bar captures go to `Videos\Captures`, screen recordings to `Videos\Screen Recordings`.

**How the files are written** (measured with a file-system watcher while the shortcuts were pressed):

| Capture | Appears after | Write pattern |
|---|---|---|
| Win+PrtScn | 0.7 s | Complete at creation, a few change events within 80 ms |
| Snipping Tool | 0.23 s after the snip | Created empty (0 bytes), filled ~90 ms later |
| Game Bar | 2.6 s | Created empty and locked, a transient 4.5 MB size, then 292 KB; named after the active window |

So a new file is held only after its writes have been quiet for 0.5 s and it opens unlocked as a real PNG, JPEG, GIF
or BMP. **Mac apps:** Dropover watches the screenshot folder and opens a shelf per screenshot (it can't see
clipboard-only captures); Yoink has no built-in option, and its users rely on Hazel or Automator folder rules.
Sources: [Dropover: Screenshot Shelves](https://dropoverapp.com/kb/screenshots),
[Eleven Forum: Snipping Tool auto-save](https://elevenforum.com/t/enable-or-disable-snipping-tool-auto-save-screenshots-in-windows-11.9418/).
