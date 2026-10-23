# PhotoSift Reborn — R30-R3 release

**Avalonia Port Challenge 2026 · Best Everyday Tool**

PhotoSift Reborn gives the original keyboard-driven photo sorter a new Avalonia Studio. This release brings together the Focus viewer, Collection contact sheet, reviewed Changes, History & recovery, and the tools that support them.

## Downloads

Get the portable archives from the [GitHub Release](https://github.com/chizzydev/photosift-reborn-avalonia/releases/tag/contest-r30-r3-20261023):

| Platform | Archive | SHA-256 |
| --- | --- | --- |
| Windows x64 | `PhotoSiftReborn-Final-Presentation-win-x64-CANDIDATE.zip` | `77310C425CD25572BCF92D5066773CC53706BB59A09C8B73A2FBF8E9C82945FB` |
| Linux x64 | `PhotoSiftReborn-Final-Presentation-linux-x64-CANDIDATE.tar.gz` | `267D3424EC2F5E82D094ACDE701F253BB35407E31B248BEBC8B37473C12A3601` |

Both archives include the .NET 10 runtime. Extract them fully before running the app; neither is a signed installer. For step-by-step launch instructions, see the [judge quick start](JUDGES_QUICK_START.md).

## What's in this release

- **Focus** for photo-by-photo keyboard sorting.
- **Collection** for an on-demand contact-sheet overview.
- **Changes** for reviewing decisions and exact paths before file operations.
- **History & recovery** for inspecting recorded actions, status and available Undo.
- A shared **Midnight Jade** Avalonia interface on Windows and Linux.

Behind the UI, the app uses persistent operation history and native filesystem checks to keep saved decisions distinct from actual changes. A failed safety check produces a visible hold rather than guessing about file ownership or overwriting a destination.

## Testing

The accepted native test runs completed with **1,274 passing cases on Windows** and **1,286 on Ubuntu**, with zero failures in those runs. Both portable builds were also launched in desktop sessions and exercised with demonstration photos.

The corresponding GPLv3 source is available in the [public repository](../README.md). The original PhotoSift is by RL Vision; Reborn is an independent modernization.

For a first session, start with copies of photos and a separate output folder. Keep recovery data intact, and review any file operation before applying it.
