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

The language is intentionally compact and readable:

```text
LAUNCH;
WHEN ALTITUDE > 10000;
STAGE;
CIRC;
```

> **Project status:** KSP-sOS is an early-alpha prototype. The terminal, parser, staging, attitude control, RCS toggles, conditions, telemetry waits, gravity-turn profiles, approximate transfers, and assisted rendezvous/docking controls are implemented. Test flight programs with a recoverable save.

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

You can install KSP-sOS either from the ZIP or from the ready-to-copy folder included in the repository.

### Ready-to-copy folder

1. Open `KSP-sOS-ready-to-install`.
2. Drag its `GameData` folder into your KSP installation folder and choose **Merge** if your file manager asks.
3. Confirm that this file exists:

   ```text
   Kerbal Space Program/GameData/KSPsOS/Plugins/KSPsOS.dll
   ```

### ZIP package

1. Download `KSP-sOS-drag-and-drop.zip`.
2. Extract it directly into your KSP installation folder.
3. Confirm that the same `GameData/KSPsOS/Plugins/KSPsOS.dll` file exists:

   ```text
   Kerbal Space Program/GameData/KSPsOS/Plugins/KSPsOS.dll
   ```

4. Start KSP and load a flight.
5. Press **F8** to open the KSP-sOS terminal.

The active vessel must be controllable. KSP-sOS does not include or replace KSP's own game libraries.

## Using the in-game terminal

The terminal has one main mission-script editor. Click the editor, then type or paste a complete multi-line flight program there. Press **RUN PROGRAM** to run it from the first line, or **SAVE SCRIPT** to save it explicitly. Editing is not saved automatically.

```text
LAUNCH;
HOLD PG 10;
STAGE;
HOLD 5;
CIRC;
```

### Terminal output

This area shows status messages, parser errors, staging, circularization, module activation, and messages from `PRINT`.

Press **STOP** to halt a running program, and press **F8** again to hide the terminal.

### Script library

The bottom of the terminal has two script-library controls:

- **WRITE NEW SCRIPT** clears the program editor and starts a new saved `.kspsos` file.
- **REUSE SCRIPT** loads saved scripts from the library; press it again to cycle through older files.

Scripts are auto-saved while you edit and are stored at:

```text
GameData/KSPsOS/PluginData/Scripts/
```

The folder is created the first time KSP-sOS runs. The prebuilt ZIP does not contain personal scripts; they are created in your local KSP installation.

## Basic syntax rules

Commands outside curly-brace blocks must end with a semicolon:

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

The in-game program editor adds four spaces automatically when you press Enter after an opening `{`, and removes one indentation level when you type a closing `}` on an indented empty line. This is editor convenience only; the parser does not require indentation.

Control-flow blocks also support C#-style curly braces. Braces are optional for the older `END;` form, but recommended for new scripts:

```text
IF FUEL < 10 {
    PRINT low fuel;
    ABORT;
} ELSE {
    PRINT fuel is okay;
}

WHEN ALTITUDE > 10000 {
    STAGE;
}
```

Inside curly-brace blocks, semicolons are optional, including compact one-line blocks:

```text
IF ORBIT = SAFE { DEPLOY SOLAR PANELS }
IF ALTITUDE > 10000 { STAGE }
```

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
| `using clearskies;` | Active: removes unmanned debris on the current celestial body once it is more than 1km from the active vessel. Use only if you want automatic debris deletion. |
| `using autowarp;` | Active for transfer planning: warps to 30 seconds before a calculated transfer window. |
| `using maxscience;` | Enables stock-experiment collection plus automatic antenna transmission when available. `science` is accepted as an alias. |
| `using safeabort;` | Active: monitors a depleted active liquid-fuel stage during powered flight and triggers the abort action group. Explicit `ABORT;` is active now. |
| `using smartrcs;` | Enables RCS assistance used by rendezvous and docking. |
| `using autoland;` | Enables the assisted landing controller used by `LAND ON`. |
| `using rendezvous;` | Enables the rendezvous controller. |
| `using docking;` | Enables docking-port selection and final-approach control. |
| `using recovery;` | Enables the recovery wait; `RECOVER;` pauses until the vessel is landed or splashed. |
| `using ctrlock;` | Locks vessel controls while the script is running, while leaving camera controls available. |
| `using manualstage;` | Disables automatic staging; the script waits for explicit `STAGE;` commands. |

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

