# tools/hil: hardware-in-the-loop tests of the C# API

The unit tests in `ShimmerAPI/ShimmerUnitTests` run the API against simulated devices. This suite runs it
against real Shimmer3 and Shimmer3R units, through the same public classes an application uses:

- **Bluetooth Classic:** `ShimmerLogAndStreamSystemSerialPort`, on the COM port Windows makes for a
  paired unit.
- **BLE:** `ShimmerLogAndStream32FeetBLE` (Shimmer32FeetAPI).

One command builds the suite against this checkout, runs every item on every unit and transport the
bench file lists, and prints pass, fail or `not_covered` per item:

```powershell
.\tools\hil\run-csharp-hil.ps1 -Rig C:\path\to\rig.toml
```

It follows the other Shimmer HILs rather than inventing its own conventions:

- **The bench file and the item model** come from the firmware HIL (log-and-stream-common `Test/hil`)
  and the shimmer-logandstream HIL:
  - the same `rig.toml`;
  - dotted item ids;
  - an item's status is the worst of its runs: fail, then `not_covered`, then pass.
- **The checks on a stream** are the Android HIL's (ShimmerAndroidAPI `tools/hil`), as shimmer-logandstream's
  `hil/checks.py` has them.
- **The release gate** works as the Android HIL's does.

## Setup

- **A Windows PC with Bluetooth, and Visual Studio** with MSBuild. The runner finds it with `vswhere`.
- **Pair each unit's Classic side in Windows** (Settings > Bluetooth & devices). BLE needs no pairing.
- **A bench file** in the firmware HIL's `rig.toml` format. Start from
  [`rig.example.toml`](rig.example.toml). Name each unit by its Bluetooth MAC, never by COM number: the
  suite finds the COM port from the MAC, because Windows reassigns COM numbers.

  This repo is public, so keep the real file outside it. A bench file that serves the firmware or
  shimmer-logandstream HIL works here unchanged, because keys this suite does not use are ignored. Point
  the runner at it with `-Rig`, or set `$env:HIL_RIG`.
- **Only one host at a time.** A Shimmer takes one link at a time. Close Consensys, the phone running the
  Android HIL, and any other suite before a run.

## Running it

```powershell
.\tools\hil\run-csharp-hil.ps1                                         # everything, on $env:HIL_RIG
.\tools\hil\run-csharp-hil.ps1 -Item stream.rest -Unit s3r-a -Transport ble
.\tools\hil\run-csharp-hil.ps1 -List                                   # the items
.\tools\hil\run-csharp-hil.ps1 -Rate 1024                              # everything at 1024 Hz
.\tools\hil\run-csharp-hil.ps1 -Report                                 # the release gate's run
```

A full run takes about 45 minutes, nearly half of it the two 10-minute `stream.rollover` runs.
`-Rate` sets the rate every item streams at (default 256 Hz); the release gate runs at the default. Each run's
report and logs go to `tools/hil/out/<time>/`, which git ignores:

- `report.json`: the results, in the shimmer-logandstream HIL's structure.
- `logs/<item>-<unit>-<transport>.log`: everything the suite did and the API said, with times.
- `logs/<item>-<unit>-<transport>.csv`: every sample the checks judged: raw ticks, arrival time,
  calibrated timestamp and accel.

