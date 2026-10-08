# Play-test fixes

All fixes are already applied to `LunarSurface.unity` and saved. To re-apply after pulling on another machine, or after re-running the builder, use **Lunar Survey ▸ Apply Play-test Fixes**. It is safe to run more than once and keeps your hand edits. The builder also runs it automatically at the end of every step.

## Scanner

| Issue | Cause | Fix |
|---|---|---|
| Screen hard to read | The screen was 9 × 5 cm, fixed at 55°, and **half of it was inside the scanner body**. The Simulator holds the controller at eye height, nose pitched 43° down, so the screen faced the sky and was seen edge-on. The controller model also covered it. | 13 × 8.5 cm display on a hinge that **tilts toward your eyes** (`ScannerScreenTilt`). The controller model and the tutorial callouts are hidden while the scanner is held. Every reading is also mirrored on a new **SCAN** line on the wrist display. |
| Drops when the left mouse is released; lost once dropped | XRI default is "hold to grab" | Without a headset, grab is **click to pick up, click again to drop** (`GrabStyleController`; a headset still uses hold-grip). After the first pickup, letting go **clips the scanner to your left hip** (`ToolHolster`). Look down and click it to take it back. |
| Clips through boxes and the floor | Movement type was *Instantaneous*, which ignores collisions while held | Changed to **Velocity Tracking**, so the scanner is a real physics body and stops at surfaces. It ignores the player's own body capsule. |

## Stones
Grabbing a rock with the hand that holds the scanner now sends the scanner to the belt instead of onto the floor. In VR you can also hold the scanner in one hand and rocks in the other.

## Storm: what it is
It is a **solar particle event**: a burst of high-energy protons from a solar flare or coronal mass ejection (CME). The Moon has no air, so there is **no wind, no dust storm and no rumble**. The real danger is radiation, which is why Houston sends you back to the lander. The effects (`StormEffects`) build up over the last 150 s:
- **Dosimeter (Geiger) clicks**: from a slow background tick to a rattle
- **Radio static**: solar radio bursts really do interfere with communications. This replaced the "rumble".
- **Cosmic-ray light flashes**: brief white streaks in the eyes, which Apollo crews reported
- **Levitating dust**: fine regolith hopping in slow arcs near the ground. Electrostatic charging of the surface is a real process, observed as "horizon glow" by Surveyor and Apollo 17. Here it is exaggerated, not invented.
- **Gold visor tint** at the edges of the view and a brighter Sun. The orange sun tint was toned down because sunlight doesn't turn orange in a vacuum.
- New Houston line when the dosimeter first picks it up. The effects fade out if you finish in time.

Demo shortcut: in the Inspector, right-click **StormTimer ▸ Debug: skip to 0:45 remaining**.

## Environment
| Issue | Fix |
|---|---|
| Sun not visible | **Sun disc** with limb darkening and glare, always where the Directional Light comes from (`CelestialBody`). It sits behind you at the start, so turn around. |
| Realistic Sun and Earth | Earth now uses **NASA Blue Marble** imagery plus a generated cloud layer, and is **lit by the Sun**, so it shows a real phase (currently gibbous). Both stay "at infinity" (no parallax). A 3D Sun model isn't needed: from the Moon the Sun is a sharp, blinding disc. |
| Falling off the edge | A **ring of 48 invisible walls** (32 m radius) blocks the player, thrown rocks and the teleport arc. Orange **survey stakes** show where the edge is, and Houston warns you as you approach. |
| Walking through bumps and craters | The terrain already had a collider. The real cause: **Simulator WASD moves your body inside the room** (tracking space), just like physically walking in a headset, so it never goes through the CharacterController. `PlayerGrounding` now lifts the player onto the terrain under the head and clamps them inside the zone. This works for room-scale walking, the thumbstick and teleport. |

## Audio
**Before:** no sound effects or voice lines at all. `Assets/_Project/Audio` was empty and every clip slot was a placeholder; Houston was subtitles only.
**Now:** 13 synthesised clips (`Tools/generate_sfx.py`, original work) are assigned: scan beep, match chime, reject buzz, sample lock, lid servo and latch, Apollo **Quindar tone** before each Houston message, lander radio beacon, radio static, dosimeter clicks, three footsteps and the suit fan. Every sound can be justified as heard *inside* the helmet.
**Houston voice:** run `bash Tools/generate_houston_voice.sh` in the Mac Terminal (macOS text-to-speech), or better, record a teammate and save the files as `Assets/_Project/Audio/Comms/<id>.wav`. Then run the fix menu, which assigns them automatically.

## Lunar movement
Yes, it now simulates hopping, but only in the opt-in **Lunar** mode. `LunarGait` moves the view along a **real ballistic arc for 1.62 m/s²**: a 6 cm hop gives about 0.54 s of airtime. There's a suit-borne thud on each landing. It is off in Comfort/teleport mode and never applies to physical walking. Lower **LunarGait ▸ Intensity** if anyone feels unwell; up-and-down camera motion is a known cause of motion sickness.

## Other changes
- URP shadow distance went from 2.5 m to 40 m (on *Performance URP Config*), so rocks and the lander cast long lunar shadows.
- README controls, the script table and the testing checklist are updated. Credits list the Earth texture and the synthesised audio.

## Recommended next steps
1. Turn off the template's tutorial callouts ("Grab / Turn / Blink") on both controllers for the demo: disable *Affordance Callouts Left/Right*.
2. Record real Houston voice lines. A human voice adds a lot of presence.
3. Test on a headset if the panel provides one. Check the holster height (`ToolHolster ▸ Offset From Head`) and the hop intensity.
4. Optional: tick *Mipmap* and set Aniso 4 on the Regolith texture for a sharper ground at grazing angles.
5. Commit to Git now. `Assets/_Project/Generated` and `Audio` are new folders.

## Round 2
- **Credits board overflow:** I removed *Software & Toolkits* and *AI Assistance* from `Credits.txt` (the board), but kept them in `CREDITS.md` for the slides, because the brief requires AI disclosure there. The board text now also shrinks to fit if the list grows. Houston is credited as macOS text-to-speech.
- **Walking through the lander, container and tool stand:** these props always had colliders, but only thumbstick and teleport movement goes through the CharacterController. Room-scale / Simulator WASD movement moves the camera directly, so nothing blocked it. `PlayerGrounding` now checks a 0.15 m body capsule under the head against static colliders and slides the player back out sideways. I tested this inside the container, the table and deep inside the lander. The radius is small so you can still lean over the container to place samples (**Body Radius** in the Inspector).
- **Earth texture source:** `three-globe` example image `earth-blue-marble.jpg` (github.com/vasturiano/three-globe), derived from NASA's Blue Marble. The official NASA collection is at science.nasa.gov/earth/earth-observatory/collections/blue-marble/.
