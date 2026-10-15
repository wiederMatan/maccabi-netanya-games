# Headers — Unity source

The playable build lives in `games/header/` and is what the portal links to
("נגיחות"). This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Headers.unity` — generated entirely from code, not hand-placed

## How it plays

A round of ten corners, seen from behind a Maccabi Netanya attacker on the penalty
spot. There are no levels and no start button: as soon as the game loads, "3, 2, 1,
יאללה!" counts down and the first corner is swung in. Each corner comes from the left
or the right ("קרן מימין!"); the attacker jogs to where it will drop, and a ring
closes on the spot where the ball will meet his head. One tap does everything: the
moment of the tap is the jump, and the point of the goal under the finger is where
the header goes. While the jump would still win the ball the ring glows green.

Arrow keys head it left or right and `Space` heads it to the side the keeper has
left open, at the moment the key is pressed.

A goal is worth 10 points, and a perfectly timed one ("נגיחה מושלמת!") 5 more.
Jumping early gets under the ball and sends it over the bar; jumping late heads it
into the grass; outside the window the ball sails past untouched. From the fourth
corner a defender in red and white jumps with the attacker and wins any header that
is not timed well enough. The end card shows the goals, the score and the best, with
one big "שחק שוב" button that counts down the next round (Space or Enter does too).

Every round follows one progression (`Progression` in `HeaderModel.cs`):

| Corner | Cross flight | Timing window | Keeper reacts / speed | Keeper cheats | Defender |
|--------|--------------|---------------|-----------------------|---------------|----------|
| 1      | 1.55 s       | ±0.24 s       | 0.32 s / 3.2 m/s      | up to 0.4 m   | no       |
| 4      | 1.42 s       | ±0.20 s       | 0.27 s / 3.9 m/s      | up to 0.67 m  | yes      |
| 10     | 1.15 s       | ±0.13 s       | 0.17 s / 5.2 m/s      | up to 1.2 m   | yes      |

and everything in between is interpolated. The crosses also land further from goal
and wider across the box as the round goes on. By the end the keeper covers most of
his goal, so a header has to go to the side he has left open.

## Stars for the website

Every finished round earns 1 to 3 stars, shown on the end card and added to the
portal's total: one for finishing, two for **4** goals and three for **7**.

`PortalBridge` (`Assets/Scripts/Runtime/PortalBridge.cs` with
`Assets/Plugins/WebGL/PortalBridge.jslib`) writes the same localStorage keys the
website reads: `MarkPlayed("header")` when a round starts, `AddStars(n)` and
`ReportBest("header", score)` when it ends, and `Haptic(10)` on every button press.
All four are no-ops in the editor. The game keeps its own best too (PlayerPrefs).

## How a header is decided

`HeaderModel.cs` holds the rules, free of the scene so they can be checked headlessly.
The cross is a scripted `Flight` under gravity from the corner to the meeting point,
which sits at the height of the forehead at the top of a jump. The perfect tap is
half a jump (0.31 s) before the ball arrives, which is when the closing ring lands.
When the ball reaches the meeting point, `HeaderJudge.Decide` takes how early or late
the jump was, where the tap aimed and where the keeper stands, and settles the whole
corner: the quality of the contact (1 at perfect, 0 at the edge of the window), where
the header crosses the goal line (aim across, timing up and down, a little wobble for
a scrappy one), and whether the keeper — who reacts, then covers ground at his speed
with an arm's reach on top — gets there. The ball then flies that path and is handed
to physics, so it settles in the net, is parried wide or rolls away.

The rig has no jump or header clip, so `Footballer` poses them by hand on top of the
idle clip: the body is lifted along the jump, the arms swing up and out, the back
arches on the way up and the head and shoulders snap through the ball at contact. A
goal gets a leap with both arms up. The keeper dives with the same mirrored dive clip
as Free Kick.

## Look and language

The UI follows the shared "Maccabi Arcade" spec used by all the club's games: navy
panels with a gold border, chunky gold buttons with a darker lower edge, pill counters
with icons (score, goals, and a corner flag for the corner count), gold titles, and the
Fredoka font (`Assets/Fonts`, SIL Open Font License). `Assets/Editor/UiKit.cs` paints
the pieces, including the corner flag and the timing ring, into `Assets/UI` on every
scene build.

All text is Hebrew. Unity's legacy Text cannot lay out right-to-left, so every string
goes through `Rtl.Fix` (one line) or `Rtl.Wrap` (a paragraph), and no Text component
wraps on its own.

`CameraFramer` frames the goal and the strip of grass where crosses land between the
counters and the page's buttons, at any window shape; on a portrait phone it stands
higher so the tall screen shows more of the box. The corner taker sits at the edge of
the shot or just off it, so the ball swings in from the side.

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Import the character and build the animator controllers (only after changing the rig or clips)
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Headers.EditorTools.CharacterImporter.Setup

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod Headers.EditorTools.SceneBuilder.BuildScene -quit

# Check the header rules and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Headers.EditorTools.BuildAndVerify.VerifyScene

# Render the camera in portrait and landscape to Builds/Shots (needs a GPU, so no -nographics)
"$UNITY" -batchmode -projectPath . -executeMethod Headers.EditorTools.BuildAndVerify.RenderShots

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod Headers.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/header
```

`VerifyScene` spot-checks the Hebrew reordering and Fredoka's Hebrew glyphs; checks
that every corner is at least as hard as the one before and the defender arrives at
the fourth; that every cross ends on its meeting point, arcs between 2.8 and 7 m and
is dropping as it arrives, and that the attacker can jog there in time; that for every
corner a perfect header away from the keeper scores, one straight at him is saved, no
jump or one outside the window never touches the ball, early goes high and late goes
low, and a decent contact aimed inside the posts never goes wide. It simulates rounds:
a perfect player scores 10/10, someone tapping at random averages under one goal, and
a decent player (within 0.12 s, heading away from the keeper) earns two or three stars.
It then checks the humanoid rig and the animator states, that the game, ball, three
outfield players, keeper, goal colliders, HUD, timing ring, audio object and web page
are all wired up, that every Text uses Fredoka and never wraps itself, that every
button has press feedback, and that the portal bridge plugin ships and is called.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers, and without the Unity splash screen, which
would hide the opening countdown.

## Sound

`HeaderAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle at kick-off and full time, a cheer for a goal, an "ohhh"
for a save or a miss, and applause for a new best. The kick of the corner, the jump,
the header (a firmer, higher thud for a better one), the ball off the keeper or the
defender, the countdown and the button tick are synthesised, and each recording falls
back to a synthesised stand-in if its clip is missing. The web page's sound button
mutes the game through `SendMessage("HeaderAudio", "SetMuted", "1")`.

Music is the club's own, composed for these games (CC0), in
`Assets/Resources/Audio/Music/`. The stadium anthem loops behind the end card, the
supporters' drums loop under the crowd during a round and quicken toward the last
corner, the goal sting plays for every goal, and the end card brings the win fanfare,
the goal sting or the "nearly!" sting for three, two or one stars.

For the browser checks, `SendMessage("HeaderGame", "Autopilot", "1")` jumps at the
perfect moment every corner and heads it away from the keeper; `"0"` hands control
back. Every corner is logged to the browser console (`[Header] corner …`) with the
timing, the aim and the result.

The stadium clips are all CC0 or public domain from Wikimedia Commons; sources and
authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/header/index.html`, or the next build will
overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
