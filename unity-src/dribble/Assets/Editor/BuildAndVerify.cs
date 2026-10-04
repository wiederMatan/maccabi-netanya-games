using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dribble;
using MaccabiShared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Dribble.EditorTools
{
    /// <summary>
    /// Headless checks that the course generator is fair and the built scene is
    /// actually wired up, plus a one-command WebGL build. Everything here is
    /// runnable from CI or the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = SceneBuilder.ScenePath;
        const string TemplatePath = "Assets/WebGLTemplates/MaccabiNetanya/index.html";

        [MenuItem("Dribble/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckRtl();
            failures += CheckProgression();
            failures += CheckGenerator();
            failures += CheckSceneWiring();
            failures += CheckCourseSimulation();
            failures += CheckAudioAndPage();
            failures += CheckPortalBridge();

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
        /// Unity's Text cannot lay out Hebrew, so Rtl reorders it. Spot-check the
        /// cases the game relies on: words reversed, numbers and sums kept the
        /// right way round, and wrapped paragraphs keeping their first line on top.
        /// </summary>
        static int CheckRtl()
        {
            int failures = 0;
            failures += Expect(Rtl.Fix("שאלה 3 מתוך 5"), "5 ךותמ 3 הלאש");
            failures += Expect(Rtl.Fix("כמה זה 7 + 3?"), "?7 + 3 הז המכ");
            failures += Expect(Rtl.Fix("100 מ'!"), "!'מ 100");
            failures += Expect(Rtl.Fix("נקודות: 193, שיא: 210"), "210 :איש ,193 :תודוקנ");
            failures += Expect(Rtl.Fix("Kick 42"), "Kick 42");

            string wrapped = Rtl.Wrap("אחת שתיים שלוש ארבע", 10);
            var lines = wrapped.Split('\n');
            failures += Require(lines.Length == 2 && lines[0] == Rtl.Fix("אחת שתיים") && lines[1] == Rtl.Fix("שלוש ארבע"),
                $"Rtl.Wrap put the lines in the wrong order: '{wrapped.Replace("\n", " | ")}'.");

            // Every Hebrew string the game shows fits Fredoka's glyphs.
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Fredoka-SemiBold.ttf");
            failures += Require(font != null, "Fredoka is missing from Assets/Fonts.");
            if (font != null)
            {
                foreach (char c in "אבגדהוזחטיכךלמםנןסעפףצץקרשת")
                    failures += Require(font.HasCharacter(c), $"Fredoka has no glyph for '{c}'.");
            }

            if (failures == 0) Debug.Log("[Verify] Rtl: OK.");
            return failures;
        }

        static int Expect(string actual, string expected) =>
            Require(actual == expected, $"Rtl gave '{actual}', expected '{expected}'.");

        /// <summary>
        /// The stars and bests reach the website: the WebGL plugin ships, and the
        /// game calls each bridge entry point at the right moment.
        /// </summary>
        static int CheckPortalBridge()
        {
            int failures = 0;
            const string plugin = "Assets/Plugins/WebGL/PortalBridge.jslib";
            var importer = AssetImporter.GetAtPath(plugin) as PluginImporter;
            failures += Require(importer != null && importer.GetCompatibleWithPlatform(BuildTarget.WebGL),
                "PortalBridge.jslib is missing or not enabled for WebGL.");

            string game = File.ReadAllText("Assets/Scripts/Runtime/DribbleGame.cs");
            string feedback = File.ReadAllText("Assets/Scripts/Runtime/PressFeedback.cs");
            failures += Require(game.Contains("PortalBridge.MarkPlayed(Slug)") && game.Contains("const string Slug = \"dribble\""),
                "A run start does not mark dribble as played.");
            failures += Require(game.Contains("PortalBridge.AddStars(earned)"), "Finished runs do not add stars.");
            failures += Require(game.Contains("PortalBridge.ReportBest(Slug, score)"), "Finished runs do not report the best.");
            failures += Require(feedback.Contains("PortalBridge.Haptic(10)"), "Button presses do not buzz.");

            int last = 0;
            for (int score = 0; score <= 3000; score += 5)
            {
                int stars = Progression.StarsFor(score);
                if (stars < 1 || stars > 3 || stars < last)
                {
                    failures += Require(false, $"{stars} stars for {score} points.");
                    break;
                }
                last = stars;
            }
            failures += Require(Progression.StarsFor(0) == 1 && Progression.StarsFor(Progression.TwoStarScore) == 2 &&
                                Progression.StarsFor(Progression.ThreeStarScore) == 3,
                $"Star thresholds do not run 1 to 3 (2 stars at {Progression.TwoStarScore}, 3 at {Progression.ThreeStarScore}).");
            failures += Require(Progression.TwoStarScore == 250 && Progression.ThreeStarScore == 650,
                "Star thresholds changed - update this check and the README together.");

            if (failures == 0) Debug.Log("[Verify] Portal bridge and star awards: OK.");
            return failures;
        }

        /// <summary>
        /// The one progression: starts at a brisk jog, only ever speeds up, reaches
        /// top speed within 75 s, and the pitch gets busier as it does - from one
        /// blocker at a time to mostly two - while rows stay far enough apart to dodge.
        /// </summary>
        static int CheckProgression()
        {
            int failures = 0;
            failures += Require(Mathf.Abs(Progression.SpeedAt(0f) - 5.5f) < 0.01f, "The run does not start at 5.5 m/s.");
            failures += Require(Progression.SpeedAt(75f) >= Progression.MaxSpeed - 0.001f, "Top speed is not reached within 75 s.");
            failures += Require(Progression.MaxSpeed >= 14.5f, "Top speed is below 15 m/s.");

            float lastSpeed = 0f, lastGap = float.MaxValue, lastDouble = -1f;
            for (float t = 0f; t <= 120f; t += 0.5f)
            {
                float speed = Progression.SpeedAt(t);
                var pace = Progression.PaceAt(speed);
                if (speed < lastSpeed || pace.RowGapSeconds > lastGap + 1e-5f || pace.DoubleBlockChance < lastDouble - 1e-5f)
                {
                    failures += Require(false, $"The progression goes backwards at {t}s.");
                    break;
                }
                failures += Require(pace.RowGapSeconds >= 1.2f, $"Rows are too close together to dodge at {t}s.");
                lastSpeed = speed; lastGap = pace.RowGapSeconds; lastDouble = pace.DoubleBlockChance;
            }

            var opening = Progression.PaceAt(Progression.StartSpeed);
            var top = Progression.PaceAt(Progression.MaxSpeed);
            failures += Require(opening.DoubleBlockChance == 0f, "The opening should only ever block one lane at a time.");
            failures += Require(top.DoubleBlockChance >= 0.5f && top.RowGapSeconds <= 1.5f, "Top speed is not as busy as the old Hard level.");

            if (failures == 0) Debug.Log($"[Verify] Progression: OK ({Progression.StartSpeed} to {Progression.MaxSpeed} m/s in {(Progression.MaxSpeed - Progression.StartSpeed) / Progression.Acceleration:0} s).");
            return failures;
        }

        /// <summary>
        /// Thousands of rows across the progression: a lane is always left open,
        /// stars never sit on a blocker, and the pitch blocks about as often as the
        /// progression promises at each speed.
        /// </summary>
        static int CheckGenerator()
        {
            int failures = 0;
            const int rows = 5000;
            float[] speeds = { Progression.StartSpeed, 8f, 11f, Progression.MaxSpeed };

            foreach (float speed in speeds)
            {
                var pace = Progression.PaceAt(speed);
                var random = new System.Random(42);
                int doubles = 0, defenders = 0, blockers = 0;

                for (int i = 0; i < rows; i++)
                {
                    var row = CourseGenerator.Next(pace, random);

                    if (row.Lanes == null || row.Lanes.Length != CourseGenerator.LaneCount)
                    {
                        Debug.LogError($"[Verify] {speed} m/s: a row does not have {CourseGenerator.LaneCount} lanes.");
                        failures++;
                        break;
                    }

                    int blocked = row.Lanes.Count(b => b != Blocker.None);
                    if (blocked == 0 || blocked >= CourseGenerator.LaneCount)
                    {
                        Debug.LogError($"[Verify] {speed} m/s: a row blocks {blocked} lanes.");
                        failures++;
                        break;
                    }

                    if (row.StarLane >= 0 && row.Lanes[row.StarLane] != Blocker.None)
                    {
                        Debug.LogError($"[Verify] {speed} m/s: stars were put on a blocker.");
                        failures++;
                        break;
                    }

                    if (blocked == 2) doubles++;
                    blockers += blocked;
                    defenders += row.Lanes.Count(b => b == Blocker.Defender);
                }

                float doubleRate = doubles / (float)rows;
                float defenderRate = defenders / (float)Mathf.Max(1, blockers);
                failures += Require(Mathf.Abs(doubleRate - pace.DoubleBlockChance) < 0.03f,
                    $"{speed} m/s: {doubleRate:P0} double rows, expected about {pace.DoubleBlockChance:P0}.");
                failures += Require(Mathf.Abs(defenderRate - pace.DefenderShare) < 0.03f,
                    $"{speed} m/s: {defenderRate:P0} defenders, expected about {pace.DefenderShare:P0}.");
            }

            if (failures == 0) Debug.Log($"[Verify] Course generator: OK ({rows * speeds.Length} rows checked).");
            return failures;
        }

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var game = Object.FindFirstObjectByType<DribbleGame>();
            if (game == null)
            {
                Debug.LogError("[Verify] No DribbleGame in the scene.");
                return failures + 1;
            }

            var serialized = new SerializedObject(game);
            foreach (var field in new[] { "course", "runner", "hud", "audio_", "framer" })
                failures += Require(serialized.FindProperty(field).objectReferenceValue != null,
                    $"DribbleGame.{field} is not wired.");

            failures += Require(Object.FindFirstObjectByType<Camera>() != null, "No camera in the scene.");
            failures += Require(Object.FindFirstObjectByType<CameraFramer>() != null, "Camera has no CameraFramer.");
            failures += Require(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");
            failures += Require(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                "No EventSystem, so the overlay buttons cannot be pressed.");

            // The page mutes the game with SendMessage("DribbleAudio", ...), which
            // finds the object by name.
            var audioObject = GameObject.Find("DribbleAudio");
            failures += Require(audioObject != null && audioObject.GetComponent<DribbleAudio>() != null,
                "No 'DribbleAudio' object for the page's mute button to reach.");

            var course = Object.FindFirstObjectByType<Course>();
            if (course != null)
            {
                var courseObject = new SerializedObject(course);
                failures += Require(courseObject.FindProperty("segments").arraySize == SceneBuilder.SegmentCount,
                    "Course does not have its pitch segments.");
                failures += CheckPool(courseObject, "defenders", SceneBuilder.DefenderPool, ItemKind.Defender);
                failures += CheckPool(courseObject, "cones", SceneBuilder.ConePool, ItemKind.Cone);
                failures += CheckPool(courseObject, "stars", SceneBuilder.StarPool, ItemKind.Star);

                var defenders = Object.FindObjectsByType<PitchItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(i => i.Kind == ItemKind.Defender).ToArray();
                foreach (var defender in defenders)
                {
                    var animator = defender.GetComponent<Animator>();
                    failures += Require(animator != null && animator.runtimeAnimatorController != null,
                        $"{defender.name} has no animator controller.");
                    failures += Require(defender.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0,
                        $"{defender.name} has no body.");
                }

                // Pitch segments laid end to end with no gap.
                var zs = Enumerable.Range(0, SceneBuilder.SegmentCount)
                    .Select(i => ((Transform)courseObject.FindProperty("segments").GetArrayElementAtIndex(i).objectReferenceValue).localPosition.z)
                    .OrderBy(z => z).ToArray();
                for (int i = 1; i < zs.Length; i++)
                    failures += Require(Mathf.Approximately(zs[i] - zs[i - 1], Course.SegmentLength),
                        "Pitch segments are not laid end to end.");
            }
            else
            {
                failures += Require(false, "No Course in the scene.");
            }

            var runner = Object.FindFirstObjectByType<Runner>();
            if (runner != null)
            {
                failures += Require(runner.Body != null, "Runner has no body.");
                var animator = runner.Body != null ? runner.Body.GetComponent<Animator>() : null;
                failures += Require(animator != null && animator.runtimeAnimatorController != null,
                    "Runner has no animator controller.");
                if (animator != null && animator.runtimeAnimatorController is UnityEditor.Animations.AnimatorController controller)
                {
                    // Runner cross-fades to these states by name.
                    var states = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
                    foreach (var state in new[] { "Run", "Idle", "Fall" })
                        failures += Require(states.Contains(state), $"Runner animator has no '{state}' state.");
                }
                failures += Require(runner.Ball != null && runner.Ball.GetComponent<Rigidbody>() != null,
                    "Runner's ball has no Rigidbody.");
                failures += Require(runner.Ball != null && runner.Ball.GetComponent<Collider>() != null,
                    "Runner's ball has no collider.");
            }
            else
            {
                failures += Require(false, "No Runner in the scene.");
            }

            var hud = Object.FindFirstObjectByType<HudController>();
            if (hud != null)
            {
                failures += Require(hud.StartButton != null, "HUD start button is not wired.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 8, $"HUD looks under-built ({texts.Length} text elements).");
                foreach (var text in texts)
                {
                    failures += Require(text.font != null && text.font.name.Contains("Fredoka"),
                        $"{text.name} does not use Fredoka (it has {(text.font != null ? text.font.name : "no font")}).");
                    // Rtl wraps Hebrew itself; Unity's wrapping would undo it.
                    failures += Require(text.horizontalOverflow == HorizontalWrapMode.Overflow,
                        $"{text.name} wraps its own text.");
                }

                var hudObject = new SerializedObject(hud);
                foreach (var field in new[] { "scaler", "counters", "scoreText", "distanceText", "starsText", "toastText",
                             "hintText", "overlay", "dim", "card", "titleText", "badge", "starRow", "bodyText",
                             "startButton", "startButtonLabel" })
                    failures += Require(hudObject.FindProperty(field).objectReferenceValue != null, $"HUD.{field} is not wired.");
                var fills = hudObject.FindProperty("starFills");
                failures += Require(fills.arraySize == 3, "The end card does not have 3 star slots.");

                // Every button squashes, ticks and buzzes when pressed.
                foreach (var button in hud.GetComponentsInChildren<Button>(true))
                    failures += Require(button.GetComponent<PressFeedback>() != null, $"{button.name} has no PressFeedback.");

                // Panels and buttons are 9-sliced rounded sprites.
                foreach (var image in hud.GetComponentsInChildren<Image>(true).Where(i => i.type == Image.Type.Sliced))
                    failures += Require(image.sprite != null && image.sprite.border != Vector4.zero,
                        $"{image.name} is sliced but its sprite has no border.");
            }
            else
            {
                failures += Require(false, "HUD is missing.");
            }

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
            return failures;
        }

        static int CheckPool(SerializedObject course, string field, int expected, ItemKind kind)
        {
            var pool = course.FindProperty(field);
            int failures = Require(pool.arraySize == expected, $"Course.{field} has {pool.arraySize} items, expected {expected}.");
            for (int i = 0; i < pool.arraySize; i++)
            {
                var item = pool.GetArrayElementAtIndex(i).objectReferenceValue as PitchItem;
                if (item != null && item.Kind == kind) continue;
                failures += Require(false, $"Course.{field}[{i}] is missing or the wrong kind.");
                break;
            }
            return failures;
        }

        /// <summary>
        /// Drives the course for six minutes of running, as the game would - well
        /// past top speed: the pools never run dry, items are recycled, and no row
        /// ever walls off all three lanes.
        /// </summary>
        static int CheckCourseSimulation()
        {
            int failures = 0;
            var course = Object.FindFirstObjectByType<Course>();
            if (course == null) return 1;

            course.Begin(Progression.StartSpeed);
            const float dt = 1f / 30f;
            float time = 0f;
            int peak = 0;
            for (int step = 0; step < 30 * 360; step++)
            {
                time += dt;
                float speed = Progression.SpeedAt(time);
                course.Advance(speed * dt, speed);
                peak = Mathf.Max(peak, course.Active.Count);

                if (step % 15 != 0) continue;
                var blockersByRow = course.Active
                    .Where(i => i.Kind != ItemKind.Star && i.gameObject.activeSelf)
                    .GroupBy(i => Mathf.RoundToInt(i.transform.localPosition.z * 10f));
                if (blockersByRow.Any(g => g.Select(i => i.Lane).Distinct().Count() >= CourseGenerator.LaneCount))
                {
                    Debug.LogError($"[Verify] A row walled off every lane at {time:0}s.");
                    failures++;
                    break;
                }
            }

            failures += Require(course.Shortfalls == 0,
                $"The pools ran dry {course.Shortfalls} time(s) - make them bigger.");
            if (failures == 0) Debug.Log($"[Verify] Course simulation: OK (6 minutes, {peak} items live at most).");
            return failures;
        }

        /// <summary>The stadium recordings ship, and the page's mute button talks to the right object.</summary>
        static int CheckAudioAndPage()
        {
            int failures = 0;
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip '{clip}' is missing.");

            // The club's music: every clip loads, the anthem and drums loop and the
            // stings do not, and DribbleAudio actually plays each one it lists.
            // MusicStar ships but is deliberately unused: the end card's stars keep
            // their rising chime, so the two never double up.
            string audioCode = File.ReadAllText("Assets/Scripts/Runtime/DribbleAudio.cs");
            foreach (var (clip, loop) in DribbleAudio.Music)
            {
                failures += Require(DribbleAudio.Load("Music/" + clip) != null, $"Music clip '{clip}' is missing.");
                bool shouldLoop = clip == "MusicAnthem" || clip == "MusicDrums";
                failures += Require(loop == shouldLoop, $"Music clip '{clip}' should {(shouldLoop ? "" : "not ")}loop.");
                if (clip != "MusicStar")
                    failures += Require(audioCode.Contains($"Load(\"Music/{clip}\")"), $"Music clip '{clip}' is not wired into DribbleAudio.");
            }
            failures += Require(audioCode.Contains("anthemSource = LoopSource(") && audioCode.Contains("drumsSource = LoopSource("),
                "The anthem and drums are not on looping sources.");

            string page = File.Exists(TemplatePath) ? File.ReadAllText(TemplatePath) : "";
            failures += Require(page.Contains("SendMessage(\"DribbleAudio\", \"SetMuted\""),
                "The web template does not mute through DribbleAudio.SetMuted.");
            failures += Require(page.Contains("../../app.js"), "The web template does not load the shared app.js.");
            failures += Require(page.Contains("manifest.webmanifest"), "The web template lost its PWA tags.");

            if (failures == 0) Debug.Log("[Verify] Audio, music and web page: OK.");
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
        [MenuItem("Dribble/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/WebGL";

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:MaccabiNetanya";

            // The page resizes the canvas to the window, and the camera and HUD adapt
            // to any shape, so this is only the size before the first resize.
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.productName = "Dribble";
            PlayerSettings.companyName = "Maccabi Netanya Games";
            PlayerSettings.runInBackground = true;
            // The game opens straight into its countdown; a splash would eat it.
            PlayerSettings.SplashScreen.show = false;

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
