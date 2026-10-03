using System;
using System.Linq;
using FreeKick;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FreeKick.EditorTools
{
    /// <summary>
    /// Headless checks that the shot model behaves - every level can be scored on,
    /// the wall and keeper do their jobs, the aim assist keeps a child's wild swipe on
    /// target - and that the built scene is wired up, plus a one-command player build.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = SceneBuilder.ScenePath;
        const string BuildPath = "Builds/FreeKick.app";
        const string TemplatePath = "Assets/WebGLTemplates/MaccabiNetanya/index.html";

        [MenuItem("Free Kick/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckShotModel();
            failures += CheckRoster();
            failures += CheckSceneWiring();

            if (failures > 0)
            {
                Debug.LogError($"[Verify] {failures} check(s) failed.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Verify] All checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- shot model

        static int CheckShotModel()
        {
            int failures = 0;
            const float NeverSaves = 0.9999f;

            foreach (Level level in Enum.GetValues(typeof(Level)))
            {
                var settings = LevelSettings.For(level);
                string name = settings.Name;

                // The farthest, widest kick the level can set, wall to the near post.
                var spot = new Vector3(settings.MaxSide, Pitch.BallRadius, Pitch.GoalLineZ - settings.MaxDistance);
                var defence = DefenceFor(settings, spot);

                // The path starts on the ball and ends on its target.
                var straight = ShotPath.FromSwipe(spot, 0f, 0.5f, 0f, settings, 0f);
                failures += Require((straight.Evaluate(0f) - spot).magnitude < 0.001f, $"{name}: path does not start at the ball.");
                failures += Require((straight.Evaluate(1f, false) - straight.Target).magnitude < 0.001f, $"{name}: path does not end on its target.");
                var curler = ShotPath.FromSwipe(spot, 0f, 0.5f, 0.7f, settings, 0.8f);
                var numeric = (curler.Evaluate(0.501f) - curler.Evaluate(0.499f)) / (0.002f * curler.FlightTime);
                failures += Require((numeric - curler.Velocity(0.5f)).magnitude < 0.05f, $"{name}: Velocity() disagrees with the path.");

                // A shot along the ground into the wall is blocked, before the goal line.
                float wallAim = AimThrough(spot, defence.WallCentreX, defence.WallZ);
                var daisyCutter = ShotPath.FromSwipe(spot, wallAim, 0f, 0f, settings, 0f);
                var blocked = ShotJudge.Judge(daisyCutter, defence, NeverSaves);
                failures += Require(blocked.Result == ShotResult.Blocked && blocked.At < 1f && blocked.WallIndex >= 0,
                    $"{name}: a low shot into the wall was {blocked.Result}, not blocked.");

                // Every level can be scored on: some swipe beats the wall and the keeper.
                int scoring = 0, tried = 0;
                for (float aim = -3f; aim <= 3f; aim += 0.5f)
                for (float power = 0f; power <= 1f; power += 0.05f)
                for (float curve = -1f; curve <= 1f; curve += 0.25f)
                {
                    tried++;
                    var path = ShotPath.FromSwipe(spot, aim, power, curve, settings, 0f);
                    if (ShotJudge.Judge(path, defence, NeverSaves).IsGoal) scoring++;
                }
                failures += Require(scoring > 0, $"{name}: no swipe at all scores from {settings.MaxDistance} m.");
                Debug.Log($"[Verify] {name}: {scoring} of {tried} test swipes score from the hardest spot.");

                // The keeper saves what he reaches when the roll goes his way, never otherwise.
                var atKeeper = new Vector3(defence.KeeperX, 1f, Pitch.GoalLineZ);
                failures += Require(ShotJudge.SaveChance(atKeeper, defence, -1) > 0f, $"{name}: keeper can never save.");
                failures += Require(ShotJudge.SaveChance(atKeeper, defence, -1) < 0.9f, $"{name}: keeper is unbeatable.");

                // Both rings sit wholly inside the frame, and a shot through one counts it.
                for (int side = -1; side <= 1; side += 2)
                {
                    var centre = Pitch.RingCentre(side, settings.RingRadius);
                    bool inside = Mathf.Abs(centre.x) + settings.RingRadius < Pitch.GoalWidth / 2f &&
                                  centre.y + settings.RingRadius < Pitch.GoalHeight && centre.y - settings.RingRadius > 0f;
                    failures += Require(inside, $"{name}: ring {side} pokes outside the goal.");
                    int ring = ShotJudge.RingAt(new Vector3(centre.x, centre.y, Pitch.GoalLineZ), settings.RingRadius);
                    failures += Require(ring == (side < 0 ? 0 : 1), $"{name}: a shot through ring {side} was not counted.");
                }

                // Aim assist: on the youngest levels even a wild swipe stays on target.
                if (settings.AimAssist >= 0.8f)
                {
                    foreach (var wild in new[] { new Vector2(-15f, 1f), new Vector2(15f, 1f), new Vector2(9f, 0f) })
                    {
                        var path = ShotPath.FromSwipe(spot, wild.x, wild.y, 0f, settings, 0f);
                        var c = path.Crossing;
                        failures += Require(Mathf.Abs(c.x) < Pitch.GoalWidth / 2f && c.y < Pitch.GoalHeight,
                            $"{name}: aim assist let a wild swipe ({wild.x}, {wild.y}) go off target at {c}.");
                    }
                }
                else if (settings.AimAssist == 0f)
                {
                    var high = ShotJudge.Judge(ShotPath.FromSwipe(spot, 0f, 1f, 0f, settings, 0f), defence, NeverSaves);
                    failures += Require(high.Result == ShotResult.Over, $"{name}: a full-power shot was {high.Result}, not over.");
                    var wide = ShotJudge.Judge(ShotPath.FromSwipe(spot, 9f, 0.5f, 0f, settings, 0f), defence, NeverSaves);
                    failures += Require(wide.Result == ShotResult.Wide, $"{name}: a shot at x=9 was {wide.Result}, not wide.");
                }

                // The levels have to get harder, not just different.
                if (level > Level.Starter)
                {
                    var easier = LevelSettings.For(level - 1);
                    failures += Require(settings.WallCount >= easier.WallCount && settings.KeeperSkill > easier.KeeperSkill &&
                                        settings.RingRadius < easier.RingRadius && settings.AimAssist <= easier.AimAssist,
                        $"{name} is not harder than {easier.Name}.");
                }
            }

            if (failures == 0) Debug.Log("[Verify] Shot model: OK.");
            return failures;
        }

        /// <summary>The same wall and keeper set-up the game builds for a kick from <paramref name="spot"/>.</summary>
        static Defence DefenceFor(in LevelSettings settings, Vector3 spot)
        {
            float near = spot.x >= 0f ? 1f : -1f;
            var flat = new Vector3(spot.x, 0f, spot.z);
            Vector3 wallSpot = flat + (new Vector3(near * 1.6f, 0f, Pitch.GoalLineZ) - flat).normalized * Pitch.WallDistance;
            float wallTop = Pitch.PlayerHeight * settings.WallScale * 0.98f + settings.WallJump * 0.9f;
            return new Defence(wallSpot.x, wallSpot.z, settings.WallCount, wallTop, -near * 0.6f,
                settings.KeeperSkill, settings.RingRadius);
        }

        /// <summary>The goal-line x that a straight shot from the spot through (x, z) reaches.</summary>
        static float AimThrough(Vector3 spot, float x, float z) =>
            spot.x + (x - spot.x) * (Pitch.GoalLineZ - spot.z) / (z - spot.z);

        /// <summary>The keeper stays in goal - he must never be drawn to take the kicks.</summary>
        static int CheckRoster()
        {
            int failures = 0;

            failures += Require(Roster.Strikers.Length > 0, "No outfield players to pick from.");
            failures += Require(Roster.Strikers.All(m => !m.IsGoalkeeper), "A goalkeeper is in the taker pool.");
            for (int i = 0; i < 500; i++)
            {
                if (!Roster.Random().IsGoalkeeper) continue;
                Debug.LogError("[Verify] Roster.Random returned a goalkeeper.");
                failures++;
                break;
            }

            if (failures == 0) Debug.Log($"[Verify] Roster: OK ({Roster.Strikers.Length} takers).");
            return failures;
        }

        // ---------------------------------------------------------------- scene

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var manager = UnityEngine.Object.FindFirstObjectByType<FreeKickManager>();
            if (manager == null)
            {
                Debug.LogError("[Verify] No FreeKickManager in the scene.");
                return failures + 1;
            }

            // Every reference the manager drives has to be filled in.
            var serialized = new SerializedObject(manager);
            foreach (var field in new[] { "ball", "keeper", "wall", "guide", "hud", "striker", "framer", "audio_" })
            {
                var property = serialized.FindProperty(field);
                failures += Require(property != null && property.objectReferenceValue != null, $"FreeKickManager.{field} is not wired.");
            }
            var ringsProperty = serialized.FindProperty("rings");
            failures += Require(ringsProperty != null && ringsProperty.arraySize == 2, "FreeKickManager should hold two rings.");

            // The web page mutes the game with SendMessage("MatchAudio", "SetMuted", ...).
            var audio = GameObject.Find("MatchAudio");
            failures += Require(audio != null && audio.GetComponent<MatchAudio>() != null, "No MatchAudio object for the page's mute button.");
            failures += Require(typeof(MatchAudio).GetMethod("SetMuted", new[] { typeof(string) }) != null, "MatchAudio.SetMuted(string) is missing.");
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip {clip} is missing.");

            var template = System.IO.File.Exists(TemplatePath) ? System.IO.File.ReadAllText(TemplatePath) : "";
            failures += Require(template.Contains("SendMessage(\"MatchAudio\", \"SetMuted\""), "The web template no longer sends the mute setting.");
            failures += Require(template.Contains($"{HudController.BarBottom + HudController.BarHeight}"),
                "The web template's placeCorner() is out of step with the score bar height.");

            // Ball.
            var ball = UnityEngine.Object.FindFirstObjectByType<BallController>();
            failures += Require(ball != null && ball.GetComponent<Rigidbody>() != null && ball.GetComponent<Collider>() != null,
                "Ball is missing, or has no Rigidbody and collider.");

            // Wall: five players, each animated.
            var wall = UnityEngine.Object.FindFirstObjectByType<WallController>();
            failures += Require(wall != null && wall.Capacity == SceneBuilder.WallPlayers, "Wall should hold five players.");
            int widest = LevelSettings.All.Max(l => l.WallCount);
            failures += Require(wall != null && wall.Capacity >= widest, $"The widest wall needs {widest} players.");
            if (wall != null)
                foreach (var player in wall.Players)
                    failures += Require(player != null && player.GetComponentInChildren<Animator>()?.runtimeAnimatorController != null,
                        "A wall player has no animator.");

            // Taker and keeper animators carry the triggers the code fires.
            var striker = GameObject.Find("Striker");
            failures += Require(HasParameters(striker, "Run", "Kick"), "Striker animator lacks Run/Kick.");
            failures += Require(HasState(striker, "Kick"), "Striker animator has no Kick state for WaitForKickContact.");
            var keeper = UnityEngine.Object.FindFirstObjectByType<GoalkeeperController>();
            failures += Require(keeper != null && HasParameters(keeper.gameObject, "Dive", "Mirror"), "Keeper animator lacks Dive/Mirror.");

            // Aim guide and rings.
            var guide = UnityEngine.Object.FindFirstObjectByType<AimGuide>();
            failures += Require(guide != null && guide.DotCount == SceneBuilder.GuideDots, "Aim guide is missing its dots.");
            var rings = UnityEngine.Object.FindObjectsByType<TargetRing>(FindObjectsSortMode.None);
            failures += Require(rings.Length == 2 && rings.Any(r => r.Side < 0) && rings.Any(r => r.Side > 0),
                "Expected one ring in each top corner.");
            failures += Require(rings.All(r => r.Visual != null && r.Visual.GetComponent<Renderer>().sharedMaterial.mainTexture != null),
                "A ring has no texture.");

            // Camera.
            var camera = Camera.main;
            failures += Require(camera != null && camera.GetComponent<CameraFramer>() != null, "Main camera or its framer is missing.");
            failures += Require(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");

            // HUD.
            var hud = UnityEngine.Object.FindFirstObjectByType<HudController>();
            if (hud == null) failures += Require(false, "HUD is missing.");
            else
            {
                failures += Require(hud.StartButton != null, "HUD start button is not wired.");
                failures += Require(hud.LevelButtons != null && hud.LevelButtons.Length == LevelSettings.All.Length &&
                                    hud.LevelButtons.All(b => b != null), "HUD level buttons are not wired.");
                failures += Require(hud.KickMarks != null && hud.KickMarks.Length == FreeKickManager.KicksPerRound,
                    "HUD needs one square per kick.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 15, $"HUD looks under-built ({texts.Length} text elements).");
                failures += Require(texts.All(t => t.font != null), "A HUD text element has no font.");
            }

            failures += Require(EditorBuildSettings.scenes.Length == 1 && EditorBuildSettings.scenes[0].path == ScenePath,
                "Build settings do not point at the Free Kick scene.");

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
            return failures;
        }

        static AnimatorController ControllerOf(GameObject go) =>
            go == null ? null : go.GetComponentInChildren<Animator>()?.runtimeAnimatorController as AnimatorController;

        static bool HasParameters(GameObject go, params string[] names)
        {
            var controller = ControllerOf(go);
            return controller != null && names.All(n => controller.parameters.Any(p => p.name == n));
        }

        static bool HasState(GameObject go, string name)
        {
            var controller = ControllerOf(go);
            return controller != null && controller.layers[0].stateMachine.states.Any(s => s.state.name == name);
        }

        static int Require(bool condition, string message)
        {
            if (condition) return 0;
            Debug.LogError($"[Verify] {message}");
            return 1;
        }

    /// <summary>
        /// WebGL build for the games portal. Compression is switched off so the
        /// files can be served by any static host without special headers.
        /// </summary>
        [MenuItem("Free Kick/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            // The page resizes the canvas to the window; this is only the size it
            // starts at, matching the HUD's 1920x1080 reference.
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.productName = "Free Kick";
            PlayerSettings.companyName = "Maccabi Netanya Games";
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[WebGL] Succeeded: {summary.totalSize / (1024 * 1024)} MB -> {output}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[WebGL] Failed: {summary.result} ({summary.totalErrors} errors)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static string CommandLineArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        [MenuItem("Free Kick/Build macOS Player")]
        public static void BuildPlayer()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[Build] Succeeded: {summary.totalSize / (1024 * 1024)} MB -> {BuildPath}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[Build] Failed: {summary.result} ({summary.totalErrors} errors)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
