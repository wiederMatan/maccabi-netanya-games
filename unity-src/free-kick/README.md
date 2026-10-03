# Free Kick — Unity source

The playable build lives in `games/free-kick/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/FreeKick.unity` — generated entirely from code, not hand-placed

## How it plays

A free kick from just outside the box, with a wall of defenders between the ball and
the goal and a keeper on the line. Drag from the ball toward the goal and let go: the
direction aims, the length of the drag is the power (a longer drag kicks it higher and
harder), and a curve in the swipe bends the ball, so it can be swung round the wall. A
dotted arc shows where the ball will go while dragging. On a desktop the mouse drags
the same way, or the arrow keys aim and set the power, `A`/`D` bend it and `Space`
shoots.

A goal is worth 10 points, and the gold rings in the top corners add 20 more. A round
is five kicks; the best score is remembered per level. Levels are chosen on the start
screen:

| Level   | Wall              | Keeper   | Rings  | Help                                    |
|---------|-------------------|----------|--------|-----------------------------------------|
| Starter | 2 small players   | slow     | big    | every aim is pulled onto the goal       |
| Easy    | 3, a little short | okay     | large  | wild aims are pulled most of the way in |
| Medium  | 4, jumping        | good     | medium | a little pull; the arc shows 3/4 of it  |
| Hard    | 5, jumping higher | sharp    | small  | none; the arc shows half, and wind      |

The taker (a random Maccabi Netanya outfield player) runs in with the Run clip and the
ball only leaves the spot when Anim_Kick reaches the moment the boot meets it (50% of
the clip), the same as Math Strikers.

## How a shot is decided

`ShotPath` is a scripted flight rather than a physics simulation: a ballistic arc from
the spot to a point on the goal line, bowed sideways by the bend and pushed by the wind.
That is what lets the aim guide show exactly where the ball will go, and lets
`ShotJudge` decide the outcome — wall, woodwork, keeper, goal, over or wide — the
moment it is struck. The ball flies the path to that moment and is then handed to
physics so rebounds and the net look natural. The keeper dives so he is at full stretch
as the ball arrives: all the way on a save, short or the wrong way when beaten. The
dive clip only goes one way, so the keeper's animator mirrors it for the other side.

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Re-import the character and rebuild its animator controllers (only after changing them)
"$UNITY" -batchmode -nographics -projectPath . -executeMethod FreeKick.EditorTools.CharacterImporter.Setup

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod FreeKick.EditorTools.SceneBuilder.BuildScene -quit

# Check the shot model and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod FreeKick.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod FreeKick.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/free-kick
```

`VerifyScene` checks the shot model for every level: the path starts on the ball and
ends on its target, a low shot into the wall is blocked, some swipe can still score
from the hardest spot the level sets, the keeper can save but is not unbeatable, the
rings sit inside the frame and count when hit, the aim assist keeps a wild swipe on
target on Starter, and each level is harder than the one before. It then checks that
the ball, wall, keeper, taker, aim guide, rings, camera, HUD and audio are all wired up,
and that the web template still sends the mute setting.

The game logs each kick to the browser console (`[FreeKick] aim …`, `shot …`,
`result …`), with the ball and goal positions on screen, so automated browser tests
can aim real swipes and read back the outcome.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Sound

`MatchAudio` is the same as Math Strikers': real stadium recordings from
`Assets/Resources/Audio/` (a looping crowd, the referee's whistle, a cheer for a goal,
an "ohhh" for a miss, applause after a good round) and a synthesised kick, which is
also pitched down for a ball off the wall or the keeper and up for one off the post. The
web page's sound button mutes the game through
`SendMessage("MatchAudio", "SetMuted", "1")`.

The clips are all CC0 or public domain from Wikimedia Commons; sources and authors are
in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/free-kick/index.html`, or the next build will
overwrite the change. Its `placeCorner()` sits the back and sound buttons just above
the score bar, so it has to match `HudController.BarBottom` and `BarHeight`.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
