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
