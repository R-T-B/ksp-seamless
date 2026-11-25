# Seamless

A Kerbal Space Program mod that replaces loading screens with camera moves.

When you change scene, the camera flies to where you are going. A title card holds the last
frame while the game loads, and the new scene opens on the same view and zooms in. The new
scene loads in the background during the camera move.

## What it covers

- Switching to another craft, from flight, the Space Center or the Tracking Station
- Flight to the Space Center or the Tracking Station, and back
- Space Center to the VAB / SPH and back
- Space Center to the Tracking Station and back
- Launching from the VAB / SPH, including Kerbal Construction Time launches
- Loading a save from the main menu
- An arrival shot: the first time a craft is captured by a body, a letterboxed shot of that body
  with its name and orbit

## Requirements

- KSP 1.12.x, or KSP Retooled 1.13 (the same download; Retooled relinks the older Unity calls)
- [Harmony](https://github.com/KSPModdingLibs/HarmonyKSP) (`000_Harmony`). Without it only the
  arrival shot works.

Works with EVE, Scatterer, Kopernicus, Parallax and KSPTextureLoader.

## Install

Download `Seamless_<version>.zip` from the
[releases](https://github.com/S4mpsa/ksp-seamless/releases) and unzip it into your KSP folder, so
you end up with `GameData/Seamless`.

## Settings

`GameData/Seamless/settings.cfg` has the full list, with comments. The ones you are most likely
to want:

- Each transition has its own switch, such as `vesselSwitchTransition` and `launchTransition`.
- `preloadScene = True` loads the next scene during the camera move. Set it to `False` if the game
  runs out of memory on scene changes.
- `departMaxSeconds`, `flightMaxRate` and `flightMaxAccel` control the camera moves' length and
  speed.
- `skipKey` (Space; Escape and a left click work too) ends a shot or transition early.
  `previewKey` (keypad *) plays the arrival shot for the body you are at.

## Building

`python tools/build.py` compiles `src/` against your KSP install (found through `KSPDIR` or the
Steam libraries, or pass `--ksp <folder>`). It writes `GameData/Seamless/Plugins/Seamless.dll`.
Build release DLLs against KSP 1.12.5; that one DLL also runs on Retooled.

## License

GPL v3, see [LICENSE.txt](GameData/Seamless/LICENSE.txt). The bundled Orbitron font is under the
SIL Open Font License 1.1.
