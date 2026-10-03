# Juggling — Unity source

The playable build lives in `games/juggling/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Juggling.unity` — generated entirely from code, not hand-placed

## How it plays

Keepy-uppy. Press Kick Off and the ball drops from the top of the screen; tap it (or
click it, or press `Space`) to kick it back up. Where the tap lands across the ball
decides where it goes: tap its left side and it drifts right, its right side and it
drifts left, so you have to follow it. The ball bounces off the sides of the play
area and the round ends when it touches the grass.

Every touch scores a point. A touch near the middle of the ball is "clean" and builds
the combo; from five clean touches in a row each touch is worth two. At 10, 25, 50
(and 75, 100, …) points the crowd cheers and the Maccabi Netanya player beside the
pitch jumps for joy. The best score for each level is kept between visits.

The keyboard has no "where on the ball", so `Space` (or `↑` / `W`) only kicks a ball
that is on its way down into the bottom of the screen, and steers it gently back
toward the middle.

Levels are chosen on the start screen. Each one has a smaller ball, a smaller tap
area, more gravity and more sideways drift than the one before; gravity also ramps up
over a round, so every round starts slow:

| Level   | Ball  | Tap area   | Gravity (start → end) | Drift |
|---------|-------|------------|-----------------------|-------|
| Starter | 0.50  | 1.8 × ball | 3.2 → 6.0             | 0.5   |
| Easy    | 0.43  | 1.65 ×     | 4.0 → 7.5             | 0.9   |
| Medium  | 0.36  | 1.5 ×      | 5.0 → 9.5             | 1.4   |
| Hard    | 0.30  | 1.35 ×     | 6.5 → 10.5            | 2.0   |

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod Juggling.EditorTools.SceneBuilder.BuildScene -quit

# Check the levels, the ball's flight and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Juggling.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod Juggling.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/juggling
```

`VerifyScene` checks that each level is harder than the last and still leaves at
least three quarters of a second for a falling ball; flies 3600 kicks, struck anywhere
across the ball, through every level and a range of screen widths to prove the ball
never leaves the play area or rises under the HUD; checks the scoring rules; checks
that the ball, player, HUD, level buttons, camera and audio are all wired up; and
checks that the WebGL template still sends the mute setting to `JuggleAudio`.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Layout

`CameraFramer` looks square-on at the plane the ball moves in and sets its height and
zoom so the grass line sits 16% up the screen and the top of the ball's flight sits
just under the HUD's score bar, whatever shape the window is. The HUD is laid out on a
720 × 720 square scaled by the screen's short side, so a phone held upright gets a
full-size HUD. The page's back and sound buttons sit in the strip of grass below the
ball's play area, where nothing ever needs tapping (`placeCorner()` in the template).

In a wide window the player stands beside the play area; in a phone held upright there
is no room, so he stands behind it.

## Sound

`JuggleAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle at kick-off, a cheer at each milestone, an "ohhh" when the
ball drops, and applause for a new best. The touch "boop" is synthesised, and climbs in
pitch as the combo builds. Each recording falls back to a synthesised stand-in if its
clip is missing. The web page's sound button mutes the game through
`SendMessage("JuggleAudio", "SetMuted", "1")`.

The clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/juggling/index.html`, or the next build will
overwrite the change.

## Test hook

`Assets/Plugins/WebGL/JugglingBridge.jslib` publishes the ball's position each frame
as `window.__juggling` (`x`, `y`, tap radius `r` in CSS pixels from the canvas's
top-left, plus `state`, `score` and `combo`), so automated browser checks can find the
ball and tap it.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
