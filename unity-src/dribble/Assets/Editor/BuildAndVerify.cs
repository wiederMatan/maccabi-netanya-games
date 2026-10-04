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
            failures += CheckTiers();
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

            foreach (var difficulty in Tiers.All)
            {
                int last = 0;
                for (int score = 0; score <= 2000; score += 5)
                {
                    int stars = Tiers.StarsFor(difficulty, score);
                    if (stars < 1 || stars > 3 || stars < last)
                    {
                        failures += Require(false, $"{difficulty}: {stars} stars for {score} points.");
                        break;
                    }
                    last = stars;
                }
                failures += Require(Tiers.StarsFor(difficulty, 0) == 1 && Tiers.StarsFor(difficulty, 5000) == 3,
                    $"{difficulty}: star thresholds do not span 1 to 3.");
            }

            if (failures == 0) Debug.Log("[Verify] Portal bridge and star awards: OK.");
            return failures;
        }

        /// <summary>Each level is a step up from the one before, and Starter is gentle.</summary>
        static int CheckTiers()
        {
            int failures = 0;
            var all = Tiers.All.Select(Tiers.For).ToArray();

            failures += Require(Tiers.Names.Length == Tiers.All.Length && Tiers.Hints.Length == Tiers.All.Length && Tiers.Ids.Length == Tiers.All.Length,
                "Every level needs a name and a hint.");

            for (int i = 1; i < all.Length; i++)
            {
                failures += Require(all[i].StartSpeed > all[i - 1].StartSpeed, $"{Tiers.All[i]} does not start faster than {Tiers.All[i - 1]}.");
                failures += Require(all[i].Acceleration > all[i - 1].Acceleration, $"{Tiers.All[i]} does not ramp faster than {Tiers.All[i - 1]}.");
                failures += Require(all[i].RowGapSeconds < all[i - 1].RowGapSeconds, $"{Tiers.All[i]} is not busier than {Tiers.All[i - 1]}.");
            }

            foreach (var tier in all)
            {
                failures += Require(tier.SpeedAt(0f) == tier.StartSpeed, "SpeedAt(0) is not the start speed.");
                failures += Require(Mathf.Approximately(tier.SpeedAt(10000f), tier.MaxSpeed), "Speed is not capped.");
                // At top speed, the gap between rows must still leave time for two lane changes.
                failures += Require(tier.RowGapSeconds >= 1.2f, "Rows are too close together to dodge.");
            }

            var starter = Tiers.For(Difficulty.Starter);
            failures += Require(starter.StartSpeed <= 5f, "Starter is too fast for a six year old.");
            failures += Require(starter.DoubleBlockChance == 0f, "Starter should only ever block one lane at a time.");

            if (failures == 0) Debug.Log("[Verify] Levels: OK.");
            return failures;
        }

        /// <summary>
        /// Thousands of rows per level: a lane is always left open, stars never sit
        /// on a blocker, and each level blocks about as often as it promises.
        /// </summary>
        static int CheckGenerator()
        {
            int failures = 0;
            const int rows = 5000;

            foreach (var difficulty in Tiers.All)
            {
                var tier = Tiers.For(difficulty);
                var random = new System.Random(42);
                int doubles = 0, defenders = 0, blockers = 0;

                for (int i = 0; i < rows; i++)
                {
                    var row = CourseGenerator.Next(tier, random);

                    if (row.Lanes == null || row.Lanes.Length != CourseGenerator.LaneCount)
                    {
                        Debug.LogError($"[Verify] {difficulty}: a row does not have {CourseGenerator.LaneCount} lanes.");
                        failures++;
                        break;
                    }

                    int blocked = row.Lanes.Count(b => b != Blocker.None);
                    if (blocked == 0 || blocked >= CourseGenerator.LaneCount)
                    {
                        Debug.LogError($"[Verify] {difficulty}: a row blocks {blocked} lanes.");
                        failures++;
                        break;
                    }

                    if (row.StarLane >= 0 && row.Lanes[row.StarLane] != Blocker.None)
                    {
                        Debug.LogError($"[Verify] {difficulty}: stars were put on a blocker.");
                        failures++;
                        break;
                    }

                    if (blocked == 2) doubles++;
                    blockers += blocked;
                    defenders += row.Lanes.Count(b => b == Blocker.Defender);
                }

                float doubleRate = doubles / (float)rows;
                float defenderRate = defenders / (float)Mathf.Max(1, blockers);
                failures += Require(Mathf.Abs(doubleRate - tier.DoubleBlockChance) < 0.03f,
                    $"{difficulty}: {doubleRate:P0} double rows, expected about {tier.DoubleBlockChance:P0}.");
                failures += Require(Mathf.Abs(defenderRate - tier.DefenderShare) < 0.03f,
                    $"{difficulty}: {defenderRate:P0} defenders, expected about {tier.DefenderShare:P0}.");
            }

            if (failures == 0) Debug.Log($"[Verify] Course generator: OK ({rows * Tiers.All.Length} rows checked).");
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
                failures += Require(hud.DifficultyButtons != null && hud.DifficultyButtons.Length == Tiers.All.Length,
                    "HUD does not have a button for every level.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                failures += Require(texts.Length >= 15, $"HUD looks under-built ({texts.Length} text elements).");
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
                             "hintText", "overlay", "dim", "card", "titleText", "badge", "starRow", "bodyText", "captionText",
                             "startButton", "startButtonLabel", "pickedFace", "pickedEdge", "restingFace", "restingEdge" })
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
        /// Drives the course for several minutes of running on every level, as the
        /// game would: the pools never run dry, items are recycled, and no row ever
        /// walls off all three lanes.
        /// </summary>
        static int CheckCourseSimulation()
        {
            int failures = 0;
            var course = Object.FindFirstObjectByType<Course>();
            if (course == null) return 1;

            foreach (var difficulty in Tiers.All)
            {
                var tier = Tiers.For(difficulty);
                course.Begin(tier, tier.StartSpeed);

                const float dt = 1f / 30f;
                float time = 0f;
                int peak = 0;
                // Six minutes - well past top speed on every level.
                for (int step = 0; step < 30 * 360; step++)
                {
                    time += dt;
                    float speed = tier.SpeedAt(time);
                    course.Advance(speed * dt, speed);
                    peak = Mathf.Max(peak, course.Active.Count);

                    if (step % 15 != 0) continue;
                    var blockersByRow = course.Active
                        .Where(i => i.Kind != ItemKind.Star && i.gameObject.activeSelf)
                        .GroupBy(i => Mathf.RoundToInt(i.transform.localPosition.z * 10f));
                    if (blockersByRow.Any(g => g.Select(i => i.Lane).Distinct().Count() >= CourseGenerator.LaneCount))
                    {
                        Debug.LogError($"[Verify] {difficulty}: a row walled off every lane at {time:0}s.");
                        failures++;
                        break;
                    }
                }

                failures += Require(course.Shortfalls == 0,
                    $"{difficulty}: the pools ran dry {course.Shortfalls} time(s) - make them bigger.");
                Debug.Log($"[Verify] {difficulty}: {peak} items live at most, top speed {tier.MaxSpeed} m/s.");
            }

            if (failures == 0) Debug.Log("[Verify] Course simulation: OK.");
            return failures;
        }

        /// <summary>The stadium recordings ship, and the page's mute button talks to the right object.</summary>
        static int CheckAudioAndPage()
        {
            int failures = 0;
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip '{clip}' is missing.");

            string page = File.Exists(TemplatePath) ? File.ReadAllText(TemplatePath) : "";
            failures += Require(page.Contains("SendMessage(\"DribbleAudio\", \"SetMuted\""),
                "The web template does not mute through DribbleAudio.SetMuted.");
            failures += Require(page.Contains("../../app.js"), "The web template does not load the shared app.js.");
            failures += Require(page.Contains("manifest.webmanifest"), "The web template lost its PWA tags.");

            if (failures == 0) Debug.Log("[Verify] Audio and web page: OK.");
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
