# The Lunar Gravity Survey

INTE 42312 Virtual & Augmented Reality group project. This is a VR mission on the Moon: scan rocks, collect three high-titanium basalt samples and lock them into the sample container before a solar storm arrives.

- **Unity:** 6000.6.4f1 (everyone must use exactly this version)
- **Toolchain:** URP · OpenXR · XR Interaction Toolkit 3.6.1 · Input System
- **Main scene:** `Assets/_Project/Scenes/LunarSurface.unity`

---

## Quick start (fresh clone)

1. Open the project in Unity Hub with **6000.6.4f1**. The first import takes a few minutes.
2. Open `Assets/_Project/Scenes/LunarSurface.unity`.
3. Run **Lunar Survey ▸ Build Everything** from the top menu bar.
4. Press **Ctrl+S / Cmd+S**, then **Play**. The XR Interaction Simulator handles input in the Editor.

> The builder deletes and rebuilds `[Environment]`, `[Lander]` and `[Mission]` (an existing Terrain is kept). Once you start hand-editing those objects, **stop re-running that step**, or your edits will be lost.

---

## How the experience flows

| Stage | What happens | Code |
|---|---|---|
| **Start** | The player stands facing the briefing board. Houston's radio explains the task. The player presses **Begin Mission**. | `MissionManager.BeginMission` |
| **Middle** | The storm clock runs. The player grabs the Geo-Scanner, scans rocks, carries the confirmed high-Ti basalt back and seats it in a socket. | `GeoScanner`, `RockSample`, `SampleSocketFilter`, `SampleContainer`, `StormTimer` |
| **End A: success** | The third sample locks in, the lid closes, Houston confirms, and the outcome board appears with stats, a rating, and Restart/Quit buttons. | `MissionManager.Complete`, `MissionHUD` |
| **End B: storm** | The timer reaches zero, Houston aborts the mission, and the outcome board appears. | `MissionManager.OnStormExpired` |

The targets are reshuffled on every run (`MissionManager.randomiseTargets`). High-Ti and low-Ti basalt look almost identical, so the player has to use the scanner to tell them apart.

---

## Scripts (`Assets/_Project/Scripts`)

| Script | Role |
|---|---|
| `Core/LunarGravity` | Sets `Physics.gravity` to 1.62 m/s². This affects rocks and the scanner, and also the player's fall speed, because the XRI Gravity Provider reads `Physics.gravity`. |
| `Core/MissionManager` | State machine (Briefing → InProgress → Success/Failed). Assigns minerals and reacts to events from other components. |
| `Core/StormTimer` | Countdown, radio warnings at 3:00, 1:00 and 0:30. Exposes `Intensity` (0→1 over the last 150 s) and fades in the radio-static loop. |
| `Core/StormEffects` | The **solar particle event** (radiation, not a dust storm): dosimeter clicks, cosmic-ray light flashes, electrostatically levitated dust, gold visor tint, brighter Sun. |
| `Core/MissionComms` | "Houston" in-helmet radio: queued lines with subtitles. Works with or without recorded audio. |
| `Samples/RockSample` | Hidden mineral identity, TiO₂ reading, colour, and the glow once confirmed. |
| `Samples/SampleSocketFilter` | XRI hover/select filter: a socket accepts only **scanned high-Ti basalt**. |
| `Samples/SampleContainer` | Three `XRSocketInteractor` slots, slot lights, and the hinged lid. |
| `Tools/ToolHolster` | Belt holster on the player (left hip). Once the scanner is taken from the stand, letting go clips it back onto the belt. |
| `Tools/ScannerScreenTilt` | Hinged scanner display that tilts toward the eyes so the readout is always readable. |
| `Tools/GeoScanner` | **Advanced feature.** Fires on the grab interactable's *Activated* event (named "Activate" action), raycasts, shows the reading on its own screen, beeps, and sends haptics. |
| `Locomotion/PlayerGrounding` | Keeps the player on the terrain (also for physical / Simulator WASD walking) and inside the 31.5 m survey zone; Houston warns near the edge. |
| `Locomotion/LunarGait` | Apollo-style hop gait (real ballistic arc at 1.62 m/s²) + suit footsteps, Lunar mode only. |
| `Desktop/GrabStyleController` | Headset: hold grip. Simulator / keyboard & mouse: click to pick up, click again to drop. |
| `Environment/CelestialBody` | Keeps the Sun disc (locked to the light) and the Earth "at infinity". |
| `UI/MissionHUD` | Briefing, status board, wrist display, and an outcome board placed in front of the player but **world-locked**. |
| `UI/CreditsBoard` | In-game credits loaded from `Assets/_Project/Data/Credits.txt`. |
| `Locomotion/LocomotionModeController` | **Comfort** (default: teleport + snap turn) vs **Lunar** (opt-in: continuous move + low-gravity jump). The tunneling vignette stays on in both. |
| `Desktop/DesktopFallbackRig` | Keyboard and mouse for the exported build when no headset is present. Adds bindings to the **same** XRI actions. |
| `Editor/LunarPlaytestFixes` | Menu **Lunar Survey ▸ Apply Play-test Fixes**: non-destructive, idempotent upgrade of an existing scene (see `PLAYTEST_FIXES.md`). Also runs at the end of every builder step. |
| `Editor/LunarSceneBuilder` | Menu **Lunar Survey ▸ …**: generates the terrain, sky, rocks, container, scanner and all panels, and wires everything together. |

