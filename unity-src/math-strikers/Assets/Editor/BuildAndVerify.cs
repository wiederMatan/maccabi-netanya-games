using System;
using System.Linq;
using MaccabiShared;
using MathStrikers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MathStrikers.EditorTools
{
    /// <summary>
    /// Headless checks that the built scene is actually wired up and that the
    /// problem generator behaves, plus a one-command player build. Everything here
    /// is runnable from CI or the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = "Assets/Scenes/Match.unity";
        const string BuildPath = "Builds/MathStrikers.app";

        [MenuItem("Math Strikers/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckGenerator();
            failures += CheckRoster();
            failures += CheckRtl();
            failures += CheckStars();
            failures += CheckSceneWiring();
            failures += CheckLook();

            if (failures > 0)
            {
                Debug.LogError($"[Verify] {failures} check(s) failed.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Verify] All checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static int CheckGenerator()
        {
            int failures = 0;
            int checkedCount = 0;

            foreach (Difficulty difficulty in Enum.GetValues(typeof(Difficulty)))
            {
                for (int i = 0; i < 400; i++)
                {
                    checkedCount++;
                    var problem = ProblemGenerator.Create(difficulty);

                    if (problem.Options == null || problem.Options.Length != ProblemGenerator.OptionCount)
                    {
                        Debug.LogError($"[Verify] {difficulty}: expected {ProblemGenerator.OptionCount} options.");
                        failures++;
                        break;
                    }

                    if (!problem.Options.Contains(problem.Answer))
                    {
                        Debug.LogError($"[Verify] {difficulty}: '{problem.Text}' options miss the answer {problem.Answer}.");
                        failures++;
                        break;
                    }

                    if (problem.Options.Distinct().Count() != problem.Options.Length)
                    {
                        Debug.LogError($"[Verify] {difficulty}: '{problem.Text}' has duplicate options.");
                        failures++;
                        break;
                    }

                    if (problem.Options[problem.CorrectIndex] != problem.Answer)
                    {
                        Debug.LogError($"[Verify] {difficulty}: CorrectIndex points at the wrong option.");
                        failures++;
                        break;
                    }

                    if (problem.Options.Any(o => o < 0))
                    {
                        Debug.LogError($"[Verify] {difficulty}: '{problem.Text}' produced a negative option.");
                        failures++;
                        break;
                    }

                    if (!VerifyArithmetic(problem))
                    {
                        Debug.LogError($"[Verify] {difficulty}: '{problem.Text}' does not equal {problem.Answer}.");
                        failures++;
                        break;
                    }

                    // The starter tier is aimed at 8-9 year olds: nothing above 20,
                    // and no operation a child would have to carry or borrow through.
                    if (difficulty == Difficulty.Starter)
                    {
                        if (problem.Answer > 20 || problem.Answer < 0)
                        {
                            Debug.LogError($"[Verify] Starter: '{problem.Text}' = {problem.Answer} is outside 0-20.");
                            failures++;
                            break;
                        }

                        string[] bits = problem.Text.Split(' ');
                        if (int.Parse(bits[0]) > 20 || int.Parse(bits[2]) > 20)
                        {
                            Debug.LogError($"[Verify] Starter: '{problem.Text}' uses a number above 20.");
                            failures++;
                            break;
                        }

                        if (bits[1] != "+" && bits[1] != "-")
                        {
                            Debug.LogError($"[Verify] Starter: '{problem.Text}' uses '{bits[1]}', expected + or -.");
                            failures++;
                            break;
                        }
                    }
                }
            }

            if (failures == 0) Debug.Log($"[Verify] Problem generator: OK ({checkedCount} problems checked).");
            return failures;
        }

        /// <summary>The keeper stays in goal - he must never be drawn as the striker.</summary>
        static int CheckRoster()
        {
            int failures = 0;

            failures += Require(Roster.Squad.Length > 0, "Roster is empty.");
            failures += Require(Roster.Strikers.Length > 0, "No outfield players to pick from.");
            failures += Require(Roster.Strikers.Length < Roster.Squad.Length,
                "No goalkeeper is flagged, so the keeper can still be drawn as striker.");
            failures += Require(Roster.Strikers.All(m => !m.IsGoalkeeper),
                "A goalkeeper is in the striker pool.");

            for (int i = 0; i < 500; i++)
            {
                if (!Roster.Random().IsGoalkeeper) continue;
                Debug.LogError("[Verify] Roster.Random returned a goalkeeper.");
                failures++;
                break;
            }

            foreach (var member in Roster.Squad)
            {
                if (Roster.LoadPortrait(member) != null) continue;
                Debug.LogError($"[Verify] Portrait missing for {member.Name} ({member.ResourcePath}).");
                failures++;
            }

            if (failures == 0)
                Debug.Log($"[Verify] Roster: OK ({Roster.Strikers.Length} strikers, " +
                          $"{Roster.Squad.Length - Roster.Strikers.Length} keeper(s) held back).");
            return failures;
        }

        /// <summary>Hebrew is laid out by hand, so pin the visual order down.</summary>
        static int CheckRtl()
        {
            int failures = 0;
            failures += Expect(Rtl.Fix("שאלה 3 מתוך 5"), "5 ךותמ 3 הלאש", "Rtl.Fix keeps numbers in place");
            failures += Expect(Rtl.Fix("כמה זה 7 + 3?"), "?7 + 3 הז המכ", "Rtl.Fix keeps a sum left to right");
            failures += Expect(Rtl.Fix("7 + 3 = ?"), "7 + 3 = ?", "Rtl.Fix leaves a bare sum alone");

            string wrapped = Rtl.Wrap("אחת שתיים שלוש ארבע", 10);
            string[] lines = wrapped.Split('\n');
            failures += Require(lines.Length == 2, $"Rtl.Wrap should make 2 lines, made {lines.Length}.");
            if (lines.Length == 2)
            {
                failures += Expect(lines[0], Rtl.Fix("אחת שתיים"), "Rtl.Wrap keeps the first logical line on top");
                failures += Expect(lines[1], Rtl.Fix("שלוש ארבע"), "Rtl.Wrap second line");
            }

            if (failures == 0) Debug.Log("[Verify] Rtl: OK.");
            return failures;
        }

        static int Expect(string actual, string expected, string what)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[Verify] {what}: got '{actual}', expected '{expected}'.");
            return 1;
        }

        /// <summary>The star rules documented in the README.</summary>
        static int CheckStars()
        {
            int failures = 0;
            failures += Require(MatchManager.StarsFor(4, 1, 4) == 3, "A win with 4 right answers should earn 3 stars.");
            failures += Require(MatchManager.StarsFor(3, 2, 3) == 2, "A win with 3 right answers should earn 2 stars.");
            failures += Require(MatchManager.StarsFor(2, 2, 3) == 1, "A draw should earn 1 star.");
            failures += Require(MatchManager.StarsFor(2, 3, 2) == 1, "A loss with 2 right answers should earn 1 star.");
            failures += Require(MatchManager.StarsFor(0, 5, 0) == 0, "A loss with no right answers should earn 0 stars.");
            if (failures == 0) Debug.Log("[Verify] Stars: OK.");
            return failures;
        }

        /// <summary>
        /// The shared look: Fredoka on every piece of text, 9-sliced rounded
        /// sprites, press feedback on every button, and the portal bridge plugin.
        /// </summary>
        static int CheckLook()
        {
            int failures = 0;

            var texts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include);
            foreach (var text in texts.Where(t => t.font == null || !t.font.name.StartsWith("Fredoka")))
                failures += Require(false, $"Text '{text.name}' uses {(text.font != null ? text.font.name : "no font")}, not Fredoka.");
            var meshes = UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include);
            foreach (var mesh in meshes.Where(t => t.font == null || !t.font.name.StartsWith("Fredoka")))
                failures += Require(false, $"TextMesh '{mesh.name}' is not Fredoka.");
            foreach (var text in texts.Where(t => t.horizontalOverflow == HorizontalWrapMode.Wrap))
                failures += Require(false, $"Text '{text.name}' wraps by itself; Hebrew must be pre-wrapped with Rtl.Wrap.");

            var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
            failures += Require(buttons.Length >= 5, $"Expected the 4 level buttons and Kick Off, found {buttons.Length} buttons.");
            foreach (var button in buttons.Where(b => b.GetComponent<PressFeedback>() == null))
                failures += Require(false, $"Button '{button.name}' has no PressFeedback.");

            var images = UnityEngine.Object.FindObjectsByType<Image>(FindObjectsInactive.Include);
            foreach (var name in new[] { "ScoreBar", "ProblemCard", "Card" })
            {
                var panel = images.FirstOrDefault(i => i.name == name);
                failures += Require(panel != null && panel.sprite != null && panel.type == Image.Type.Sliced
                                    && panel.sprite.border.x > 0f, $"{name} is not a 9-sliced rounded panel.");
            }

            var hud = UnityEngine.Object.FindAnyObjectByType<HudController>();
            failures += Require(hud != null && hud.GetComponentInChildren<ResultPop>(true) != null, "No result word pop in the HUD.");
            failures += Require(hud != null && hud.GetComponentInChildren<OverlayPop>(true) != null, "The overlay does not pop in.");
            failures += Require(UnityEngine.Object.FindAnyObjectByType<MatchAudio>() != null, "No MatchAudio to play the press tick.");

            var plugin = AssetImporter.GetAtPath("Assets/Plugins/WebGL/PortalBridge.jslib") as PluginImporter;
            failures += Require(plugin != null && plugin.GetCompatibleWithPlatform(BuildTarget.WebGL),
                "PortalBridge.jslib is missing or not enabled for WebGL.");
            failures += Require(MatchManager.Slug == "math-strikers", "The portal slug must be math-strikers.");

            if (failures == 0)
                Debug.Log($"[Verify] Look: OK ({texts.Length} texts in Fredoka, {buttons.Length} buttons with press feedback, bridge plugin present).");
            return failures;
        }

        /// <summary>Re-computes the printed expression to prove the stated answer is right.</summary>
        static bool VerifyArithmetic(MathProblem problem)
        {
            string[] parts = problem.Text.Split(' ');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out int left) || !int.TryParse(parts[2], out int right)) return false;

            return parts[1] switch
            {
                "+" => left + right == problem.Answer,
                "-" => left - right == problem.Answer,
                "×" => left * right == problem.Answer,
                "÷" => right != 0 && left % right == 0 && left / right == problem.Answer,
                _ => false
            };
        }

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var manager = UnityEngine.Object.FindFirstObjectByType<MatchManager>();
            if (manager == null)
            {
                Debug.LogError("[Verify] No MatchManager in the scene.");
                return failures + 1;
            }

            failures += Require(UnityEngine.Object.FindFirstObjectByType<BallController>() != null, "Ball is missing.");
            failures += Require(UnityEngine.Object.FindFirstObjectByType<GoalkeeperController>() != null, "Goalkeeper is missing.");
            failures += Require(UnityEngine.Object.FindFirstObjectByType<HudController>() != null, "HUD is missing.");
            failures += Require(UnityEngine.Object.FindFirstObjectByType<Camera>() != null, "No camera in the scene.");
            failures += Require(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");

            var zones = UnityEngine.Object.FindObjectsByType<TargetZone>(FindObjectsSortMode.None);
            failures += Require(zones.Length == ProblemGenerator.OptionCount,
                $"Expected {ProblemGenerator.OptionCount} target zones, found {zones.Length}.");

            foreach (var zone in zones)
            {
                failures += Require(zone.GetComponent<Collider>() != null, $"{zone.name} has no collider to click.");
                failures += Require(zone.AimPoint != zone.transform.position, $"{zone.name} has no aim point offset.");
            }

            var hudObject = UnityEngine.Object.FindFirstObjectByType<HudController>();
            if (hudObject != null)
            {
                failures += Require(hudObject.StartButton != null, "HUD start button is not wired.");
                var texts = hudObject.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 10, $"HUD looks under-built ({texts.Length} text elements).");
                failures += Require(texts.All(t => t.font != null), "A HUD text element has no font.");
            }

            var ball = UnityEngine.Object.FindFirstObjectByType<BallController>();
            if (ball != null)
            {
                var body = ball.GetComponent<Rigidbody>();
                failures += Require(body != null, "Ball has no Rigidbody.");
                failures += Require(ball.GetComponent<Collider>() != null, "Ball has no collider.");
            }

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
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
        [MenuItem("Math Strikers/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            // The HUD is authored against a 1920x1080 reference and the camera is
            // framed for 16:9, so the canvas has to share that aspect or the match
            // gets letterboxed against its own layout.
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.productName = "Math Strikers";
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

        [MenuItem("Math Strikers/Build macOS Player")]
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
