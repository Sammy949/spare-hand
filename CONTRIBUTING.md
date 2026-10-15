# Contributing

Thanks for wanting to help. Bug reports from real setups are the most useful thing right now: different monitors,
scaling, apps you drag from, phones you send to.

## Build

```powershell
dotnet build src/SpareHand/SpareHand.csproj -c Debug -p:Platform=x64
```

Requires the .NET 10 SDK on x64 Windows. `tools/build.ps1` does the same and stops a running copy first (it locks the
exe). From WSL, `tools/build.sh` builds on the Windows side.

The app writes a log to `%LOCALAPPDATA%\SpareHand\logs`. Please attach the relevant lines to bug reports (check them
for file names you'd rather not share first).

## Testing without dragging

Most surfaces have self-test flags, so they can be opened and screenshotted without moving the mouse:

| Flag | Does |
|---|---|
| `--selftest` | Opens the tray at a fixed point; add `--hot` to show a tile's drop-hover state |
| `--theme light` / `--theme dark` | Forces a theme on the pop-up surfaces |
| `--library-test` | Opens the Hold library and keeps it open |
| `--picker-test <file>` | Opens the LocalSend picker for a file |
| `--share-test <file>` | Opens the Windows Share sheet for a file |
| `--settings` | Opens the settings window |
| `--toast-test` | Shows a test notification |
| `--gc-stress` | Garbage-collects every second, to catch delegate-lifetime bugs |
| `--quit-test` | Quits after 4 s |

`tools/` has small helpers: UI Automation scripts that click and read controls without the mouse (`uia-*.ps1`), memory
measurement (`mem.ps1`), window listing and screenshots, a drag driver that performs a real OLE drag (it moves your
mouse), and probes for Quick Share and LocalSend.

## House rules

- **Nothing appears uninvited.** No feature may show UI unless the user asked for it.
- **Hold never copies, moves, modifies or expires files.**
- **No nags, ever**: no rating prompts, upsells or recurring welcome screens.
- **Offline and private**: no telemetry, no account; the only network use is LocalSend on the local network.
- **PowerToys design language** (see [docs/design.md](docs/design.md)). The accent colour belongs only to the drop
  target under the cursor.
- **Verify, then claim.** Say what you tested and how in the PR, and what you couldn't test.
- Native callbacks (window procedures, hooks) must never let an exception escape.

## Pull requests

Open an issue first for anything bigger than a fix, so we can agree on the approach. Keep PRs focused, and match the
surrounding code's style and comment density.