### `LAUNCH TO DOCK TO <vessel>;`

Launches, selects a named vessel as the target, automatically enters the current body's gravity-turn profile, and queues an orbital-plane correction burn when the target orbit is available before continuing with the rendezvous/docking sequence. Use `DISTANCE` and `DOCK TO TARGET` or `AUTODOCK` on following lines:

```text
LAUNCH TO DOCK TO Station Alpha;
DISTANCE 50;
AUTODOCK;
```

The target vessel must already exist in the current game and be loaded/targetable. The target inclination is reported in the terminal. Exact atmospheric launch-heading optimization is still vessel- and launch-site-dependent, so check the ascent in a test save.

### `ALIGN PLANE TO TARGET;`

Creates a normal-direction maneuver node at the next suitable ascending-node opportunity to match the selected target vessel's orbital plane:

```text
LAUNCH TO DOCK TO Station Alpha;
ALIGN PLANE TO TARGET;
EXECUTE NODE;
```

This handles orbital-plane correction after ascent. It does not replace launch-site heading optimization during atmospheric ascent.

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

### `CIRCULARISE <altitude>;`

The British spelling is accepted, and an optional altitude may be supplied in metres or kilometres. The burn stops when periapsis reaches the requested altitude:

```text
CIRCULARISE 100KM;
CIRCULARIZE 80000;
```

### `GRAVTURN <body>;`

Starts an adaptive gravity-turn ascent using a body-specific profile. The default target is an equatorial/prograde orbit; after ascent, KSP-sOS creates and executes a plane-correction node when the current orbit is not equatorial:

```text
GRAVTURN KERBIN;
GRAVTURN DUNA;
GRAVTURN MUN;
GRAVTURN MINMUS;
```

Use `GRAVTURN POLAR <body>;` to request a polar launch orbit. `GRAVTURN EQUATORIAL <body>;` is an explicit spelling of the default:

```text
GRAVTURN POLAR KERBIN;
GRAVTURN EQUATORIAL KERBIN;
```

Profiles contain tailored starting altitude, pitch-over timing, prograde hand-off speed, throttle behavior, and target apoapsis. Atmospheric bodies such as Kerbin, Duna, Eve, and Laythe use slower pitch programs; low-gravity bodies such as Mun, Minmus, Gilly, Bop, and Pol use shorter pitch-over phases and lower orbital targets.

Gravity turns can automatically circularize at a target altitude. If no altitude is supplied, the default is 80 km. The body may be omitted to use the vessel's current body:

```text
GRAVTURN KERBIN { AUTOCIRC 100KM }
GRAVTURN { AUTOCIRC }
```

During flight the controller continuously reads vessel mass, local gravity, live engine thrust, current-stage delta-v, TWR, speed, and apoapsis error. It uses those values to choose throttle, soften the pitch program for low-delta-v stages, stage only when allowed by the active modules, and taper thrust as the target apoapsis is approached. The profiles remain safe starting envelopes because no generic controller can predict every vehicle's drag, steering authority, or engine layout.

`KEEP SPEED` holds a target speed with a bounded feedback controller. It is intentionally a blocking command until `STOP` is pressed or the script is stopped:

```text
KEEP SPEED 250M/S;
KEEP SPEED SURFACE 250M/S;
KEEP SPEED ORBITAL 2200M/S;
KEEP SPEED VERTICAL 10M/S;
```

`M/S` (or `MS`) is optional. `SURFACE` is the default.

### `HOHMANN TO <body>;`

Creates an approximate Hohmann-transfer maneuver node to a celestial body:

```text
HOHMANN TO MUN;
```

For a moon or other body inside the current parent body's sphere of influence, KSP-sOS calculates a local Hohmann transfer. For an interplanetary target, it calculates an approximate heliocentric departure burn. It samples the source and target orbits to choose the closest phase-window opportunity over one synodic period, then creates a node in the normal KSP maneuver system and reports the estimated phase error.

