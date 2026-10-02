# Math Strikers — Unity source

The playable build lives in `games/math-strikers/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Match.unity` — generated entirely from code, not hand-placed

## How it plays

Each shot shows a math problem with a 30 second clock. Solve it, then pick the board
holding the right answer (click it, or press `1`, `2`, `3`). A correct answer strikes
the ball at the matching lane of the goal; the keeper guesses a lane and occasionally
saves it. Five shots make a match, and matches run as a career against a list of
opponents. Difficulty is chosen on the start screen: Easy (+ −), Medium (+ − ×),
Hard (× ÷).

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod MathStrikers.EditorTools.SceneBuilder.BuildScene -quit

# Check the problem generator and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod MathStrikers.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod MathStrikers.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/math-strikers
```

`VerifyScene` re-computes 1200 generated problems to confirm each stated answer is
correct, and checks that the ball, keeper, HUD, and three answer boards are all wired up.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Sound

`MatchAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle (kick-off, and two short and one long at full time), a
cheer for a goal, an "ohhh" for a miss, and applause after a win or draw. The kick is
synthesised, and each recording falls back to a synthesised stand-in if its clip is
missing. The web page's sound button mutes the game through
`SendMessage("MatchAudio", "SetMuted", "1")`.

The clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/math-strikers/index.html`, or the next build
will overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.

