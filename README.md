# KSP-sOS

<p align="center">
  <strong>A simpler flight automation and scripting mod for Kerbal Space Program</strong><br>
  Write readable commands. Run them from an in-game terminal. Fly more ambitious missions.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/KSP-1.8--1.12.5-4e9f3d?style=for-the-badge" alt="KSP 1.8 to 1.12.5">
  <img src="https://img.shields.io/badge/status-early%20alpha-d99000?style=for-the-badge" alt="Early alpha">
  <img src="https://img.shields.io/badge/license-MIT-236ad3?style=for-the-badge" alt="MIT License">
</p>

KSP-sOS (simple operating system) is a small, beginner-friendly flight scripting mod for Kerbal Space Program 1.x. Type commands into an in-game terminal, combine them into a program, and let the vessel execute them in sequence.

It is inspired by kOS/KerbOS, but the language is intentionally compact and readable:

```text
LAUNCH;
WHEN ALTITUDE > 10000;
STAGE;
CIRC;
```

> **Project status:** KSP-sOS is an early-alpha prototype. The terminal, parser, staging, attitude control, RCS toggles, conditions, telemetry waits, and approximate circularization are implemented. Test flight programs with a recoverable save.

KSP-sOS is an independent community project and is not affiliated with Squad or Private Division.

## Contents

