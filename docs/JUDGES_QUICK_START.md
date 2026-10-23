# Judges' quick start — R30-R3 polished presentation

**Target systems:** Windows x64 and Ubuntu/Linux x64 graphical desktops. **Do not run elevated/as root.** No installation wizard is required; extract the entire self-contained archive.

## Windows

1. Download `PhotoSiftReborn-Final-Presentation-win-x64-CANDIDATE.zip` from the GitHub Release and check its SHA-256 against `SHA256SUMS.txt` on the release.
2. Extract fully into a new user-owned directory (do not open/run the executable from inside a ZIP).
3. Run `PhotoSiftReborn.exe`. The build is not digitally signed; treat any operating-system warning accordingly and verify the published checksum. It contains the .NET runtime; no .NET SDK is needed.
4. Explore Focus, Collection, Changes, History, Tools and Diagnostics. Import a directory of **copies** of images to inspect previews and keyboard routes.

## Ubuntu/Linux x64

1. Download `PhotoSiftReborn-Final-Presentation-linux-x64-CANDIDATE.tar.gz`; check SHA-256.
2. Extract into a writable user directory (for example `~/Downloads/PhotoSiftReborn`) using `tar -xzf <archive> -C <destination>`.
3. In a graphical session run `./PhotoSiftReborn`. If executable permission is not preserved, `chmod +x PhotoSiftReborn` first. The build is self-contained .NET 10, but native desktop dependencies (display server, fonts, graphics stack and supported Linux shared libraries) still depend on the host distribution.
4. Use copies of disposable images for the first session. Do not run as root or work from a VirtualBox `vboxsf` shared folder for native file-operation verification.

## A useful first review

Open three or more disposable photos; inspect Focus's single-photo viewer and Collection's visual overview; visit Changes to see decisions separately from applied file actions; open History/Diagnostics to understand how the application reports recorded operations. Use no irreversible actions on irreplaceable files. Where a native operation is held, preserve recovery data instead of forcing a retry.

**Evidence limit:** Owner-tested on Windows 11 x64 and Ubuntu 24.04 x64; not independently smoke-tested on a pristine no-SDK machine or macOS. General arbitrary ordinary mixed-family operations are not authorized by the R30-R3 test scope.
