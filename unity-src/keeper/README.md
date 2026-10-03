# Keeper — Unity source

The playable build lives in `games/keeper/` and is what the portal links to.
This folder is the Unity project it is built from.

- **Unity version:** 6000.6.3f1
- **Scene:** `Assets/Scenes/Keeper.unity` — generated entirely from code, not hand-placed

## How it plays

You are Maccabi Netanya's goalkeeper, seen from behind the goal. Each penalty, the
striker picks a spot and it glows for a moment (the tell) while he runs in; dive
the right way before the ball crosses the line and it is a save. A round is five
penalties, with saves, a streak, and the best streak per level kept on the HUD.

Controls: swipe left / right (up-left / up-right for the top corners), or tap the
spot you want to dive to; on a keyboard, the arrow keys or WASD (hold Up for a top
corner), `1` `2` `3`, and `4` `5` (or `Q` `E`) for the top corners. A guess made
before the kick is held until the ball is struck, so the keeper never leaves his
line early.

Levels are chosen on the start screen:

| Level   | Spots | Glow  | Ball flight |
|---------|-------|-------|-------------|
| Starter | 3     | 1.6 s | 1.25 s      |
| Easy    | 3     | 0.9 s | 0.95 s      |
| Medium  | 5     | 0.6 s | 0.8 s       |
| Hard    | 5     | 0.35 s| 0.62 s      |

The table lives in `Assets/Scripts/Runtime/Levels.cs`.

## Rebuilding

The scene is built from `Assets/Editor/SceneBuilder.cs`, so it can be regenerated from
scratch. All commands are headless:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity

# Re-import the character and rebuild the animator controllers (only after changing them)
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Keeper.EditorTools.CharacterImporter.Setup

# Regenerate the scene
"$UNITY" -batchmode -projectPath . -executeMethod Keeper.EditorTools.SceneBuilder.BuildScene

# Check the levels, the dive controls, the animators, the framing and the scene wiring
"$UNITY" -batchmode -nographics -projectPath . -executeMethod Keeper.EditorTools.BuildAndVerify.VerifyScene

# Rebuild the web version into the portal
"$UNITY" -batchmode -nographics -projectPath . -buildTarget WebGL \
  -executeMethod Keeper.EditorTools.BuildAndVerify.BuildWebGL \
  -outputPath ../../games/keeper
```

`VerifyScene` checks that each level is harder than the one before and every spot
sits inside the goal, that swipes and taps map to the spot a child would expect,
that the keeper has a dive each way (the right-hand one is `Anim_Dive` mirrored)
and the striker a run and a kick, that the goal and striker stay on screen and
clear of the HUD on phone portrait, phone landscape, tablet and desktop shapes,
and that the ball, keeper, striker, spot rings, HUD and `MatchAudio` are wired up.

WebGL is built with compression disabled so the files can be served by any static host
without special `Content-Encoding` headers.

The game logs `[Keeper] tell …` and `[Keeper] result …` lines to the browser console;
the automated browser tests use them to know when to dive.

## Sound

`MatchAudio` plays real stadium recordings from `Assets/Resources/Audio/`: a looping
crowd, the referee's whistle before each penalty (and two short and one long at the
end of a round), a cheer for a save, an "ohhh" for a goal against, and applause for
three saves or more. The kick, the keeper's dive and the glove on the ball are
synthesised, and each recording falls back to a synthesised stand-in if its clip is
missing. The web page's sound button mutes the game through
`SendMessage("MatchAudio", "SetMuted", "1")`.

The clips are the same ones the memory game uses, all CC0 or public domain from
Wikimedia Commons; sources and authors are in `sounds/CREDITS.md` at the repo root.

The web page is generated from `Assets/WebGLTemplates/MaccabiNetanya/index.html`, so
edit that template rather than `games/keeper/index.html`, or the next build
will overwrite the change.

After shipping a new build, bump `VERSION` in `sw.js` at the repo root: the build's
file names never change, so phones would otherwise keep playing the cached one.
