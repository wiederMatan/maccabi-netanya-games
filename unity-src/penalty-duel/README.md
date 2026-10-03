# Penalty Duel — Unity source

The playable build lives in `games/penalty-duel/` and is what the portal links to
("פנדלים לשניים"). This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Match.unity` — generated entirely from code, not hand-placed

## How it plays

A penalty shootout for two players sharing one phone, tablet or computer. Every kick
has two picks:

1. The shooter taps one of six spots in the goal (top or bottom; left, middle or
   right), or presses `1`–`6` (top row `1 2 3`, bottom row `4 5 6`).
2. A "pass the phone" screen covers everything so the keeper cannot see the pick.
   The keeper taps "Ready to save!" (or presses Enter / Space), then taps where to
   dive the same way.

The run-up, the kick and the dive then play out together. Diving to the exact spot
always saves it; the right side at the wrong height saves it half the time; the
wrong side never does. The players swap roles every kick — and the keeper of one
kick is the shooter of the next, so the phone only changes hands once per kick.

Five kicks each, finishing early once one side cannot catch up, then sudden death
one round at a time. The scoreboard shows a tick or a cross for every kick, like a
TV shootout graphic. Player 1 wears Maccabi yellow and black, Player 2 a sky-blue
away kit, and each side shoots as a randomly drawn member of the squad (portraits
from `Assets/Resources/Players`, see `Roster.cs`).

"VS Computer" lets one child play alone: the computer aims and dives at random, and
there is no pass screen.

Keyboard on the menu: Enter for 2 players, `C` for vs Computer; on the end screen,
Enter plays again and Escape goes back to the menu.

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Import the character and build the animator controllers (only after changing them)
"$UNITY" -batchmode -nographics -projectPath . -executeMethod PenaltyDuel.EditorTools.CharacterImporter.Setup

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod PenaltyDuel.EditorTools.SceneBuilder.BuildScene -quit

# Check the shootout rules and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod PenaltyDuel.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod PenaltyDuel.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/penalty-duel
```

`VerifyScene` plays scripted shootouts through `Shootout` (turn order, the early
finish, sudden death, the save rule, a thousand random shootouts that must all end),
checks the roster and its portraits, and checks the scene: six spots with the right
index, aim point and tap area (a ray from the aim camera must land on each one), the
keeper's mirrored dive, the striker's run and kick, the HUD and its buttons, the
audio object the page talks to, and a pass-the-phone screen that is fully opaque and
drawn on top.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

## Notes

- `CameraFramer` has two shots: a close-up on the goal while a player picks a spot,
  so the spots are as big as the screen allows, and a wide shot from behind the taker
  for the kick. Both are fitted into the band the HUD leaves free, for portrait and
  landscape alike, and the camera eases between them.
- `HudController` lays the scoreboard and the prompt side by side along the top of a
  wide screen and stacks them on a tall or narrow one.
- Taps are read straight from the pointer in `MatchManager` rather than through
  `OnMouseDown` or the UI event system: in a mobile browser a quick tap can start and
  end within one frame, and both of those can then miss it.
- The keeper has one dive clip (`Anim_Dive`), mirrored for the other side through a
  `Mirror` parameter and played at double speed so he is stretched out by the time
  the ball arrives.
- The ball leaves the spot when the boot reaches it in `Anim_Kick` (half way through
  the clip), not after a fixed delay.
- In-game text is English and plain ASCII: the built-in font in the web build cannot
  shape Hebrew and has no dashes or ellipsis.
- `Assets/Plugins/PhaseBridge.jslib` publishes the current step (Menu, Shoot, Pass,
  PassShown, Save, Kick, Over) as `window.penaltyDuelPhase`, which the browser tests
  wait on.

## Sound

`MatchAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle (kick-off, and two short and one long at full time), a
cheer for a goal, an "ohhh" for a save, and applause for the winner (not when the
computer wins). The kick is synthesised, and each recording falls back to a
synthesised stand-in if its clip is missing. The web page's sound button mutes the
game through `SendMessage("MatchAudio", "SetMuted", "1")`.

The clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/penalty-duel/index.html`, or the next build
will overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