This command currently **plans the node but does not execute the burn**. Plane changes and detailed launch-window correction are not included, so check the node, inclination, phase angle, and available delta-v in map view before burning.

### `EXEC;` and `EXECUTE NODE;`

Executes the next maneuver node in the vessel's KSP maneuver list. KSP's maneuver autopilot controls attitude while KSP-sOS estimates burn duration from the vessel's current delta-v and burn-time data, applies a bounded full-throttle burn, and removes the node when the burn completes:

```text
using autowarp;
HOHMANN TO DUNA;
EXEC;
```

This is an assisted burn controller, not a proof of exact delta-v delivery. Test it with a recoverable save and verify the node before committing.

### `TRANSFER TO <body>;` and `TRANSFER TO CB <body>;`

`TRANSFER` is the general mission-planning spelling. Both forms are accepted:

```text
TRANSFER TO MUN;
TRANSFER TO CB DUNA;
```

`CB` makes it explicit that the final word is a celestial-body name. The current implementation creates the same phase-window transfer node as `HOHMANN`; plane alignment remains manual, while `EXECUTE NODE;` can commit the generated node.

`POLAR ORBIT;` creates a plane-change node for the current body's polar plane. `POLAR ORBIT AROUND <body>;` waits for that body after an executed transfer, then creates the polar plane node. In WALK mode it executes the node automatically; outside WALK mode, follow it with `EXECUTE NODE;`. The vessel should already have a stable captured orbit around the destination:

```text
*WALK IN THE PARK*;
TRANSFER TO MUN;
POLAR ORBIT AROUND MUN;
```

When a transfer node is executed and the next command is `LAND ON <body>;`, KSP-sOS keeps the program alive while the vessel coasts through the transfer. It begins landing guidance only after KSP reports that the vessel has entered the destination body's sphere of influence, then performs a retrograde capture phase when the arrival orbit is hyperbolic before starting descent guidance. If the transfer was only planned and no node was executed, `LAND ON` stops with an actionable error instead of silently waiting forever.

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

### `SCIENCE AUTO;`, `SCIENCE COLLECT;`, `SCIENCE TRANSMIT;`, and `DO SCIENCE;`

`SCIENCE AUTO;` deploys available stock science experiments on the active vessel, waits for data generation, and collects the resulting data. With `using maxscience;`, it also starts every usable stock antenna transmitter after collection. `SCIENCE COLLECT;` only collects experiment data that already exists, while `SCIENCE TRANSMIT;` starts antenna transmission for data already stored on the vessel. `DO SCIENCE;` is an alias for `SCIENCE AUTO;`.

```text
using maxscience;
SCIENCE AUTO;
```

The current routine handles the vessel's present situation. It does not yet plan an entire multi-biome science route, transmit science automatically, or reset every reusable experiment for a future situation.

### Safe-orbit checks and solar panels

`ORBIT = SAFE` is a built-in safety condition. It becomes true only when the vessel has a stable elliptical orbit, its periapsis and apoapsis clear the body's surface and atmosphere, and the vessel is not landed or splashed. `DEPLOY SOLAR PANELS;` commands every stock deployable solar panel on the active vessel to extend. `DEPLOY SOLAR;` is accepted as a short form:

```text
IF ORBIT = SAFE {
    DEPLOY SOLAR PANELS;
}
```

`ORBIT = UNSAFE` and `ORBIT != SAFE` are also supported. The condition is a conservative flight-safety gate; it does not guarantee sunlight, communication, battery margin, or collision clearance.

## Rendezvous, docking, landing, and mission helpers

These commands are designed to make longer scripts readable:

```text
DISTANCE 50;
RENDEZVOUS WITH TARGET;
DOCK TO TARGET;
AUTODOCK;
APOLLO DOCK;
APOLLO DOCK TO TARGET;
LAND ON THE MUN;
RECOVER;
```