The suite writes accel only at 256 Hz, or `-Rate`, to each unit (the Android HIL's configuration). It writes it only
when a unit's configuration differs, and leaves the unit that way afterwards.

## The items

| Item | What it does | Fails when |
| --- | --- | --- |
| `stream.rest` | Streams 20 s, lying still, with the link CRC off | the stream checks below |
| `crc.stream` | Switches the link CRC to 1 byte and then 2 bytes with `WriteCRCMode`, and streams 10 s in each | the switch does not take, or a stream fails the checks |
| `stream.log_and_battery` | Starts streaming and SD logging together (`StartStreamingandLog`), reads the battery three times while streaming, and stops both | a command fails, no battery reply arrives while streaming, or the stream fails the checks |
| `link.reconnect_loop` | Connects, streams 5 s and disconnects, five times | any cycle fails to connect, stream or disconnect |
| `link.idle_start` | Connects, leaves the link idle 60 s, then streams 10 s | the API reports the connection lost during the wait, streaming starts over 500 ms after the start command (the shimmer-logandstream HIL's limit), or the stream fails the checks |
| `stream.rollover` | Streams 10 minutes, across the 512 s wrap of the 3-byte timestamp, on one transport per unit (Classic where it has one) | the checks below, or the API's calibrated timeline steps by over 1 s, runs backwards, or stops matching the device's ticks |

**The stream checks** use the unit's own timestamps, so a loss is a gap in its sequence rather than the
PC's scheduling. A stream fails when:

- over 1 % of samples are missing;
- a timestamp repeats;
- the rate is more than 1 % off (a Shimmer3R with an odd divider is held to its effective rate);
- the stream goes silent for over 1 s;
- accel at rest is more than 1 m/s² from 9.81. A unit's `accel_tolerance_ms2` widens this: one holding
  only its default calibration reads about 11.3.

**One process per run.** The suite starts a fresh process of itself for each run, so each starts as an
application would. A run can leave behind whatever the API leaves running: a read thread that never
stops, as the BLE link's did until DEV-1270, or a call that never returned. In a shared process those
would slow or break every later run, and the failure would land on the wrong item. The process's exit
takes them with it. A run whose process crashes fails, even when the crash comes after its result, as
an application would have gone down with it; the exception is left in `logs/` as `<run>.crash.txt`.

**Watchdog.** Every API call has 10 s to return (45 s for `Connect`), or the run fails naming the call.
A run that overruns its item's time budget is stopped. Either way the unit is checked on its next run:
if it no longer connects, the rest of its runs are `not_covered`, and the run that hung takes the
blame.

**What the bench lacks is reported, not failed:**

- a transport the rig does not list for a unit;
- a unit with no paired COM port;
- a combination the rig lists under the unit's `known_issues`.

`HilStreamChecksTest` in the unit tests runs the checks on made-up streams in CI.

## Release gate

`-Report` is the run the release relies on. It refuses to start unless:

- it runs every item on every unit and transport;
- the rig has at least one Shimmer3 and one Shimmer3R, and both transports;
- the working tree is clean;
- the commit is on GitHub.

After the run it posts the commit status `csharp-hil` on that commit. The status is `success` only if
every item passed: a `not_covered` item is not a pass.

`PublishNuGet` checks that status before it builds anything. It stops unless the newest `csharp-hil`
status on the commit being released is `success`, or the run is given `hil_waiver`: a Jira key naming
why it is released without one. The release notes say which.

## Things that bite

- **Rest between links.** Windows holds a closed link for a few seconds. The suite rests 8 s between
  runs; set `settle_seconds` in the rig to change it.
- **A Shimmer3 over Classic** can lose samples on its own side: gaps in its own timestamps, with no
  misalignment or CRC failure in the API. Most of all with a 2-byte link CRC (13-18 % in `crc.stream`,
  where a 1-byte CRC loses little or nothing). The Android HIL sees the same (DEV-1248).
- **A Shimmer3 over BLE.** These all showed on the bench the suite was written on:
  - LogAndStream v1.1.5 with an RN4678 can stop answering after a configuration write over BLE. The
    suite writes the configuration only when it differs, which keeps BLE runs clear of that (DEV-1246).
  - The API decodes its accel through the calibration dump, which `ShimmerLogAndStream` marks unfinished,
    and reads about 1.85 times what the same unit gives over Classic (DEV-1271).
  - At 256 Hz it loses samples in bursts. The API does not ask Windows for a short connection interval,
    which this radio needs for that rate (DEV-1272).
