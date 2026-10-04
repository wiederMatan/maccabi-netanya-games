# Dribble — Unity source

The playable build lives in `games/dribble/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Run.unity` — generated entirely from code, not hand-placed

## How it plays

An endless run down the pitch. There are no levels and no start button: as soon as the
game loads, "3, 2, 1, יאללה!" counts down over the pitch and the run starts by itself.
A Maccabi Netanya player dribbles the ball forward in one of three lanes; defenders in
red and white, training cones and gold stars come toward him. Swipe left or right, tap
the left or right half of the screen, drag or click with a mouse, or press the arrow
keys / `A` `D` to change lane. Stars are worth 10 points each and every metre is a
point. Knocking over a cone only costs some speed; running into a defender ends the
run — he goes down, the ball rolls loose and the crowd groans. The end card shows the
stars earned, the score and the best, and one big "שחק שוב" button that counts down
the next run (Space or Enter does too).

Every run follows one progression (`Progression.cs`). It kicks off at 5.5 m/s and
speeds up steadily to 15 m/s after 65 s. As it does, the pitch gets busier:

| Speed            | Seconds between rows | Rows blocking two lanes | Defenders (vs cones) |
|------------------|----------------------|-------------------------|----------------------|
| 5.5 m/s (start)  | 2.5                  | never                   | 50%                  |
| 15 m/s (top)     | 1.45                 | 55%                     | 75%                  |

and everything in between is interpolated by speed. A lane is always left open, so
every row can be dodged.

## Stars for the website

Every finished run earns 1 to 3 stars, shown in three slots on the end card and added
to the portal's total. The first star is for finishing a run at all; the others need a
score (metres + 10 per star collected) of at least **250** for two stars (a kid who
lasts about half a minute and picks up some stars) and **650** for three (a good run
of three-quarters of a minute or more).

`PortalBridge` (`Assets/Scripts/Runtime/PortalBridge.cs` with
`Assets/Plugins/WebGL/PortalBridge.jslib`) writes the same localStorage keys the
website reads: `MarkPlayed("dribble")` when a run starts, `AddStars(n)` and
`ReportBest("dribble", score)` when it ends, and `Haptic(10)` on every button press.
All four are no-ops in the editor. The game keeps its own best too (PlayerPrefs), for
the end card and the mid-run "שיא חדש!".

## Look and language

The UI follows the shared "Maccabi Arcade" spec used by all the club's games: navy
panels with a gold border, chunky gold buttons with a darker lower edge, pill counters
with icons, gold titles with an outline and drop shadow, and the Fredoka font
(`Assets/Fonts`, SIL Open Font License). The rounded panels, buttons, pills, stars and
icons are painted by `Assets/Editor/UiKit.cs` into `Assets/UI` as 9-sliced sprites
on every scene build. Buttons squash to 94% when pressed, with a synthesised tick and
a short buzz (`PressFeedback`); the countdown numbers and the end card pop in, and the
earned stars drop into their slots one by one.

All text is Hebrew. Unity's legacy Text cannot lay out right-to-left, so every string
goes through `Rtl.Fix` (one line) or `Rtl.Wrap` (a paragraph, wrapped for the current
orientation), and no Text component wraps on its own.

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

`VerifyScene` spot-checks the Hebrew reordering (words reversed, numbers kept in
order, wrapped lines in the right order) and that Fredoka has every Hebrew letter,
checks that the progression only speeds up and gets busier, reaches top speed within
75 s and keeps rows far enough apart to dodge, that the star awards run 1 to 3 at 250
and 650, runs 20,000 generated
rows to confirm a lane is always open and stars never sit on a blocker, drives the
course for six simulated minutes to confirm the pools never run dry
and no row walls off all three lanes, and checks that the runner, ball, defenders,
HUD, audio object and web page are all wired up, that every Text uses Fredoka and
never wraps itself, that every button has press feedback, and that the portal bridge
plugin ships and is called.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers, and without the Unity splash screen, which
would hide the opening countdown.

## Sound

`DribbleAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle at kick-off and after a tackle, a cheer every 100 m, an
"ohhh" when a defender wins the ball, and applause for a new best. The ball touches,
lane-change swoosh, cone knock, tackle thud and the star chime (rising in pitch for
stars collected in a row, also played as each star lands on the end card) and the
button tick are synthesised, and each recording falls back to a
synthesised stand-in if its clip is missing. The web page's sound button mutes the
game through `SendMessage("DribbleAudio", "SetMuted", "1")`.

Music is the club's own, composed for these games (CC0), in
`Assets/Resources/Audio/Music/`. The 30 s stadium anthem loops behind the end card at low
volume, and fades out over 0.8 s as the next countdown starts; the very first run goes
straight to the crowd and drums. The supporters' drums
loop under the crowd during a run, and their pitch rises from 1.0 at the start speed to
1.12 at top speed. Every 100 m, and on passing your best mid-run, the goal
sting plays over a softer cheer. When the end card appears, a run that earned 3 stars
gets the win fanfare, 2 stars the goal sting and 1 star the "nearly!" sting, and the
anthem comes back in. `MusicStar` ships with the set but is not used: the stars landing
on the end card keep their rising chime, so the two never double up.

For the browser checks, `SendMessage("DribbleGame", "Autopilot", "1")` makes the runner
steer round every blocker, so a scripted run can reach a milestone or a star threshold;
`"0"` hands control back.

The stadium clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/dribble/index.html`, or the next build will
overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
