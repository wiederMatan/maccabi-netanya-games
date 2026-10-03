using System;
using System.Linq;
using PenaltyDuel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PenaltyDuel.EditorTools
{
    /// <summary>
    /// Headless checks that the shootout rules hold and the built scene is actually
    /// wired up, plus a one-command player build. Everything here
    /// is runnable from CI or the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = "Assets/Scenes/Match.unity";
        const string BuildPath = "Builds/PenaltyDuel.app";

        [MenuItem("Penalty Duel/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckShootoutRules();
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

        /// <summary>
        /// Plays scripted shootouts through <see cref="Shootout"/> and checks the
        /// turn order, the early finish, sudden death and the save rule.
        /// </summary>
        static int CheckShootoutRules()
        {
            int failures = 0;

            // Turn order: player 1, player 2, player 1 ...
            var order = new Shootout();
            for (int i = 0; i < 6; i++)
            {
                failures += Require(order.Shooter == i % 2, $"Kick {i + 1} should be taken by player {i % 2 + 1}.");
                failures += Require(order.Round == i / 2, $"Kick {i + 1} should be in round {i / 2 + 1}.");
                order.Record(true);
            }

            // Everyone scores five: level, so sudden death and no winner yet.
            var level = Play(true, true, true, true, true, true, true, true, true, true);
            failures += Require(level.Winner == -1, "5-5 after regulation should not be decided.");
            failures += Require(level.SuddenDeath, "5-5 after regulation should go to sudden death.");

            // Sudden death: player 1 scores, player 2 misses -> player 1.
            level.Record(true);
            failures += Require(level.Winner == -1, "Sudden death is not over halfway through a round.");
            level.Record(false);
            failures += Require(level.Winner == 0, "6-5 after a full sudden-death round should go to player 1.");
            level.Record(true);
            failures += Require(level.Kicks(0).Count == 6, "Kicks after the winner is known should be ignored.");

            // Early finish: 3-0 after three rounds each, player 2 cannot catch up
            // with two kicks left.
            var early = Play(true, false, true, false, true, false);
            failures += Require(early.Winner == 0, "3-0 with two kicks left should be over.");

            // ...but 3-1 after three each is not over: player 2 could still level.
            var alive = Play(true, false, true, true, true, false);
            failures += Require(alive.Winner == -1, "3-1 with two kicks left should still be alive.");

            // Player 2 wins it with the last regulation kick.
            var late = Play(true, true, true, true, true, true, true, true, false, true);
            failures += Require(late.Winner == 1, "4-5 after five each should go to player 2.");
            failures += Require(late.Goals(0) == 4 && late.Goals(1) == 5, "Goal count is wrong.");

            // Save rule: the exact spot always saves, the wrong side never does,
            // the right side at the wrong height half the time.
            for (int shot = 0; shot < Shootout.Spots; shot++)
            {
                for (int dive = 0; dive < Shootout.Spots; dive++)
                {
                    bool sameSide = Shootout.Column(shot) == Shootout.Column(dive);
                    bool lowRoll = Shootout.IsSave(shot, dive, 0.1f);
                    bool highRoll = Shootout.IsSave(shot, dive, 0.9f);

                    if (shot == dive) failures += Require(lowRoll && highRoll, $"Diving to spot {dive + 1} should always save a shot there.");
                    else if (!sameSide) failures += Require(!lowRoll && !highRoll, $"Diving to {dive + 1} should never save a shot at {shot + 1}.");
                    else failures += Require(lowRoll && !highRoll, $"Same side, other height ({shot + 1}/{dive + 1}) should be a coin flip.");
                }
            }

            // The six spots are three columns over two rows, top row first.
            failures += Require(Shootout.IsHigh(0) && Shootout.IsHigh(2) && !Shootout.IsHigh(3) && !Shootout.IsHigh(5),
                "Spots 1-3 should be the top row and 4-6 the bottom.");

            // A thousand random shootouts all finish, and never past sensible length.
            var random = new System.Random(5);
            for (int game = 0; game < 1000; game++)
            {
                var shootout = new Shootout();
                int kicks = 0;
                while (shootout.Winner < 0 && kicks < 400)
                {
                    shootout.Record(random.NextDouble() < 0.7);
                    kicks++;
                }
                if (shootout.Winner >= 0) continue;
                Debug.LogError("[Verify] A random shootout never finished.");
                failures++;
                break;
            }

            if (failures == 0) Debug.Log("[Verify] Shootout rules: OK.");
            return failures;
        }

        static Shootout Play(params bool[] kicks)
        {
            var shootout = new Shootout();
            foreach (bool scored in kicks) shootout.Record(scored);
            return shootout;
        }

        /// <summary>The keeper stays in goal - he must never be drawn as a shooter.</summary>
        static int CheckRoster()
        {
            int failures = 0;

            failures += Require(Roster.Squad.Length > 0, "Roster is empty.");
            failures += Require(Roster.Strikers.Length > 1, "Need two outfield players to give each side one.");
            failures += Require(Roster.Strikers.All(m => !m.IsGoalkeeper), "A goalkeeper is in the striker pool.");

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
                Debug.Log($"[Verify] Roster: OK ({Roster.Strikers.Length} shooters).");
            return failures;
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

            var wiring = new SerializedObject(manager);
            foreach (var field in new[] { "ball", "keeper", "hud", "striker", "framer", "audio_" })
                failures += Require(wiring.FindProperty(field)?.objectReferenceValue != null,
                    $"MatchManager.{field} is not wired.");

            failures += Require(UnityEngine.Object.FindFirstObjectByType<Camera>() != null, "No camera in the scene.");
            failures += Require(UnityEngine.Object.FindFirstObjectByType<CameraFramer>() != null, "Camera has no CameraFramer.");
            failures += Require(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");
            failures += Require(UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                "No EventSystem, so the buttons cannot be pressed.");

            // The web page mutes the game with SendMessage("MatchAudio", "SetMuted", ...).
            var audioObject = GameObject.Find("MatchAudio");
            failures += Require(audioObject != null && audioObject.GetComponent<MatchAudio>() != null,
                "No MatchAudio object for the page's sound button to reach.");
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip {clip} is missing.");

            // Six spots, each with its own index, a tap area and an aim point in the goal.
            var zones = wiring.FindProperty("zones");
            failures += Require(zones != null && zones.arraySize == Shootout.Spots,
                $"MatchManager should hold {Shootout.Spots} spots.");
            var found = UnityEngine.Object.FindObjectsByType<TargetZone>(FindObjectsSortMode.None);
            failures += Require(found.Length == Shootout.Spots, $"Expected {Shootout.Spots} spots, found {found.Length}.");
            if (zones != null)
            {
                for (int i = 0; i < zones.arraySize; i++)
                {
                    var zone = zones.GetArrayElementAtIndex(i).objectReferenceValue as TargetZone;
                    if (zone == null) { failures += Require(false, $"Spot {i + 1} is not wired."); continue; }
                    failures += Require(zone.Index == i, $"{zone.name} has index {zone.Index}, expected {i}.");
                    failures += Require(zone.GetComponent<Collider>() != null, $"{zone.name} has no collider to tap.");
                    var aim = zone.AimPoint;
                    failures += Require(Mathf.Abs(aim.x) < 3.4f && aim.y > 0.2f && aim.y < 2.2f && aim.z > 12f,
                        $"{zone.name} aims outside the goal ({aim}).");
                    bool left = aim.x < -1f, right = aim.x > 1f;
                    int column = Shootout.Column(i);
                    failures += Require(column == 0 ? left : column == 2 ? right : !left && !right,
                        $"{zone.name} aims at the wrong side of the goal.");
                    failures += Require(Shootout.IsHigh(i) == aim.y > 1.2f, $"{zone.name} aims at the wrong height.");
                }
            }

            // A tap on each spot, seen from where the aim camera stands, has to land
            // on that spot's tap area and no other.
            Physics.SyncTransforms();
            var eye = new Vector3(0f, 1.6f, 2f);
            foreach (var zone in found)
            {
                var hits = Physics.RaycastAll(new Ray(eye, zone.transform.position - eye), 100f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                var first = hits.OrderBy(h => h.distance).Select(h => h.collider.GetComponent<TargetZone>())
                    .FirstOrDefault(z => z != null);
                failures += Require(first == zone,
                    $"A tap on {zone.name} lands on {(first != null ? first.name : "nothing")} ({hits.Length} hits).");
            }

            var keeper = UnityEngine.Object.FindFirstObjectByType<GoalkeeperController>();
            failures += Require(keeper != null, "Goalkeeper is missing.");
            var keeperAnimator = keeper != null ? keeper.GetComponentInChildren<Animator>() : null;
            var keeperController = keeperAnimator != null
                ? keeperAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
                : null;
            failures += Require(keeperController != null && keeperController.parameters.Any(p => p.name == "Mirror")
                                && keeperController.parameters.Any(p => p.name == "Dive"),
                "Keeper animator has no Dive trigger and Mirror switch, so he can only dive one way.");

            var strikerAnimator = wiring.FindProperty("striker")?.objectReferenceValue is Transform strikerTransform
                ? strikerTransform.GetComponentInChildren<Animator>()
                : null;
            var strikerController = strikerAnimator != null
                ? strikerAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
                : null;
            failures += Require(strikerController != null
                                && strikerController.layers[0].stateMachine.states.Any(s => s.state.name == "Kick")
                                && strikerController.parameters.Any(p => p.name == "Run"),
                "Striker animator is missing its Run / Kick states.");

            var hud = UnityEngine.Object.FindFirstObjectByType<HudController>();
            if (hud != null)
            {
                failures += Require(hud.PrimaryButton != null && hud.SecondaryButton != null,
                    "Menu buttons are not wired.");
                failures += Require(hud.PassButton != null, "Pass-the-phone button is not wired.");
                failures += Require(hud.RowCount == 2, "Scoreboard should have a row per player.");
                failures += Require(hud.MarkSlots == Shootout.Regulation, "Scoreboard rows should show five kicks.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.All(t => t.font != null), "A HUD text element has no font.");

                // The pass screen has to hide the shooter's pick completely.
                var hudWiring = new SerializedObject(hud);
                var pass = hudWiring.FindProperty("passPanel")?.objectReferenceValue as GameObject;
                var passImage = pass != null ? pass.GetComponent<Image>() : null;
                failures += Require(passImage != null && passImage.color.a >= 0.999f,
                    "The pass-the-phone screen is not fully opaque.");
                failures += Require(pass != null && pass.transform.GetSiblingIndex() == pass.transform.parent.childCount - 1,
                    "The pass-the-phone screen must be drawn on top of the rest of the HUD.");
                failures += Require(pass != null && !pass.activeSelf, "The pass-the-phone screen should start hidden.");

                foreach (var field in new[] { "canvasRect", "framer", "scoreboard", "promptCard", "ring", "disc", "tick", "cross" })
                    failures += Require(hudWiring.FindProperty(field)?.objectReferenceValue != null,
                        $"HudController.{field} is not wired.");
            }
            else
            {
                failures += Require(false, "HUD is missing.");
            }

            var ball = UnityEngine.Object.FindFirstObjectByType<BallController>();
            failures += Require(ball != null, "Ball is missing.");
            if (ball != null)
            {
                failures += Require(ball.GetComponent<Rigidbody>() != null, "Ball has no Rigidbody.");
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
        [MenuItem("Penalty Duel/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            // Only the starting canvas size: the page resizes the canvas to the
            // window, and the HUD and camera lay themselves out to suit.
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.productName = "Penalty Duel";
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

        [MenuItem("Penalty Duel/Build macOS Player")]
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
