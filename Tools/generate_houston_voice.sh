#!/bin/bash
# Generates placeholder "Houston" voice lines with macOS text-to-speech.
# Run in Terminal from the project root:   bash Tools/generate_houston_voice.sh
# Then in Unity:  Lunar Survey > Apply Play-test Fixes   (assigns every <id>.wav to its comms line)
#
# Better still: record a teammate reading the lines (phone voice memo is fine), name each file
# exactly like the ids below (e.g. briefing.wav) and drop them into Assets/_Project/Audio/Comms.
# Credit the voice actor in Assets/_Project/Data/Credits.txt either way.
set -e
OUT="Assets/_Project/Audio/Comms"
VOICE="${VOICE:-Daniel}"     # try: say -v '?'  to list voices (Daniel, Alex, Samantha, Fred...)
mkdir -p "$OUT"

line() {
  local id="$1"; shift
  say -v "$VOICE" -r 175 -o "/tmp/$id.aiff" "$*"
  afconvert -f WAVE -d LEI16@22050 "/tmp/$id.aiff" "$OUT/$id.wav"
  rm -f "/tmp/$id.aiff"
  echo "  $OUT/$id.wav"
}

line briefing          "Survey One, Houston. Welcome to the surface. Read the briefing board in front of you, then press Begin Mission."
line begin             "Clock is running. A solar storm is inbound. Grab the scanner from the tool stand and find three high-titanium basalt samples."
line scanner_hint      "Point the scanner at a rock and pull the trigger. Dark rocks are basalt, but only the scanner can confirm titanium content."
line target_found      "That's a match. High-titanium basalt. Bring it back and lock it into the sample container."
line decoy_found       "Negative, that one is not on the manifest. Keep looking."
line unscanned_reject  "Container won't accept an unverified sample. Scan it first."
line decoy_reject      "That rock isn't on the manifest. We only have room for high-titanium basalt."
line sample_secured    "Sample secured. Good work."
line storm_3min        "Three minutes until the storm front arrives."
line storm_1min        "One minute, Survey One. Start heading back to the lander."
line storm_30s         "Thirty seconds! Secure what you have!"
line mission_success   "Container sealed. All three samples secured before the storm. Outstanding work, Survey One. Mission complete."
line storm_hit         "Storm front has arrived. Abort the survey and shelter in the lander. We'll try again on the next window."
line scanner_holstered "Scanner is clipped to your belt. Grab it from your left hip whenever you need it."
line boundary_warning  "Survey One, you're at the edge of the survey zone. Turn back toward the lander."
line storm_radiation   "Your dosimeter is picking up the leading edge of the solar particle event. Radiation is climbing. Keep moving."
echo "Done. Now run  Lunar Survey > Apply Play-test Fixes  in Unity."
