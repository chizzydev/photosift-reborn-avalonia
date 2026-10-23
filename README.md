# PhotoSift Reborn

**Sort photos at keyboard speed. Know exactly what happens to your files.**

PhotoSift Reborn brings [RL Vision's PhotoSift](https://github.com/rlv-dan/PhotoSift) into a modern Avalonia desktop app. The original had a great idea: look at a photo, press a key, and get on with the next one. I wanted to keep that rhythm, but make it easier to browse a collection, review decisions, and recover from mistakes.

The result is a desktop photo sorter built with **Avalonia 12 and .NET 10**, with a Midnight Jade interface and portable releases for **Windows x64 and Linux x64**.

**Avalonia Port Challenge 2026 — Best Everyday Tool**

## From PhotoSift to PhotoSift Reborn

| Original PhotoSift | PhotoSift Reborn |
| --- | --- |
| ![Original PhotoSift running on Windows, owner-captured](docs/screenshots/before-original-photosift.png) | ![PhotoSift Reborn Focus view on Windows](docs/screenshots/after-reborn-focus.png) |

The original PhotoSift screenshot was captured directly from RL Vision's application on Windows on October 23, 2026. The Reborn screenshots were also taken from running desktop builds.

**Explore the app:** [Collection](docs/screenshots/after-reborn-collection.png) · [Changes](docs/screenshots/after-reborn-changes.png) · [History & recovery](docs/screenshots/after-reborn-history.png) · [Ubuntu Focus](docs/screenshots/after-ubuntu-focus.png)

## What you can do

- **Focus:** review one photo at a time, use keyboard destinations, and move naturally through the collection.
- **Collection:** get a contact-sheet view, preview images on demand, and jump back to the one you want.
- **Changes:** see saved sorting choices separately from file operations, review exact paths, and apply supported changes deliberately.
- **History & recovery:** inspect recorded operations, their state, and the recovery or Undo options available for them.
- **Tools and diagnostics:** find workspace settings, inspection tools, and information about the current session.

The main design decision is simple: **choosing where a photo belongs should not immediately change the original file.** Reborn gives that choice a clear review step. Filesystem operations have identity and destination checks; when those checks cannot establish that an action is safe, the app holds it rather than guessing.

## Download

**[Get PhotoSift Reborn for Windows or Linux](https://github.com/chizzydev/photosift-reborn-avalonia/releases/tag/contest-r30-r3-20261023)**

| Platform | Download | Run |
| --- | --- | --- |
| Windows x64 | `PhotoSiftReborn-Final-Presentation-win-x64-CANDIDATE.zip` | Extract the ZIP and open `PhotoSiftReborn.exe`. |
| Linux x64 | `PhotoSiftReborn-Final-Presentation-linux-x64-CANDIDATE.tar.gz` | Extract the archive and run `./PhotoSiftReborn` in a desktop session. |

These are self-contained .NET 10 portable builds, not installers. You don't need the .NET SDK to run them. Check the included `SHA256SUMS.txt` before launching. For platform notes, see the [quick start](docs/JUDGES_QUICK_START.md).

For a first session, use **copies of photos** and a separate output folder. Review proposed file changes before applying them, and keep the app's recovery data alongside your workspace.

## The migration: what changed, and why

### Keeping what made PhotoSift useful

The original was a compact, keyboard-first Windows utility. Its short feedback loop was worth saving. I kept the idea of assigning destinations by key, then built a Studio around it so you don't have to choose between sorting quickly and seeing the bigger picture.

Focus is still the place to work through photographs. Collection adds an overview to the original workflow, while Changes makes decisions visible before a file is touched. History brings recovery into the interface rather than leaving it buried in a database.

### Moving the interface to Avalonia

The rewrite uses Avalonia for the desktop UI, with application, core, and filesystem code separated into `PhotoSiftReborn.App`, `PhotoSiftReborn.Core`, and `PhotoSiftReborn.IO`.

The same Studio interface runs on Windows and Ubuntu. That shared UI was one of the big wins of the port: I could spend time refining one experience instead of maintaining separate front ends.

### The part that took the most work

The difficult part wasn't drawing windows. It was making sure a saved decision still referred to the **same file** when someone eventually clicked Apply or Undo.

A file can move, change, or collide with another file between selection and execution. Reborn records operation history in SQLite, checks native file identity and destinations, and separates review from execution. Recovery is part of that workflow too: it matters just as much to show what happened after a restart as it does to show that a button was pressed.

Windows and Linux have different native filesystem behavior, so the UI can be shared while the file-handling layer uses platform-specific checks. A failed safety check produces a visible hold instead of an overwrite or an unverified success.

### What surprised me

Two things stood out. First, porting the UI was only one slice of the work; the original application's quick actions had to be rethought once the new interface exposed review and recovery explicitly. Second, restart behavior is a feature of a file organizer, not just a testing detail. The user needs to be able to reopen a session and understand what was decided, what was applied, and what can be undone.

That led to a more deliberate workflow than the original's immediate-action model. It's a trade-off I'm happy with: the keyboard sorting stays quick, and the consequential steps stay visible.

### Building and testing the result

The published release was exercised on **Windows 11 x64** and **Ubuntu 24.04 x64**. The accepted native test suites completed with **1,274 passing tests on Windows** and **1,286 on Ubuntu**, with no failures in those runs. I also opened the portable applications on both systems and checked the actual interface using demonstration photos. The screenshots above come from those desktop sessions.

To build from source, install the **.NET 10 SDK**, clone this repository, and build the application project:

```sh
dotnet build src/PhotoSiftReborn.App/PhotoSiftReborn.csproj -c Release
```

The public source includes the UI, core and filesystem layers, supporting assets, and the GPLv3 licence. The [release notes](docs/RELEASE_NOTES_R30_R3.md) and [quick start](docs/JUDGES_QUICK_START.md) provide the details needed to run the builds.

## Original project and licence

PhotoSift was created by **RL Vision** and predates this challenge. Original project: [rlv-dan/PhotoSift](https://github.com/rlv-dan/PhotoSift); upstream reference commit: `a5e83b9198f5dd251053c48182cc37fea9ff6670`.

PhotoSift Reborn is an independent modernization, **not an official RL Vision release**. It is distributed under **GPLv3**; see [LICENSE-GPLv3.txt](LICENSE-GPLv3.txt).

Built for the [Avalonia Port Challenge](https://avaloniaui.net/blog/avalonia-port-challenge), **Best Everyday Tool** category.
