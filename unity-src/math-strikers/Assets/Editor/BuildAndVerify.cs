using System;
using System.Linq;
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
            PlayerSettings.WebGL.template = "APPLICATION:Default";
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
