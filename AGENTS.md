# Shimmer-C-API

The C# host API. Controls and streams from Shimmer3/3R running LogAndStream, and from Verisense over
BLE. One of the native implementations of the wire format — not a wrapper over anything.

## Two independent trees

| Tree | Device | Solution |
|---|---|---|
| `ShimmerAPI/` | Shimmer3 / 3R over Bluetooth serial | `ShimmerAPI/ShimmerAPI.sln`, tests in `ShimmerAPITest.sln` |
| `ShimmerBLE/` | Verisense over BLE | `ShimmerBLE/ShimmerBLEAPItest.sln` |

Both libraries target **netstandard2.0**. The rest of the top level is examples and console apps.

**Build `ShimmerAPI` before any example** — the examples reference the built DLL, not the project, so
a clean checkout fails until the library exists. The README says this and it is easy to miss.

## CI covers both trees, but only on `master`

Two workflows, both triggered on push to `master` and PRs into it:

- `ShimmerBLEAPIUnitTest.yml` — `dotnet` on `ShimmerBLE/ShimmerBLEAPItest.sln`. Note it **removes
  `PropertyChanged.Fody` and reinstalls 3.4.0** before building. That pin is load-bearing; if you
  change the Fody dependency, change it here too or CI diverges from what the csproj declares.
- `ShimmerBluetoothAPIUnitTest.yml` — MSBuild + VSTest on `ShimmerAPI/ShimmerAPITest.sln`, running
  `ShimmerUnitTests/bin/Debug/ShimmerBluetoothTests.dll`.

Anything outside those two solutions — every example app, the 32Feet apps, the gRPC and protobuf
tests, `ShimmerCaptureXamarin` — is **never built by CI**.

## Releases publish to NuGet from CI

To release, open **Actions → PublishNuGet → Run workflow** on `master` and pick the part to bump —
`patch` by default. That is the whole process: no version edits, no uprev PR, no hand-made release.

The run takes the latest `vX.Y.Z` tag (other tag formats in this repo, such as `BLEv…` and `REV0_…`,
are ignored), bumps it, and stamps that version into the build: `-p:Version` for both projects, plus a
rewrite of the two version attributes in `Shimmer32FeetAPI/Properties/AssemblyInfo.cs` inside the CI
checkout. **The `<Version>` in the csproj files and the version in that `AssemblyInfo.cs` are only the
defaults for local builds** — they are not the released version, and nobody bumps them.

A separate `publish` job then pushes exactly the packages the `build` job made to nuget.org (profile
`ShimmerEngineering`) — ShimmerAPI first, because the Shimmer32FeetAPI package depends on that exact
ShimmerAPI version — and only then creates the `vX.Y.Z` tag and a GitHub release with generated notes.
A release created by hand on GitHub publishes nothing.

**If a release fails, recover with *Re-run failed jobs* on that same run — never a new Run
workflow.** The re-run reuses that run's packages and commit, and `--skip-duplicate` skips whatever was
already pushed. A new run would pick the same version (no tag exists yet) but build it from whatever
`master` is now, so ShimmerAPI could stay the older build while Shimmer32FeetAPI and the tag come from
the newer commit.

Publishing uses **trusted publishing**, so there is no API key to store or renew. Instead, a policy on
that nuget.org profile trusts `ShimmerResearch/Shimmer-C-API`, the workflow file name
`PublishNuGet.yml` and the GitHub environment `nuget-release`, and each run exchanges GitHub's OIDC
token for a one-hour key. Only `master` may deploy to `nuget-release`, which is what stops a branch
from publishing by editing its own copy of the workflow — the `if:` guards in the file cannot, because
the branch can edit them too. Renaming the workflow file or the environment, or moving the repo, breaks
publishing until the policy is updated to match.

PRs that touch either package run only the `build` job, at the next patch version and with read-only
permissions, and attach the `.nupkg` files as an artifact.

## The same logic exists three times in this repo

Cross-repo sibling APIs are the well-known trap. This repo has the problem *internally* as well, so
fixing `ShimmerBluetooth.cs` alone is not enough — and mirroring blindly into all three is wrong too:

| Copy | Lines | Last changed | Treat as |
|---|---|---|---|
| `ShimmerAPI/ShimmerAPI/ShimmerBluetooth.cs` | 7983 | 2026-01-13 | **the live one** |
| `ShimmerCaptureXamarin/ShimmerCaptureXamarin/ShimmerBluetooth.cs` | 5958 | 2017-07-11 | abandoned fork — 3325 lines differ |
| `ShimmerAPI/TestSerialPort/Program.cs` | 122 | — | its own timestamp unwrap and calibration |

`ShimmerCaptureXamarin` has its own solution and is in no CI workflow. It has not been touched in
nine years. Do not port a fix into it without asking whether it should exist at all.

`TestSerialPort/Program.cs` is more dangerous because it looks like a throwaway example: it carries
its own `parseTimeStamps`, `CalibrateTimeStamp`, `CurrentTimeStampCycle` and
`TimeStampPacketRawMaxValue`. That is the DEV-1023 unwrap rule, re-implemented. A fix to the unwrap
rule applies here.

`ShimmerDevice.cs` (102 lines) is a thin base type, not a fourth copy.

## Sibling implementations

The wire format is implemented natively in Java, Python, TypeScript, Swift and here, plus a separate
2025E parser in `ASM_BaseStation`. Nothing links them and nothing tests them together. A parsing,
protocol, calibration or timestamp fix here almost certainly applies to the others — check them and
say which need it, including "none", so the check is visible.

`Shimmer-Labview-API` wraps this API, and its README says LabVIEW support is migrating onto it, so
this is the one to fix rather than the old LabVIEW API.

## README links point at a retired org

The README links to `ShimmerEngineering/Shimmer-C-API` for releases, the wiki and the Shimmer3R
integration notes. That org no longer exists; the links work only on GitHub's rename redirect.