`DISTANCE` sets the approach distance in metres. `RENDEZVOUS WITH TARGET;` uses the currently selected target, or a vessel name may be supplied. `DOCK TO TARGET;` requires a docking port on the target vessel to be selected in KSP. `AUTODOCK;` searches for the nearest compatible docking-port pair. The controller enables RCS, aligns the ports, limits closing speed, changes the active vessel reference to the selected docking port, and calls KSP's docking API when capture conditions are met.

`APOLLO DOCK TO TARGET;` uses the selected target vessel, or `APOLLO DOCK;` searches for the nearest dockable vessel and then performs the transposition approach with automatic RCS, port alignment, rotation, and capture. If no external target is available, it falls back to two compatible separated ports on the active vessel and uses KSP's same-vessel docking path. It does not teleport parts or create a docking port that the craft does not have.

`LAND ON BODY;` performs a retrograde deorbit burn at periapsis when needed, then provides an assisted descent controller. It still expects a lander with suitable thrust, fuel, control authority, and a survivable trajectory; terrain targeting and vehicle-specific tuning are not guaranteed. `RECOVER;` waits until the vessel is landed or splashed before the script continues.

`*WALK IN THE PARK*;` is a readable high-level mission marker. It enables the script's mission-assist profile and can be placed before commands such as `GRAVTURN`, `CIRCULARISE`, `TRANSFER`, and `LAND`.

In WALK mode, transfer commands automatically execute the generated maneuver node before the next script command runs. Outside WALK mode, use `EXECUTE NODE;` when you want the script to commit a planned node explicitly. For example:

```text
*WALK IN THE PARK*;
GRAVTURN KERBIN;
CIRCULARISE 100KM;
TRANSFER TO MUN;
LAND ON THE MUN;
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

The same block can be written with braces:

```text
IF LANDING_MODE {
    THROTTLE 20;
}
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

`WHEN` also supports a block:

```text
WHEN APOAPSIS > 80000 {
    THROTTLE 0;
    PRINT coast to apoapsis;
}
```

### Syntax colors and diagnostics

The terminal includes a syntax preview beneath the program editor. Logic is purple, flight/orbit commands are yellow, vessel commands are blue, science is cyan, mission/navigation commands are green, numbers are pale green, comments are muted green, and unknown command words are bright red. Parser errors appear below the preview with their line number, so misspellings are visible before you run the program. Right-click the terminal to toggle compact mode; clicking outside it does not hide it and leaves the camera available.

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
- Opt-in debris cleanup through `using clearskies;`.
- Opt-in transfer-window warping through `using autowarp;`.
- Opt-in active-stage safety aborts through `using safeabort;`.
- Stock-experiment execution, collection, and optional antenna transmission through `SCIENCE AUTO;` with `using maxscience;`.
- Safe-orbit telemetry conditions and stock solar-panel deployment through `ORBIT = SAFE` and `DEPLOY SOLAR PANELS;`.
- Body-specific gravity-turn presets through `GRAVTURN <body>;`.
- Live gravity-turn adaptation using vessel mass, thrust-to-weight ratio, active-stage delta-v, apoapsis error, and speed feedback.
- Target-speed feedback control through `KEEP SPEED`.
- Equatorial-by-default ascent and opt-in polar ascent/arrival through `GRAVTURN POLAR <body>;` and `POLAR ORBIT AROUND <body>;`.
- Approximate local and interplanetary transfer-node planning through `HOHMANN TO <body>;` and `TRANSFER TO <body>;`.
- Long-mission transfer continuation: after an executed transfer, `LAND ON <body>;` waits for destination sphere-of-influence entry before starting descent guidance.
- Target-altitude circularization through `CIRCULARISE 100KM;`.
- Target-vessel selection and assisted rendezvous through `LAUNCH TO DOCK TO`, `DISTANCE`, and `RENDEZVOUS`.
- Orbital-plane correction nodes through `ALIGN PLANE TO TARGET;`.
- Compatible-port selection and final-approach control through `DOCK TO TARGET`, `AUTODOCK`, and `APOLLO DOCK`.
- Assisted descent and landed-state recovery checks through `LAND ON` and `RECOVER`.
- Mission-assist profile activation through `*WALK IN THE PARK*;`.
- Optional manual staging through `using manualstage;`, which disables both automatic module staging and gravity-turn low-delta-v staging.
- Optional vessel-control protection through `using ctrlock;`, which locks vessel controls while the script runs but leaves the camera and pause controls available.

