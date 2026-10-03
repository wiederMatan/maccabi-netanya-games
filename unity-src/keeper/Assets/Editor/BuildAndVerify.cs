using System.Linq;
using Keeper;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Keeper.EditorTools
{
    /// <summary>
    /// Headless checks that the levels and the dive controls behave and that the
    /// built scene is actually wired up, plus a one-command WebGL build. Everything
    /// here is runnable from CI or the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = SceneBuilder.ScenePath;

        [MenuItem("Keeper/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckLevels();
            failures += CheckInput();
            failures += CheckAnimators();
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

        /// <summary>Each level must be at least as hard as the one before, and every spot inside the frame.</summary>
        static int CheckLevels()
        {
            int failures = 0;
            var levels = Levels.All;

            failures += Require(levels.Length == 4, $"Expected 4 levels, found {levels.Length}.");
            failures += Require(levels[0].SpotCount == 3 && levels[levels.Length - 1].SpotCount == Goal.Spots.Length,
                "Levels should run from 3 spots up to every spot.");

            for (int i = 0; i < levels.Length; i++)
            {
                var l = levels[i];
                failures += Require(l.SpotCount >= 3 && l.SpotCount <= Goal.Spots.Length, $"{l.Name}: bad spot count {l.SpotCount}.");
                failures += Require(l.TellSeconds > 0.2f && l.RunSeconds > 0.3f && l.FlightSeconds > 0.4f,
                    $"{l.Name}: a timing is too short for a child to react to.");
                if (i == 0) continue;
                var p = levels[i - 1];
                failures += Require(l.SpotCount >= p.SpotCount, $"{l.Name} has fewer spots than {p.Name}.");
                failures += Require(l.TellSeconds < p.TellSeconds, $"{l.Name}'s tell is not shorter than {p.Name}'s.");
                failures += Require(l.FlightSeconds < p.FlightSeconds, $"{l.Name}'s shot is not faster than {p.Name}'s.");
            }

            for (int i = 0; i < Goal.Spots.Length; i++)
            {
                var s = Goal.Spots[i];
                failures += Require(Mathf.Abs(s.x) < Goal.Width / 2f - 0.4f && s.y > 0.25f && s.y < Goal.Height - 0.3f,
                    $"Spot {i} ({Goal.SpotNames[i]}) is not comfortably inside the goal.");
            }

            failures += Require(Goal.Side(Goal.Left) < 0 && Goal.Side(Goal.HighLeft) < 0 && Goal.Side(Goal.Right) > 0
                && Goal.Side(Goal.HighRight) > 0 && Goal.Side(Goal.Centre) == 0, "Goal.Side is wrong.");
            failures += Require(Goal.Clamp(Goal.HighLeft, 3) == Goal.Left && Goal.Clamp(Goal.HighRight, 3) == Goal.Right
                && Goal.Clamp(Goal.HighLeft, 5) == Goal.HighLeft, "Goal.Clamp does not fold high spots down on 3-spot levels.");

            if (failures == 0) Debug.Log($"[Verify] Levels: OK ({levels.Length} levels, {Goal.Spots.Length} spots).");
            return failures;
        }

        /// <summary>Swipes and taps land on the spot a child would expect.</summary>
        static int CheckInput()
        {
            int failures = 0;

            (Vector2 swipe, int spots, int expected)[] swipes =
            {
                (new Vector2(-100f, 0f), 3, Goal.Left),
                (new Vector2(100f, 10f), 3, Goal.Right),
                (new Vector2(5f, 100f), 3, Goal.Centre),
                (new Vector2(-5f, -100f), 5, Goal.Centre),
                (new Vector2(-100f, 80f), 5, Goal.HighLeft),
                (new Vector2(100f, 70f), 5, Goal.HighRight),
                (new Vector2(-100f, 80f), 3, Goal.Left),
                (new Vector2(100f, -60f), 5, Goal.Right),
                (new Vector2(-100f, 20f), 5, Goal.Left),
            };

            foreach (var (swipe, spots, expected) in swipes)
            {
                int got = DiveInput.FromSwipe(swipe, spots);
                failures += Require(got == expected,
                    $"Swipe {swipe} with {spots} spots went {Goal.SpotNames[got]}, expected {Goal.SpotNames[expected]}.");
            }

            // A portrait phone's view of the goal, roughly: spots spread across the width.
            Vector2[] screen =
            {
                new Vector2(70f, 300f), new Vector2(195f, 360f), new Vector2(320f, 300f),
                new Vector2(60f, 420f), new Vector2(330f, 420f),
            };

            (Vector2 tap, int spots, int expected)[] taps =
            {
                (new Vector2(20f, 600f), 3, Goal.Left),
                (new Vector2(380f, 100f), 3, Goal.Right),
                (new Vector2(200f, 300f), 3, Goal.Centre),
                (new Vector2(40f, 440f), 5, Goal.HighLeft),
                (new Vector2(40f, 280f), 5, Goal.Left),
                (new Vector2(350f, 460f), 5, Goal.HighRight),
                (new Vector2(195f, 450f), 5, Goal.Centre),
            };

            foreach (var (tap, spots, expected) in taps)
            {
                int got = DiveInput.FromTap(tap, screen, spots);
                failures += Require(got == expected,
                    $"Tap {tap} with {spots} spots went {Goal.SpotNames[got]}, expected {Goal.SpotNames[expected]}.");
            }

            if (failures == 0) Debug.Log($"[Verify] Dive input: OK ({swipes.Length} swipes, {taps.Length} taps).");
            return failures;
        }

        /// <summary>The keeper needs a dive each way and the striker a run and a kick.</summary>
        static int CheckAnimators()
        {
            int failures = 0;

            var keeper = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Characters/KeeperAnimator.controller");
            var striker = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Characters/StrikerAnimator.controller");
            failures += Require(keeper != null && striker != null, "Animator controllers are missing.");
            if (failures > 0) return failures;

            failures += Require(HasTrigger(keeper, "Dive") && HasTrigger(keeper, "DiveRight"), "Keeper is missing a dive trigger.");
            failures += Require(HasTrigger(striker, "Run") && HasTrigger(striker, "Kick"), "Striker is missing Run or Kick.");

            var states = keeper.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            var left = states.FirstOrDefault(s => s.name == "Dive");
            var right = states.FirstOrDefault(s => s.name == "DiveRight");
            failures += Require(left != null && !left.mirror, "Left dive state missing or mirrored.");
            failures += Require(right != null && right.mirror, "Right dive state missing or not mirrored.");
            failures += Require(right != null && right.motion == left?.motion, "The two dives should share Anim_Dive.");
            failures += Require(left != null && left.speed > 1.5f, "The dive is not sped up - it would land after the ball.");
            failures += Require(states.Any(s => s.name == "Idle"), "Keeper has no Idle state.");

            if (failures == 0) Debug.Log("[Verify] Animators: OK (dive left, mirrored dive right, run, kick).");
            return failures;
        }

        static bool HasTrigger(AnimatorController controller, string name) =>
            controller.parameters.Any(p => p.name == name && p.type == AnimatorControllerParameterType.Trigger);

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var game = Object.FindFirstObjectByType<KeeperGame>();
            if (game == null)
            {
                Debug.LogError("[Verify] No KeeperGame in the scene.");
                return failures + 1;
            }

            // Every serialized reference on the game object has to be filled in.
            var serialized = new SerializedObject(game);
            foreach (var field in new[] { "ball", "keeper", "striker", "hud", "audio_", "view" })
            {
                var property = serialized.FindProperty(field);
                failures += Require(property != null && property.objectReferenceValue != null, $"KeeperGame.{field} is not wired.");
            }
            var markersProperty = serialized.FindProperty("markers");
            failures += Require(markersProperty != null && markersProperty.arraySize == Goal.Spots.Length,
                $"KeeperGame should hold {Goal.Spots.Length} spot markers.");

            var markers = Object.FindObjectsByType<SpotMarker>(FindObjectsSortMode.None);
            failures += Require(markers.Length == Goal.Spots.Length, $"Expected {Goal.Spots.Length} spot markers, found {markers.Length}.");
            foreach (var marker in markers)
            {
                var ring = new SerializedObject(marker).FindProperty("ring");
                failures += Require(ring != null && ring.objectReferenceValue != null, $"{marker.name} has no ring renderer.");
                failures += Require(marker.transform.position.z < 0f, $"{marker.name} is behind the keeper instead of in front of the camera.");
            }

            // The web page mutes the game with SendMessage("MatchAudio", "SetMuted", ...).
            var audio = GameObject.Find("MatchAudio");
            failures += Require(audio != null && audio.GetComponent<MatchAudio>() != null,
                "No 'MatchAudio' object for the page's sound button to reach.");
            failures += Require(typeof(MatchAudio).GetMethod("SetMuted", new[] { typeof(string) }) != null,
                "MatchAudio.SetMuted(string) is missing.");

            var ball = Object.FindFirstObjectByType<BallController>();
            failures += Require(ball != null && ball.GetComponent<Rigidbody>() != null && ball.GetComponent<Collider>() != null,
                "Ball is missing, or has no Rigidbody or collider.");
            failures += Require(ball != null && Mathf.Abs(ball.transform.position.z - Goal.PenaltySpotZ) < 0.01f,
                "Ball is not on the penalty spot.");

            var netBack = GameObject.Find("NetBackWall");
            failures += Require(netBack != null && netBack.GetComponent<Collider>() != null, "The net has no back wall to stop the ball.");

            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            failures += Require(keeper != null && keeper.GetComponentInChildren<Animator>()?.runtimeAnimatorController != null,
                "Goalkeeper is missing or has no animator controller.");
            failures += Require(keeper != null && Vector3.Dot(keeper.transform.forward, Vector3.forward) > 0.99f,
                "The keeper should face out of the goal, toward the striker.");

            var striker = Object.FindFirstObjectByType<StrikerController>();
            failures += Require(striker != null && striker.GetComponentInChildren<Animator>()?.runtimeAnimatorController != null,
                "Striker is missing or has no animator controller.");

            var hud = Object.FindFirstObjectByType<HudController>();
            if (hud == null)
            {
                failures += Require(false, "HUD is missing.");
            }
            else
            {
                failures += Require(hud.StartButton != null, "HUD start button is not wired.");
                failures += Require(hud.LevelButtons != null && hud.LevelButtons.Length == Levels.All.Length
                    && hud.LevelButtons.All(b => b != null), "HUD level buttons are not all wired.");
                failures += Require(hud.PipCount == KeeperGame.ShotsPerRound, "HUD should have one pip per shot.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 15, $"HUD looks under-built ({texts.Length} text elements).");
                failures += Require(texts.All(t => t.font != null), "A HUD text element has no font.");
                failures += Require(texts.All(t => t.fontSize >= 28), "A HUD text is too small to read on a phone.");
            }

            failures += CheckFraming();

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
            return failures;
        }

        /// <summary>
        /// On every screen shape the game targets, the whole goal and the striker
        /// have to land on screen, clear of the HUD at the top and bottom.
        /// </summary>
        static int CheckFraming()
        {
            int failures = 0;
            var camera = Object.FindFirstObjectByType<Camera>();
            var framer = camera != null ? camera.GetComponent<CameraFramer>() : null;
            if (framer == null) return Require(false, "Camera has no CameraFramer.");

            Vector2[] screens =
            {
                new Vector2(390f, 664f), new Vector2(844f, 390f), new Vector2(320f, 568f),
                new Vector2(1280f, 720f), new Vector2(768f, 1024f), new Vector2(1920f, 900f)
            };
            Vector3[] mustSee =
            {
                new Vector3(-Goal.Width / 2f, 0f, 0f), new Vector3(Goal.Width / 2f, 0f, 0f),
                new Vector3(-Goal.Width / 2f, Goal.Height, 0f), new Vector3(Goal.Width / 2f, Goal.Height, 0f),
                new Vector3(0f, 0f, Goal.PenaltySpotZ), new Vector3(0.35f, 1.9f, Goal.PenaltySpotZ + 3.2f),
            };

            var pose = (camera.transform.position, camera.transform.rotation, camera.fieldOfView);
            foreach (var size in screens)
            {
                framer.Frame(size.x, size.y);
                camera.aspect = size.x / size.y;
                float unit = Mathf.Sqrt(size.x * size.y / (1920f * 1080f));
                // Top card (24 + 168 units) and bottom bar (24 + 130 units) must stay clear.
                float top = 1f - (24f + 168f) * unit / size.y;
                float bottom = (24f + 130f) * unit / size.y;

                foreach (var point in mustSee)
                {
                    var v = camera.WorldToViewportPoint(point);
                    bool ok = v.z > 0f && v.x > 0f && v.x < 1f && v.y > bottom && v.y < top;
                    failures += Require(ok, $"At {size.x}x{size.y} the point {point} lands at {v} (band {bottom:0.00}-{top:0.00}).");
                }
                Debug.Log($"[Verify] Framing {size.x}x{size.y}: fov {camera.fieldOfView:0.0}, camera at {camera.transform.position}.");
            }
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.fieldOfView = pose.fieldOfView;
            camera.ResetAspect();

            if (failures == 0) Debug.Log($"[Verify] Framing: OK ({screens.Length} screen shapes).");
            return failures;
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
        [MenuItem("Keeper/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            // The page resizes the canvas to the viewport; these only seed it.
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.productName = "Keeper";
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
    }
}
