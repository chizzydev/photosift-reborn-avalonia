# Run PhotoSift Reborn

PhotoSift Reborn comes as a portable desktop app for **Windows x64** and **Linux x64**. There's no installer, and you don't need the .NET SDK to run the release builds.

**[Download the release](https://github.com/chizzydev/photosift-reborn-avalonia/releases/tag/contest-r30-r3-20261023)** and check your archive against `SHA256SUMS.txt`.

## Windows

1. Download `PhotoSiftReborn-Final-Presentation-win-x64-CANDIDATE.zip`.
2. Extract the entire ZIP into a folder you own.
3. Open `PhotoSiftReborn.exe`. The app is unsigned, so check the published hash if Windows displays a security prompt.
4. Start with Focus and Collection; then explore Changes, History & recovery, and Tools.

## Ubuntu/Linux

1. Download `PhotoSiftReborn-Final-Presentation-linux-x64-CANDIDATE.tar.gz`.
2. Extract it to a folder in your home directory.
3. From that folder, run `./PhotoSiftReborn` in a graphical desktop session. If the executable bit was lost during extraction, run `chmod +x PhotoSiftReborn` first.
4. Explore the same Focus, Collection, Changes, and History views. Standard Linux graphical libraries, fonts, and a running display session are required.

## A five-minute tour

Start with a folder of **copied sample photos**, not your only copies of personal pictures.

- **Focus** is for working through photos with destination keys.
- **Collection** gives you a visual overview of the whole set.
- **Changes** shows saved choices and provides review before file operations.
- **History & recovery** shows what happened and which recovery actions are available.

PhotoSift Reborn deliberately distinguishes selecting a destination from modifying a file. Review the full source and destination before applying a change. If the app holds an operation because a safety check fails, leave its recovery files intact and inspect the reported reason.

The published applications were tested in real desktop sessions on Windows 11 and Ubuntu 24.04. For the story behind the port, see the [README](../README.md).