Transfer nodes remain approximate. `EXECUTE NODE;` can commit the next node, but verify phase angle, inclination, node direction, and available delta-v in map view before committing to a long interplanetary burn.

The scripting engine is still an early alpha. A successful build and parser test do not establish that every flight controller can complete a full mission in KSP. In particular, long-range rendezvous, transfer targeting, docking, and powered landing need in-game flight validation and further guidance work before they can be relied on unattended.

## Troubleshooting

### The terminal does not appear

- Confirm the DLL is at `GameData/KSPsOS/Plugins/KSPsOS.dll`.
- Enter an actual flight scene, not the editor or space center.
- Press **F8**.
- Check `KSP.log` for plugin-loading errors.

### I cannot type in the terminal

- Click inside the **Terminal input** line, then type `PRINT hello;` and press Enter. The typed characters should appear beside `>`.
- Make sure the terminal is open with **F8** and that the game is paused only if your KSP setup still delivers GUI input while paused.
- If the line still does not receive keys, press **F8** to close and reopen the terminal, then click the line again.
- Check `KSP.log` for `[KSP-sOS] Flight terminal loaded`. If it is absent, the DLL is not loading; if an error follows it, include the error when reporting the issue.

### A command reports an error

Check that:

- The line ends with `;`.
- The command name is supported.
- `HOLD` has a valid duration.
- A telemetry condition uses a supported name and comparison operator.
- Every `IF`, `ELSE`, and `WHEN` block has a matching `}`; legacy `IF` blocks use `END;`.
- Every `using` directive appears before the first flight command.

### The program is not responding

`WHEN` and `CIRC` intentionally wait for game conditions. Press **STOP** to release script throttle control and stop the program.

### Mission templates

The program editor includes starter templates for launch-to-orbit, transfer-and-land, and rendezvous/docking missions. Select a template, replace placeholder body names, and review the syntax preview before running it. **CLEAR PROGRAM** starts a blank saved script.

## Build from source

The repository includes the source, project file, a prebuilt drag-and-drop package, and a ready-to-copy installation folder. The distributed plugin is a managed DLL built for KSP 1.12.5 and is portable across Windows, macOS, and Linux. KSP's own assemblies should remain in the game installation and should not be copied into `GameData`.

### Validation status

- The source compiles against the installed KSP 1.12.5 assemblies.
- Parser smoke tests cover semicolon enforcement, braces, distance units, science modes, mission commands, and malformed-argument diagnostics.
- The drag-and-drop archive contains only `GameData/KSPsOS` and passes ZIP integrity testing.
- A complete live KSP flight has not been automated in this repository, so ascent, transfer, landing, science-transmission, and docking controllers remain early-alpha and should be tested in a throwaway save.

Run the parser smoke test with Mono:

```bash
mcs -out:/tmp/kspsos-parser-smoke.exe src/CommandParser.cs tests/ParserSmoke.cs
mono /tmp/kspsos-parser-smoke.exe
```

The simplest build method on macOS or Linux is:

```bash
./build.sh
```

The script finds a standard Steam installation automatically. If KSP is installed elsewhere, set `KSP_MANAGED` to KSP 1.12.5's `KSP_x64_Data/Managed` directory first. The script uses the local KSP assemblies only as compile-time references and writes the portable plugin to `GameData/KSPsOS/Plugins/KSPsOS.dll`.

On macOS, the equivalent manual command is:

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

On Windows, set `KSP_MANAGED` or `KspInstall` to your KSP 1.12.5 installation and build with an MSBuild-compatible .NET SDK or Mono. The output belongs at `GameData/KSPsOS/Plugins/KSPsOS.dll`. Distribute only the `GameData/KSPsOS` folder; do not distribute KSP's own assemblies.

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
| `tests/ParserSmoke.cs` | Parser and diagnostic smoke test used during development. |