- [Install the drag-and-drop package](#install-the-drag-and-drop-package)
- [Use the terminal](#using-the-in-game-terminal)
- [Language basics](#basic-syntax-rules)
- [Module directives](#module-directives)
- [Flight commands](#flight-commands)
- [Booleans and control flow](#booleans-and-control-flow)
- [Telemetry conditions](#telemetry-conditions)
- [Example programs](#example-programs)
- [Current scope](#what-is-currently-automated)
- [Troubleshooting](#troubleshooting)
- [Build from source](#build-from-source)
- [Project files](#project-files)

## Install the drag-and-drop package

1. Download `KSP-sOS-drag-and-drop.zip`.
2. Extract it directly into your KSP installation folder.
3. Confirm that this file exists:

   ```text
   Kerbal Space Program/GameData/KSPsOS/Plugins/KSPsOS.dll
   ```

4. Start KSP and load a flight.
5. Press **F8** to open the KSP-sOS terminal.

The active vessel must be controllable. KSP-sOS does not include or replace KSP's own game libraries.

## Using the in-game terminal

The terminal has three areas:

### Terminal input

Type one command, including its final semicolon, then press **Enter** or **EXECUTE**.

```text
PRINT hello from KSP-sOS;
```

Interactive commands run only when another program is not already running. Press **STOP** first if the terminal reports that it is busy.

### Program

Type or paste a complete multi-line flight program here. Press **RUN PROGRAM** to run it from the first line.

```text
LAUNCH;
HOLD PG 10;
STAGE;
HOLD 5;
CIRC;
```

### Terminal output

This area shows status messages, parser errors, staging, circularization, module activation, and messages from `PRINT`.

Press **F8** again to hide the terminal.

## Basic syntax rules

Every command must end with a semicolon:

```text
LAUNCH;
STAGE;
THROTTLE 65;
```

Commands are not case-sensitive, so `launch;`, `LAUNCH;`, and `Launch;` are equivalent. One command goes on each line. Blank lines are ignored.

Use `#` for comments:

```text
# Start the first stage.
LAUNCH;
```

Indentation is optional. It is only used to make blocks easier to read.

## Module directives

Optional behavior modules are enabled at the top of a program with `using` statements. They must appear before the first flight command:

```text
using autostage;
using smartrcs;

LAUNCH;
```

Supported module names are:

| Directive | Status |
| --- | --- |
| `using autostage;` | Active: stages when the current stage's detected fuel resources are depleted. |
| `using clearskies;` | Registered switch; debris-management automation is planned. |
| `using autowarp;` | Registered switch; safe time-warp automation is planned. |
| `using maxscience;` | Registered switch; automatic science planning is planned. `science` is accepted as an alias. |
| `using safeabort;` | Registered switch; automatic safety-abort policy is planned. |
| `using smartrcs;` | Registered switch for the future rendezvous/docking controller. |
| `using autoland;` | Registered switch; automatic landing is planned. |
| `using rendezvous;` | Registered switch; rendezvous guidance is planned. |
| `using docking;` | Registered switch; docking guidance is planned. |
| `using recovery;` | Registered switch; recovery automation is planned. |

Unknown modules and `using` statements placed after flight commands produce parser errors instead of being silently ignored.

## Flight commands

### `LAUNCH;`

Starts a simple launch sequence:

- Sets throttle to 100%.
- Enables the vessel autopilot in prograde mode.
- Activates the next stage.

```text
LAUNCH;
```

### `STAGE;`

Activates the next stage.

```text
STAGE;
```

### `HOLD <seconds>;`

Waits for a number of seconds while leaving the current attitude in place. `WAIT` is accepted as an alias.

```text
HOLD 5;
WAIT 10;
```

### `HOLD <mode> <seconds>;`

Selects an attitude mode, then holds it for the requested number of seconds:

```text
HOLD PG 10;
HOLD RG 2;
HOLD NML 3;
```

Available attitude modes:

| Mode | Meaning |
| --- | --- |
| `PG` | Prograde |
| `RG` | Retrograde |
| `NML` | Normal |
| `ANML` | Antinormal |
| `RI` | Radial in |
| `RO` | Radial out |

If no duration is supplied, the default is one second:

```text
HOLD PG;
```

### `SAS <mode>;`

Changes the autopilot attitude mode without waiting:

```text
SAS PG;
SAS RG;
SAS NML;
```

### `RCS ON;`, `RCS OFF;`, and `RCS TOGGLE;`

Controls the vessel's RCS action group:

```text
RCS ON;
HOLD PG 2;
RCS OFF;
```

### `THROTTLE <value>;`

Sets the throttle. Values from `0` to `1` are treated as fractions; values above `1` are treated as percentages.

```text
THROTTLE 1;     # 100%
THROTTLE 0.5;   # 50%
THROTTLE 65;    # 65%
THROTTLE 0;     # engine cutoff
```

### `CIRC;`

Runs a simple automatic circularization sequence:

1. Points the vessel prograde.
2. Waits until it is near apoapsis.
3. Burns prograde at 75% throttle.
4. Stops when periapsis is within approximately 1.5% of apoapsis.

```text
CIRC;
```

This is an approximate first-generation circularizer, not a full maneuver-node planner.

### `GRAVTURN <body>;`

Starts an adaptive gravity-turn ascent using a body-specific profile:

```text
GRAVTURN KERBIN;
GRAVTURN DUNA;
GRAVTURN MUN;
GRAVTURN MINMUS;
```

Profiles contain tailored starting altitude, pitch-over timing, prograde hand-off speed, throttle behavior, and target apoapsis. Atmospheric bodies such as Kerbin, Duna, Eve, and Laythe use slower pitch programs; low-gravity bodies such as Mun, Minmus, Gilly, Bop, and Pol use shorter pitch-over phases and lower orbital targets.

The selected body must match the vessel's current celestial body. `GRAVTURN MUN;` will stop safely if the vessel is still around Kerbin. The profile is a strong starting point, not a guarantee for every launch vehicle: thrust-to-weight ratio, drag, payload mass, and staging still affect the result.

### `ABORT;`

Toggles the vessel's abort action group and stops the running program:

```text
ABORT;
```

### `PRINT <message>;`

Writes a message to the terminal output:

```text
PRINT Booster stage complete;
```

## Booleans and control flow

### `SET <name> = TRUE/FALSE;`

Creates or changes a boolean flag. Flag names are case-insensitive:

```text
SET AUTO_STAGE = TRUE;
SET LANDING_MODE = FALSE;
```

Flags start as `FALSE` when they have not been set.

### `IF`, `ELSE`, and `END`

Runs a block only when its condition is true:

```text
SET AUTO_STAGE = TRUE;

IF AUTO_STAGE;
    STAGE;
ELSE;
    PRINT staging disabled;
END;
```

The `ELSE` section is optional:

```text
IF LANDING_MODE;
    THROTTLE 20;
END;
```

Use `NOT` to reverse a boolean condition:

```text
IF NOT LANDING_MODE;
    PRINT still in ascent;
END;
```

### `WHEN <condition>;`

Pauses the program until the condition becomes true, then continues with the next line:

```text
WHEN ALTITUDE > 10000;
PRINT ten kilometres reached;
```

Unlike `IF`, `WHEN` waits. This makes it useful for flight milestones.

## Telemetry conditions

Telemetry conditions use this form:

```text
IF <TELEMETRY> <operator> <number>;
WHEN <TELEMETRY> <operator> <number>;
```

Supported operators are `<`, `<=`, `=`, `!=`, `>=`, and `>`.

| Name | Meaning | Typical unit |
| --- | --- | --- |
| `ALTITUDE` | Vessel altitude above the current reference surface | metres |
| `SPEED` | Surface speed | metres/second |
| `APOAPSIS` | Orbit apoapsis altitude | metres |
| `PERIAPSIS` | Orbit periapsis altitude | metres |
| `FUEL` | Total LiquidFuel remaining | resource units |
| `OXIDIZER` | Total Oxidizer remaining | resource units |
| `BATTERY` | Total ElectricCharge remaining | resource units |

Examples:

```text
WHEN ALTITUDE > 1000;
HOLD PG 5;

IF FUEL < 10;
    PRINT low fuel;
    ABORT;
END;

IF BATTERY <= 20;
    PRINT low battery;
END;
```

## Example programs

### Simple ascent

```text
# Simple KSP-sOS ascent program
using autostage;

LAUNCH;
PRINT liftoff;

WHEN ALTITUDE > 100;
HOLD PG 10;

WHEN ALTITUDE > 10000;
THROTTLE 80;
HOLD PG 10;

WHEN APOAPSIS > 70000;
THROTTLE 0;
PRINT coast to apoapsis;
CIRC;
PRINT circularization complete;
```

### Conditional abort

```text
using safeabort;

SET ABORT_ENABLED = TRUE;
LAUNCH;

WHEN ALTITUDE > 500;

IF ABORT_ENABLED;
    IF FUEL < 5;
        PRINT emergency low-fuel abort;
        ABORT;
    END;
END;

HOLD PG 20;
STAGE;
```

## What is currently automated

The current KSP-sOS version supports:

- Sequential scripts.
- Interactive terminal commands.
- Throttle control.
- Staging.
- SAS/autopilot attitude modes.
- Timed holds.
- RCS action-group control.
- Approximate circularization.
- Boolean flags.
- Conditional blocks.
- Telemetry-based waits and conditions.
- Abort action-group activation.
- Top-of-script module directives.
- Basic automatic staging through `using autostage;`.
- Body-specific adaptive gravity turns through `GRAVTURN <body>;`.

The following are planned extensions rather than current commands:

```text
CIRCULARISE 100KM;
TRANSFER TO MUN;
RENDEZVOUS WITH TARGET;
AUTODOCK;
SCIENCE AUTO;
LAND ON MUN;
```

Do not put those commands in a script yet; the current parser will report them as unknown commands.

## Troubleshooting

### The terminal does not appear

- Confirm the DLL is at `GameData/KSPsOS/Plugins/KSPsOS.dll`.
- Enter an actual flight scene, not the editor or space center.
- Press **F8**.
- Check `KSP.log` for plugin-loading errors.

### A command reports an error

Check that:

- The line ends with `;`.
- The command name is supported.
- `HOLD` has a valid duration.
- A telemetry condition uses a supported name and comparison operator.
- Every `IF` has a matching `END`.
- Every `using` directive appears before the first flight command.

### The program is not responding

`WHEN` and `CIRC` intentionally wait for game conditions. Press **STOP** to release script throttle control and stop the program.

## Build from source

The repository includes the source, project file, and a prebuilt drag-and-drop package. To build a fresh DLL, compile against the KSP assemblies from your local installation. KSP's own assemblies should remain in the game installation and should not be copied into `GameData`.

On macOS, for example:

```bash
KSP_MANAGED="/path/to/Kerbal Space Program/KSP.app/Contents/Resources/Data/Managed"

mkdir -p GameData/KSPsOS/Plugins

mcs -target:library \
  -out:GameData/KSPsOS/Plugins/KSPsOS.dll \
  -r:"$KSP_MANAGED/Assembly-CSharp.dll" \
  -r:"$KSP_MANAGED/UnityEngine.dll" \
  -r:"$KSP_MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$KSP_MANAGED/UnityEngine.IMGUIModule.dll" \
  -r:"$KSP_MANAGED/UnityEngine.InputLegacyModule.dll" \
  -r:"$KSP_MANAGED/UnityEngine.UI.dll" \
  src/*.cs
```

On Windows, set `KspInstall` to your KSP installation directory and build `KSPsOS.csproj` with an MSBuild-compatible .NET SDK. The project copies the output to `GameData/KSPsOS/Plugins`.

## License

KSP-sOS is released under the [MIT License](LICENSE).

## Project files

| File | Purpose |
| --- | --- |
| `src/CommandParser.cs` | Reads semicolon-terminated commands, modules, and control-flow blocks. |
| `src/FlightScriptRuntime.cs` | Executes commands, modules, and vessel telemetry logic. |
| `src/KSPsOSAddon.cs` | Creates the in-game terminal and connects flight controls. |
| `GameData/KSPsOS/KSPsOS.version` | KSP-AVC version metadata. |
| `GameData/KSPsOS/Plugins/KSPsOS.dll` | Compiled plugin used by KSP. |
