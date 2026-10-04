# Math Strikers — Unity source

The playable build lives in `games/math-strikers/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Match.unity` — generated entirely from code, not hand-placed

## How it plays

Each shot shows a math problem with a 30 second clock. Solve it, then pick the board
holding the right answer (tap or click it, or press `1`, `2`, `3`). A correct answer strikes
the ball at the matching lane of the goal; the keeper guesses a lane and occasionally
saves it. Five shots make a match, and matches run as a career against a list of
opponents. Difficulty is chosen on the start screen: Starter (+ − to 20), Easy (+ −),
Medium (+ − ×), Hard (× ÷).

## Look, Hebrew and the portal

The UI is Hebrew and follows the shared "Maccabi Arcade" design (colours in
`Scripts/Runtime/Palette.cs`): rounded 9-sliced panels, chunky gold buttons with a 3D
lower edge, pill counters, Fredoka everywhere. The sprites are drawn by
`Editor/UiSprites.cs` into `Assets/UI/Generated/` each time the scene is built.

- Unity's legacy Text can't lay out right-to-left text, so Hebrew goes through
  `Rtl.Fix` (one line) or `Rtl.Wrap` (paragraphs, pre-wrapped; no Text auto-wraps).
  The sums stay left to right.
- Every button has `PressFeedback`: it sinks to 94%, ticks through `MatchAudio`
  (so the page's mute applies) and buzzes for 10 ms.
- `PortalBridge` (with `Plugins/WebGL/PortalBridge.jslib`) shares progress with the
  site through localStorage: `MarkPlayed("math-strikers")` when a match starts, and at
  full time `AddStars(n)` and `ReportBest("math-strikers", score)` with that match's score.

Stars per match (shown as 3 slots on the full-time card, filled from the right):

| Result | Stars |
|---|---|
| Win with 4 or 5 right answers | 3 |
| Any other win | 2 |
| Draw, or a loss with at least 2 right answers | 1 |
| Loss with 0–1 right answers | 0 |

A right answer the keeper saves still counts as a right answer. The score resets each
match, so the best score is the best single match.

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
It also unit-checks `Rtl` and the star rules, and that every Text is Fredoka and
never auto-wraps, every button has press feedback, and the bridge plugin is in place.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Sound

`MatchAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle (kick-off, and two short and one long at full time), a
cheer for a goal, an "ohhh" for a miss, and applause after a win or draw. The kick is
synthesised, and each recording falls back to a synthesised stand-in if its clip is
missing. The web page's sound button mutes the game through
`SendMessage("MatchAudio", "SetMuted", "1")`.

The music is our own (CC0), in `Assets/Resources/Audio/Music/`: the stadium anthem
loops at 0.3 behind the start and full-time cards and fades out over 0.8 s at kick-off;
the supporters' drums loop at 0.22 during play (pitch 1.08 while the clock is under
10 s) and fade out at full time. A goal plays MusicGoal over a softer cheer. After the
full-time whistles a win plays MusicWin and a draw or loss MusicTryAgain, then the
anthem fades back in; each earned star fills 0.25 s apart with MusicStar. All of it
goes through the AudioListener, so the page's mute covers the music too.

The stadium clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/math-strikers/index.html`, or the next build
will overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.

