using System.IO;
using System.Linq;
using Juggling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Juggling.EditorTools
{
    /// <summary>
    /// Headless checks that the difficulty levels are fair, that the ball can never
    /// leave the play area, and that the built scene is actually wired up - plus a
    /// one-command WebGL build. Everything here is runnable from the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = "Assets/Scenes/Juggling.unity";
        const string TemplatePath = "Assets/WebGLTemplates/MaccabiNetanya/index.html";

        [MenuItem("Juggling/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckTiers();
            failures += CheckFlight();
            failures += CheckScoring();
            failures += CheckSceneWiring();
            failures += CheckTemplate();

            if (failures > 0)
            {
                Debug.LogError($"[Verify] {failures} check(s) failed.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Verify] All checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Each level is a little harder than the one before, every kick tops out
        /// below the HUD, and even at full speed a falling ball gives a child time
        /// to react.
        /// </summary>
        static int CheckTiers()
        {
            int failures = 0;
            var all = Tiers.All;

            failures += Require(all.Length == 4, $"Expected 4 levels, found {all.Length}.");
            failures += Require(all.Select(t => t.Name).SequenceEqual(new[] { "Starter", "Easy", "Medium", "Hard" }),
                "Levels should be Starter, Easy, Medium, Hard in that order.");

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                failures += Require(t.HitScale >= 1f, $"{t.Name}: the tap area is smaller than the ball.");
                failures += Require(t.GravityEnd >= t.GravityStart, $"{t.Name}: gravity ramps down.");
                failures += Require(t.ApexMax >= t.ApexMin, $"{t.Name}: apex band is upside down.");
                failures += Require(t.ApexMax + t.BallRadius <= CameraFramer.Ceiling,
                    $"{t.Name}: a kick can carry the ball above the ceiling.");

                // From the lowest apex at full gravity, the ball must take at least
                // three quarters of a second to reach the grass.
                float fall = Mathf.Sqrt(2f * (t.ApexMin - t.BallRadius) / t.GravityEnd);
                failures += Require(fall >= 0.75f, $"{t.Name}: the ball falls in {fall:0.00}s at full speed.");

                if (i == 0) continue;
                var easier = all[i - 1];
                failures += Require(t.BallRadius < easier.BallRadius, $"{t.Name}: ball is not smaller than {easier.Name}.");
                failures += Require(t.GravityStart > easier.GravityStart, $"{t.Name}: not faster than {easier.Name}.");
                failures += Require(t.Drift > easier.Drift, $"{t.Name}: drifts no more than {easier.Name}.");
                failures += Require(t.HitScale * t.BallRadius < easier.HitScale * easier.BallRadius,
                    $"{t.Name}: tap area is not smaller than {easier.Name}.");
            }

            if (failures == 0) Debug.Log($"[Verify] Levels: OK ({all.Length} levels, each harder than the last).");
            return failures;
        }

        /// <summary>
        /// Fly thousands of kicks, struck anywhere across the ball, through every
        /// level and a range of screen widths: the ball must stay between the side
        /// walls, never rise past the ceiling, and always come back down.
        /// </summary>
        static int CheckFlight()
        {
            int failures = 0;
            var random = new System.Random(4242);
            int kicks = 0;
            const float dt = 1f / 60f;
            float[] halfWidths = { 1.4f, 2.1f, 3.2f };

            foreach (var tier in Tiers.All)
            {
                foreach (float halfWidth in halfWidths)
                {
                    var position = new Vector2(0f, tier.ApexMax);
                    var velocity = Vector2.zero;
                    bool failed = false;

                    for (int touch = 0; touch < 300 && !failed; touch++)
                    {
                        float gravity = tier.GravityAt(touch);
                        float apex = Mathf.Lerp(tier.ApexMin, tier.ApexMax, (float)random.NextDouble());
                        float offset = (float)(random.NextDouble() * 2.4 - 1.2);
                        velocity = BallPhysics.Kick(position, offset, apex,
                            CameraFramer.Ceiling - tier.BallRadius, gravity, tier.Drift);
                        kicks++;

                        float peak = BallPhysics.PeakHeight(position, velocity, gravity);
                        if (peak + tier.BallRadius > CameraFramer.Ceiling + 0.01f)
                        {
                            Debug.LogError($"[Verify] {tier.Name}: kick from {position.y:0.00} peaks at {peak:0.00}.");
                            failures++;
                            failed = true;
                        }

                        // Fly until it falls to a random height in the bottom half,
                        // where the next touch happens - or the grass.
                        float kickAt = Mathf.Lerp(tier.BallRadius + 0.2f, 2.5f, (float)random.NextDouble());
                        for (int step = 0; step < 2000; step++)
                        {
                            bool landed = BallPhysics.Step(ref position, ref velocity, dt, gravity, tier.BallRadius, halfWidth);
                            if (Mathf.Abs(position.x) > halfWidth - tier.BallRadius + 0.001f)
                            {
                                Debug.LogError($"[Verify] {tier.Name}: ball left the play area at x={position.x:0.00}.");
                                failures++;
                                failed = true;
                                break;
                            }
                            if (landed || (velocity.y < 0f && position.y <= kickAt)) break;
                            if (step == 1999)
                            {
                                Debug.LogError($"[Verify] {tier.Name}: ball never came back down.");
                                failures++;
                                failed = true;
                            }
                        }
                        position.y = Mathf.Max(position.y, tier.BallRadius);
                    }
                }
            }

            if (failures == 0) Debug.Log($"[Verify] Ball flight: OK ({kicks} kicks stayed in the play area).");
            return failures;
        }

        static int CheckScoring()
        {
            int failures = 0;

            failures += Require(ScoreRules.IsClean(0f) && !ScoreRules.IsClean(0.9f) && !ScoreRules.IsClean(-0.9f),
                "Clean-touch test is wrong.");
            failures += Require(ScoreRules.PointsFor(0) == 1 && ScoreRules.PointsFor(ScoreRules.DoublePointsCombo) == 2,
                "Points per touch are wrong.");
            failures += Require(ScoreRules.MilestoneCrossed(9, 10) == 10, "Missed the 10 milestone.");
            failures += Require(ScoreRules.MilestoneCrossed(24, 26) == 25, "A double-points touch skipped the 25 milestone.");
            failures += Require(ScoreRules.MilestoneCrossed(10, 11) == 0, "A milestone fired twice.");
            failures += Require(ScoreRules.MilestoneCrossed(49, 50) == 50, "Missed the 50 milestone.");

            // Tapping the left side of the ball must send it right.
            var right = BallPhysics.Kick(new Vector2(0f, 1f), 0.8f, 4f, 4.5f, 5f, 1f);
            var left = BallPhysics.Kick(new Vector2(0f, 1f), -0.8f, 4f, 4.5f, 5f, 1f);
            failures += Require(right.x > 0f && left.x < 0f && right.y > 0f, "Kick direction is backwards.");

            if (failures == 0) Debug.Log("[Verify] Scoring: OK.");
            return failures;
        }

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var manager = Object.FindFirstObjectByType<JuggleManager>();
            if (manager == null)
            {
                Debug.LogError("[Verify] No JuggleManager in the scene.");
                return failures + 1;
            }

            var serialized = new SerializedObject(manager);
            foreach (var field in new[] { "ball", "hud", "audio_", "framer", "cheer" })
                failures += Require(serialized.FindProperty(field)?.objectReferenceValue != null,
                    $"JuggleManager.{field} is not wired.");

            var ball = Object.FindFirstObjectByType<JuggleBall>();
            failures += Require(ball != null, "Ball is missing.");
            if (ball != null)
            {
                failures += Require(ball.Shadow != null, "Ball has no shadow.");
                failures += Require(ball.GetComponent<Renderer>() != null, "Ball has no renderer.");
            }

            var audio = Object.FindFirstObjectByType<JuggleAudio>();
            failures += Require(audio != null && audio.name == "JuggleAudio",
                "No audio object named JuggleAudio for the page's mute button to reach.");
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip {clip} is missing.");

            var camera = Object.FindFirstObjectByType<Camera>();
            failures += Require(camera != null && camera.GetComponent<CameraFramer>() != null,
                "Camera has no CameraFramer.");
            failures += Require(camera != null && camera.GetComponent<AudioListener>() != null, "No AudioListener.");
            failures += Require(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");

            var cheer = Object.FindFirstObjectByType<PlayerCheer>();
            failures += Require(cheer != null, "The Maccabi Netanya player is missing.");
            if (cheer != null)
            {
                var animator = cheer.GetComponentInChildren<Animator>();
                failures += Require(animator != null && animator.runtimeAnimatorController != null,
                    "The player has no animator controller.");
                failures += Require(cheer.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0,
                    "The player has no mesh.");
            }

            failures += Require(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                "No EventSystem, so the overlay buttons cannot be pressed.");

            var hud = Object.FindFirstObjectByType<HudController>();
            failures += Require(hud != null, "HUD is missing.");
            if (hud != null)
            {
                failures += Require(hud.StartButton != null, "HUD start button is not wired.");
                failures += Require(hud.DifficultyButtons != null && hud.DifficultyButtons.Length == Tiers.All.Length &&
                                    hud.DifficultyButtons.All(b => b != null),
                    "Difficulty buttons are not all wired.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 15, $"HUD looks under-built ({texts.Length} text elements).");
                failures += Require(texts.All(t => t.font != null), "A HUD text element has no font.");

                var scaler = hud.GetComponent<CanvasScaler>();
                failures += Require(scaler != null &&
                                    scaler.referenceResolution == new Vector2(HudController.ReferenceSize, HudController.ReferenceSize),
                    "Canvas scaler is not on the square reference the camera framing assumes.");
            }

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
            return failures;
        }

        /// <summary>The page's mute button has to address the object the scene actually has.</summary>
        static int CheckTemplate()
        {
            int failures = 0;
            string html = File.Exists(TemplatePath) ? File.ReadAllText(TemplatePath) : "";

            failures += Require(html.Length > 0, $"WebGL template missing at {TemplatePath}.");
            failures += Require(html.Contains("SendMessage(\"JuggleAudio\", \"SetMuted\""),
                "Template does not send SetMuted to JuggleAudio.");
            failures += Require(html.Contains("../../app.js"), "Template does not load the shared app.js.");
            failures += Require(html.Contains("apple-mobile-web-app-capable") && html.Contains("manifest.webmanifest"),
                "Template lost its PWA tags.");
            failures += Require(html.Contains("id=\"sound-toggle\"") && html.Contains("class=\"back\""),
                "Template lost its back or sound button.");

            if (failures == 0) Debug.Log("[Verify] WebGL template: OK.");
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
        [MenuItem("Juggling/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.productName = "Juggling";
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