---

## Controls

| Action | VR controller | Simulator (Editor) | Keyboard & mouse (exported build) |
|---|---|---|---|
| Look | Head | Mouse (see simulator help panel) | Mouse |
| Teleport | Right stick forward, release | Simulator stick keys | Hold **T**, release |
| Turn | Right stick left/right (snap) | Simulator stick keys | Mouse, or **Q/E** snap |
| Walk (Lunar mode / desktop) | Left stick | Select left controller (**[** or **Tab**), then **I J K L** = thumbstick. (WASD moves your *body in the room*, like real walking) | **WASD** |
| Jump (Lunar mode) | **A** button | Simulator | **Space** |
| Grab / hold | Grip (hold) | **Left mouse** or **G**: click to grab, click again to drop | **Left mouse**: click to grab, click again to drop |
| Scan (use held tool) | Trigger | Simulator trigger | **Right mouse** |
| Press UI button | Trigger at a panel | Simulator trigger | **Left mouse** at a panel |
| Free cursor | – | – | **Esc** (click to recapture) |

---

## Design decisions to defend (cheat sheet)

- **Tracking origin: floor-referenced.** It's a standing experience. Players reach down to pick up rocks and to the waist-height container, so the virtual floor must match the real one.
- **Locomotion:** teleport + snap turn by default (no vection, so less cybersickness). Lunar mode is opt-in, with a slow speed and a tunneling vignette.
- **UI:** every panel is world-space, placed about 1–2 m away (comfortable vergence/accommodation). The outcome board appears in front of the player but stays fixed in the world rather than following the head.
- **Audio:** Houston is 2D on purpose, because it's an in-helmet radio. Everything physical is a 3D spatial source: the scanner, the container, and the lander's radio beacon, which helps players find their way back.
- **Physics:** 1/6 g makes dropped and thrown rocks fall noticeably slower. Rocks use velocity tracking so they collide while held (no clipping). Regolith friction keeps them from bouncing.
- **VR over AR (D.I.C.E.):** training on the Moon is **I**mpossible and **E**xpensive. AR can't replace the black sky, the Earth on the horizon, low gravity or the horizon itself.

---

## Testing checklist (non-builder runs this)

- [ ] Briefing is readable and Begin works with the Simulator
- [ ] Scanner: grab, aim, trigger → reading appears and a target glows
- [ ] An unscanned target **and** a decoy are both refused by the container (Houston explains why)
- [ ] Three scanned targets → lid closes → success board → Restart works
- [ ] Let the timer run out → failure board
- [ ] Lunar mode: walking and jumping work, and the vignette shows
- [ ] Nothing falls through the ground or floats; thrown rocks arc slowly
- [ ] Let go of the scanner → it clips to the left hip; grab it again from there
- [ ] Walk toward the orange stakes → Houston warns, you can't pass them; walking up a crater rim doesn't sink you into the ground
- [ ] Storm (right-click StormTimer ▸ *Debug: skip to 0:45*): dosimeter clicks speed up, static, flashes, dust, gold visor edge
- [ ] **Exported Windows build** launches without a headset; WASD, mouse, T, LMB and RMB all work; the mission can be completed

---

## Building the Windows .exe

1. **File ▸ Build Profiles** → Windows. Check that `LunarSurface` is the first enabled scene (the builder sets this).
2. On macOS you need **Windows Build Support (Mono)**, added in Unity Hub ▸ Installs ▸ gear ▸ Add modules.
3. Build to a `Builds/` folder (it is git-ignored) and zip it for submission.

---

## Credits

See `CREDITS.md`. `Assets/_Project/Data/Credits.txt` (shown inside the experience) is the same list minus *Software and toolkits* and *AI assistance*, which only go in `CREDITS.md` and the slides.
