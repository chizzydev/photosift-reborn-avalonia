# PhotoSift Reborn — R30-R3 polished presentation release

**Contest publication candidate:** R30-R3 polished presentation. **Not** R31 experimental.

## Contents

- Windows x64: `PhotoSiftReborn-Final-Presentation-win-x64-CANDIDATE.zip`, expected SHA-256 `77310C425CD25572BCF92D5066773CC53706BB59A09C8B73A2FBF8E9C82945FB`.
- Linux x64: `PhotoSiftReborn-Final-Presentation-linux-x64-CANDIDATE.tar.gz`, expected SHA-256 `267D3424EC2F5E82D094ACDE701F253bb35407e31b248bebc8b37473c12a3601`.
- Corresponding source: **must be generated from the 794-entry base plus verified one-file Studio presentation overlay**; check the source manifest and resulting ZIP SHA before publishing.
- GPLv3 licence and original RL Vision PhotoSift attribution.

This package is an independent Avalonia 12 / .NET 10 port, not an official RL Vision distribution. The archives are self-contained portable builds, not signed installers. No macOS runtime/native operation support is claimed.

## Observed baseline evidence

- Polished Windows executable: 1,274 scoped generated-only native tests PASS; owner observed the actual Studio pages and imported generated photos.
- Polished Linux executable: 1,286 scoped native tests PASS; owner observed Focus/Collection with generated photos, and GUI exited cleanly.
- SHA-256 manifest and extracted-archive verification for each polished build.

## Safety scope

This release separates sorting selections from actual filesystem changes, exposes reviewed Changes and history, and preserves app-managed recovery information. Native file-changing operations are constrained to the scenarios for which evidence exists. Ordinary arbitrary mixed-family operation authority is not enabled; never treat a list of native test labels as proof that arbitrary personal photographs are safe to modify. Prefer copied/disposable inputs for initial use. Preserve recovery databases, journals and app-owned archives.

## Installation

See `docs/JUDGES_QUICK_START.md` and the `README_RELEASE.txt` inside each binary archive. Keep original PhotoSift installed separately; do not attempt in-place migration of its settings and undo state.

## Release identity rule

If the newer R31-R3 candidate later passes independent full-scope acceptance, create a **new tagged release with new source hashes and assets**. Do not silently replace binaries under the R30-R3 tag, and do not present the R31 test counts as R30-R3 evidence.
