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
