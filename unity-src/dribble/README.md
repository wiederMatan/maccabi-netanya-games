# Dribble — Unity source

The playable build lives in `games/dribble/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Run.unity` — generated entirely from code, not hand-placed

## How it plays

An endless run down the pitch. A Maccabi Netanya player dribbles the ball forward in
one of three lanes; defenders in red and white, training cones and gold stars come
toward him. Swipe left or right, tap the left or right half of the screen, drag or
click with a mouse, or press the arrow keys / `A` `D` to change lane. Stars are worth
10 points each and every metre is a point. Knocking over a cone only costs some speed;
running into a defender ends the run — he goes down, the ball rolls loose and the
crowd groans. The run speeds up as it goes.

Difficulty is chosen on the start screen, and the best score is kept per level:

| Level   | Start speed | Top speed | Seconds between rows | Rows blocking two lanes |
|---------|-------------|-----------|----------------------|-------------------------|
| Starter | 4.5 m/s     | 7.5 m/s   | 2.5                  | never                   |
| Easy    | 5.5 m/s     | 9.5 m/s   | 2.1                  | 15%                     |
| Medium  | 7 m/s       | 12.5 m/s  | 1.75                 | 35%                     |
| Hard    | 8.5 m/s     | 15.5 m/s  | 1.45                 | 55%                     |

A lane is always left open, so every row can be dodged.

## How it is put together

The runner stays at `z = 0` and the pitch moves: `Course` slides six 20 m pitch segments
toward the camera and leapfrogs each one to the front once it is behind, and takes
defenders, cones and stars from fixed pools as rows spawn 68 m ahead (inside the fog,
so they fade in). Nothing is created or destroyed during a run. `CourseGenerator`
decides what goes in each row and is pure logic, so it can be tested without a scene.

`CameraFramer` solves the field of view whenever the window changes shape so all three
lanes always fit, and tilts so the runner's feet sit at a fixed height on screen. The
HUD scales in Expand mode against 1280x720 in landscape and 720x1280 in portrait, so
it fits any window and stays readable on an iPhone SE.

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Import the character and build the animator controllers (only after changing the rig or clips)
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Dribble.EditorTools.CharacterImporter.Setup

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod Dribble.EditorTools.SceneBuilder.BuildScene -quit

# Check the course generator and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Dribble.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod Dribble.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/dribble
```

`VerifyScene` checks that each level is a step up from the last, runs 20,000 generated
rows to confirm a lane is always open and stars never sit on a blocker, drives the
course for six simulated minutes on every level to confirm the pools never run dry
and no row walls off all three lanes, and checks that the runner, ball, defenders,
HUD, audio object and web page are all wired up.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Sound

`DribbleAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle at kick-off and after a tackle, a cheer every 100 m, an
"ohhh" when a defender wins the ball, and applause for a new best. The ball touches,
lane-change swoosh, cone knock, tackle thud and the star chime (rising in pitch for
stars collected in a row) are synthesised, and each recording falls back to a
synthesised stand-in if its clip is missing. The web page's sound button mutes the
game through `SendMessage("DribbleAudio", "SetMuted", "1")`.

The clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/dribble/index.html`, or the next build will
overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
