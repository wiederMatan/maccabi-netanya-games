using System.IO;
using System.Linq;
using Headers;
using MaccabiShared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Headers.EditorTools
{
    /// <summary>
    /// Headless checks that the header rules are fair and the built scene is
    /// actually wired up, a few rendered frames to eyeball the framing, and a
    /// one-command WebGL build. Everything here is runnable from the command line.
    /// </summary>
    public static class BuildAndVerify
    {
        const string ScenePath = SceneBuilder.ScenePath;
        const string TemplatePath = "Assets/WebGLTemplates/MaccabiNetanya/index.html";

        [MenuItem("Headers/Verify Scene")]
        public static void VerifyScene()
        {
            int failures = 0;

            failures += CheckRtl();
            failures += CheckProgression();
            failures += CheckCross();
            failures += CheckJudge();
            failures += CheckPlayers();
            failures += CheckSceneWiring();
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
        /// cases the game relies on, and that Fredoka has every Hebrew letter.
        /// </summary>
        static int CheckRtl()
        {
            int failures = 0;
            failures += Expect(Rtl.Fix("קרן מימין!"), "!ןימימ ןרק");
            failures += Expect(Rtl.Fix("נקודות: 75, שיא: 90"), "90 :איש ,75 :תודוקנ");
            failures += Expect(Rtl.Fix("3/10"), "3/10");

            string wrapped = Rtl.Wrap("אחת שתיים שלוש ארבע", 10);
            var lines = wrapped.Split('\n');
            failures += Require(lines.Length == 2 && lines[0] == Rtl.Fix("אחת שתיים") && lines[1] == Rtl.Fix("שלוש ארבע"),
                $"Rtl.Wrap put the lines in the wrong order: '{wrapped.Replace("\n", " | ")}'.");

            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Fredoka-SemiBold.ttf");
            failures += Require(font != null, "Fredoka is missing from Assets/Fonts.");
            if (font != null)
                foreach (char c in "אבגדהוזחטיכךלמםנןסעפףצץקרשת")
                    failures += Require(font.HasCharacter(c), $"Fredoka has no glyph for '{c}'.");

            if (failures == 0) Debug.Log("[Verify] Rtl: OK.");
            return failures;
        }

        static int Expect(string actual, string expected) =>
            Require(actual == expected, $"Rtl gave '{actual}', expected '{expected}'.");

        /// <summary>Each corner of the round is at least as hard as the one before, and the defender arrives on time.</summary>
        static int CheckProgression()
        {
            int failures = 0;
            failures += Require(Progression.CornersPerRound == 10, "A round should be ten corners.");
            var first = Progression.For(0);
            var last = Progression.For(Progression.CornersPerRound - 1);
            failures += Require(!first.Defender && last.Defender, "The defender should join part way through the round.");
            failures += Require(!Progression.For(Progression.FirstDefender - 1).Defender && Progression.For(Progression.FirstDefender).Defender,
                $"The defender should first appear at corner {Progression.FirstDefender + 1}.");

            for (int i = 1; i < Progression.CornersPerRound; i++)
            {
                var a = Progression.For(i - 1);
                var b = Progression.For(i);
                bool harder = b.CrossTime <= a.CrossTime && b.Tolerance <= a.Tolerance &&
                              b.KeeperReaction <= a.KeeperReaction && b.KeeperSpeed >= a.KeeperSpeed &&
                              b.KeeperShift >= a.KeeperShift && b.DefenderBeats >= a.DefenderBeats;
                failures += Require(harder, $"Corner {i + 1} is easier than corner {i} in some way.");
            }

            // A child gets a generous window early on, and still a fair one at the end.
            failures += Require(first.Tolerance >= 0.2f, "The first corner's timing window is under 0.2 s.");
            failures += Require(last.Tolerance >= 0.12f, "The last corner's timing window is under 0.12 s.");
            failures += Require(first.CrossTime >= 1.4f, "The first cross arrives too quickly to read.");

            if (failures == 0) Debug.Log("[Verify] Progression: OK.");
            return failures;
        }

        /// <summary>
        /// The crosses: each ends on the meeting point, arcs high enough to read but
        /// stays inside the stadium, arrives dropping toward the head, and the
        /// meeting point is where a perfectly timed jump puts the forehead.
        /// </summary>
        static int CheckCross()
        {
            int failures = 0;
            float perfectHead = Field.HeadHeight(HeaderJudge.PerfectLead);
            failures += Require(Mathf.Abs(perfectHead - (Field.StandingHead + Field.JumpHeight)) < 0.001f,
                $"A perfect jump puts the head at {perfectHead:0.00} m, not at the top of the jump.");

            for (int i = 0; i < Progression.CornersPerRound; i++)
            {
                var s = Progression.For(i);
                foreach (int side in new[] { -1, 1 })
                {
                    foreach (float x in new[] { -s.MeetSpread, 0f, s.MeetSpread })
                    {
                        var meet = new Vector3(x, perfectHead, Field.GoalLineZ - s.MeetDistance);
                        var cross = new Flight(Field.CornerSpot(side), meet, s.CrossTime);
                        failures += Require((cross.At(s.CrossTime) - meet).magnitude < 0.001f, $"Corner {i + 1}: the cross misses the meeting point.");
                        failures += Require(cross.Apex > 2.8f && cross.Apex < 7f, $"Corner {i + 1}: the cross peaks at {cross.Apex:0.0} m.");
                        failures += Require(cross.VelocityAt(s.CrossTime).y < 0f, $"Corner {i + 1}: the cross is still rising at the head.");

                        // The attacker starts a step back and must get there in 70% of the flight.
                        var start = new Vector3(x * 0.4f - side * 1.1f, 0f, meet.z - 1.8f);
                        float run = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(meet.x, 0f, meet.z));
                        failures += Require(run / (s.CrossTime * 0.7f) < 7f, $"Corner {i + 1}: the attacker would have to sprint {run:0.0} m.");
                    }
                }
            }

            if (failures == 0) Debug.Log("[Verify] Crosses: OK.");
            return failures;
        }

        /// <summary>
        /// The header rules, for every corner: a perfect jump aimed away from the
        /// keeper always scores and one straight at him is saved; no jump or a jump
        /// outside the window never touches the ball; jumping early sends it high
        /// and late keeps it low; and the stars run from a whiff to a perfect round.
        /// </summary>
        static int CheckJudge()
        {
            int failures = 0;
            float lead = HeaderJudge.PerfectLead;

            for (int i = 0; i < Progression.CornersPerRound; i++)
            {
                var s = Progression.For(i);
                var meet = new Vector3(s.MeetSpread, Field.StandingHead + Field.JumpHeight, Field.GoalLineZ - s.MeetDistance);
                float keeper = s.KeeperShift;

                var away = HeaderJudge.Decide(s, meet, lead, -Field.AimLimit, keeper, 1f);
                failures += Require(away.Result == HeaderResult.Goal && away.Perfect && away.Quality > 0.99f,
                    $"Corner {i + 1}: a perfect header away from the keeper gives {away.Result}.");

                var atHim = HeaderJudge.Decide(s, meet, lead, keeper, keeper, 0f);
                failures += Require(atHim.Result == HeaderResult.Saved, $"Corner {i + 1}: a header straight at the keeper gives {atHim.Result}.");

                var none = HeaderJudge.Decide(s, meet, -1f, 0f, keeper, 0f);
                failures += Require(none.Result == (s.Defender ? HeaderResult.Cleared : HeaderResult.Missed),
                    $"Corner {i + 1}: not jumping gives {none.Result}.");

                foreach (float off in new[] { -1.05f, 1.05f })
                {
                    var wild = HeaderJudge.Decide(s, meet, lead + off * s.Tolerance, -Field.AimLimit, keeper, 0f);
                    failures += Require(!wild.Headed, $"Corner {i + 1}: a jump {off * s.Tolerance:+0.00;-0.00}s off still headed it.");
                }

                // The edges of the window: early goes high, late goes low, and a
                // jump just inside the window still finds the ball.
                var early = HeaderJudge.Target(0f, s.Tolerance * 0.9f, s.Tolerance, 0.1f, 0f);
                var late = HeaderJudge.Target(0f, -s.Tolerance * 0.9f, s.Tolerance, 0.1f, 0f);
                failures += Require(early.y > Field.GoalHeight * 0.85f && late.y < 0.4f,
                    $"Corner {i + 1}: early/late headers go to {early.y:0.00}/{late.y:0.00} m.");
                var edge = HeaderJudge.Decide(s, meet, lead - s.Tolerance * 0.8f, -Field.AimLimit, keeper, 0f);
                failures += Require(edge.Quality > 0f, $"Corner {i + 1}: a jump inside the window missed.");

                // A scrappy contact can wobble, but an aim inside the posts with a
                // decent jump stays on target.
                foreach (float scatter in new[] { -1f, 1f })
                {
                    var wobble = HeaderJudge.Decide(s, meet, lead + s.PerfectWindow, Field.AimLimit * 0.5f, -keeper, scatter);
                    failures += Require(wobble.Result != HeaderResult.Wide, $"Corner {i + 1}: a decent header went wide.");
                }
            }

            // Simulated rounds: a perfect player who heads away from the keeper wins
            // three stars; someone tapping at random earns one.
            int perfectGoals = SimulatedRound(new System.Random(1), 0f, true);
            failures += Require(perfectGoals == Progression.CornersPerRound, $"A perfect round scores {perfectGoals}/10.");
            int randomTotal = 0;
            for (int r = 0; r < 200; r++) randomTotal += SimulatedRound(new System.Random(100 + r), 1f, false);
            float randomAverage = randomTotal / 200f;
            failures += Require(Progression.StarsFor(Mathf.RoundToInt(randomAverage)) == 1,
                $"Tapping at random averages {randomAverage:0.0} goals, which earns more than one star.");
            int decentTotal = 0;
            for (int r = 0; r < 200; r++) decentTotal += SimulatedRound(new System.Random(500 + r), 0.12f, true);
            float decentAverage = decentTotal / 200f;
            failures += Require(decentAverage >= Progression.TwoStarGoals && decentAverage < Progression.CornersPerRound,
                $"A decent player (0.12 s off, aiming away) averages {decentAverage:0.0} goals.");

            failures += Require(Progression.StarsFor(0) == 1 && Progression.StarsFor(Progression.TwoStarGoals) == 2 &&
                                Progression.StarsFor(Progression.ThreeStarGoals) == 3 && Progression.StarsFor(10) == 3,
                "Star thresholds do not run 1 to 3.");
            failures += Require(Progression.TwoStarGoals == 4 && Progression.ThreeStarGoals == 7,
                "Star thresholds changed - update this check and the README together.");

            if (failures == 0)
                Debug.Log($"[Verify] Header rules: OK (random tapping averages {randomAverage:0.0} goals, a decent player {decentAverage:0.0}).");
            return failures;
        }

        /// <summary>
        /// Goals in one round for a player whose jump is up to <paramref name="sloppiness"/>
        /// seconds off (1 means anywhere in the cross), aiming away from the keeper or anywhere.
        /// </summary>
        static int SimulatedRound(System.Random random, float sloppiness, bool smartAim)
        {
            int goals = 0;
            for (int i = 0; i < Progression.CornersPerRound; i++)
            {
                var s = Progression.For(i);
                float side = random.NextDouble() < 0.5 ? -1f : 1f;
                float meetX = ((float)random.NextDouble() * 2f - 1f) * s.MeetSpread;
                var meet = new Vector3(meetX, Field.StandingHead + Field.JumpHeight, Field.GoalLineZ - s.MeetDistance);
                float keeper = side * (0.3f + 0.7f * (float)random.NextDouble()) * s.KeeperShift;
                float age = sloppiness >= 1f
                    ? (float)random.NextDouble() * s.CrossTime
                    : HeaderJudge.PerfectLead + ((float)random.NextDouble() * 2f - 1f) * sloppiness;
                float aim = smartAim ? -Mathf.Sign(keeper) * Field.AimLimit * 0.9f
                                     : ((float)random.NextDouble() * 2f - 1f) * Field.AimLimit;
                var outcome = HeaderJudge.Decide(s, meet, age, aim, keeper, (float)random.NextDouble() * 2f - 1f);
                if (outcome.Result == HeaderResult.Goal) goals++;
            }
            return goals;
        }

        /// <summary>The character rig maps to a humanoid, so the jump and header can be posed by bone.</summary>
        static int CheckPlayers()
        {
            int failures = 0;
            var avatar = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Remy.fbx").OfType<Avatar>().FirstOrDefault();
            failures += Require(avatar != null && avatar.isValid && avatar.isHuman, "The character has no valid humanoid avatar.");

            failures += CheckController(CharacterImporter.PlayerControllerPath, new[] { "Idle", "Run", "Kick" }, null);
            failures += CheckController(CharacterImporter.KeeperControllerPath, new[] { "Idle", "Dive" }, "Mirror");

            if (failures == 0) Debug.Log("[Verify] Players: OK.");
            return failures;
        }

        static int CheckController(string path, string[] states, string parameter)
        {
            int failures = 0;
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null) return Require(false, $"No animator controller at {path}.");
            var names = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
            foreach (var state in states)
                failures += Require(names.Contains(state), $"{Path.GetFileName(path)} has no '{state}' state.");
            if (parameter != null)
                failures += Require(controller.parameters.Any(p => p.name == parameter), $"{Path.GetFileName(path)} has no '{parameter}' parameter.");
            return failures;
        }

        static int CheckSceneWiring()
        {
            int failures = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var game = Object.FindFirstObjectByType<HeaderGame>();
            if (game == null) return Require(false, "No HeaderGame in the scene.");

            var serialized = new SerializedObject(game);
            foreach (var field in new[] { "ball", "attacker", "defender", "taker", "keeper", "hud", "ring", "audio_", "framer" })
                failures += Require(serialized.FindProperty(field).objectReferenceValue != null, $"HeaderGame.{field} is not wired.");

            // The page and the browser checks reach these by name through SendMessage.
            failures += Require(GameObject.Find("HeaderGame") != null, "No 'HeaderGame' object for the autopilot hook.");
            var audioObject = GameObject.Find("HeaderAudio");
            failures += Require(audioObject != null && audioObject.GetComponent<HeaderAudio>() != null,
                "No 'HeaderAudio' object for the page's mute button to reach.");

            failures += Require(Object.FindFirstObjectByType<CameraFramer>() != null, "Camera has no CameraFramer.");
            failures += Require(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length >= 1, "No lights in the scene.");
            failures += Require(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                "No EventSystem, so the end card's button cannot be pressed.");

            var ball = Object.FindFirstObjectByType<Ball>();
            failures += Require(ball != null && ball.GetComponent<Rigidbody>() != null && ball.GetComponent<Collider>() != null,
                "The ball needs a Rigidbody and a collider.");

            foreach (var player in Object.FindObjectsByType<Footballer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var animator = player.GetComponent<Animator>();
                failures += Require(animator != null && animator.runtimeAnimatorController != null && animator.isHuman,
                    $"{player.name} has no humanoid animator.");
                failures += Require(player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0, $"{player.name} has no body.");
            }
            failures += Require(Object.FindObjectsByType<Footballer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 3,
                "Expected an attacker, a defender and a corner taker.");

            var keeper = Object.FindFirstObjectByType<Keeper>();
            failures += Require(keeper != null && keeper.GetComponent<Animator>() != null, "The keeper is missing or has no animator.");

            // The goal stops a headed ball: posts, bar and net all have colliders.
            foreach (var name in new[] { "PostLeft", "PostRight", "Crossbar", "NetBack", "NetLeft", "NetRight", "NetRoof", "Pitch" })
            {
                var part = GameObject.Find(name);
                failures += Require(part != null && part.GetComponent<Collider>() != null, $"'{name}' is missing or has no collider.");
            }

            var hud = Object.FindFirstObjectByType<HudController>();
            if (hud != null)
            {
                failures += Require(hud.StartButton != null, "HUD start button is not wired.");
                var texts = hud.GetComponentsInChildren<Text>(true);
                foreach (var text in texts)
                {
                    failures += Require(text.font != null && text.font.name.Contains("Fredoka"),
                        $"{text.name} does not use Fredoka.");
                    failures += Require(text.horizontalOverflow == HorizontalWrapMode.Overflow, $"{text.name} wraps its own text.");
                }

                var hudObject = new SerializedObject(hud);
                foreach (var field in new[] { "scaler", "counters", "scoreText", "goalsText", "cornerText", "toastText",
                             "hintText", "overlay", "dim", "card", "titleText", "badge", "starRow", "bodyText",
                             "startButton", "startButtonLabel" })
                    failures += Require(hudObject.FindProperty(field).objectReferenceValue != null, $"HUD.{field} is not wired.");
                failures += Require(hudObject.FindProperty("starFills").arraySize == 3, "The end card does not have 3 star slots.");

                foreach (var button in hud.GetComponentsInChildren<Button>(true))
                    failures += Require(button.GetComponent<PressFeedback>() != null, $"{button.name} has no PressFeedback.");
                foreach (var image in hud.GetComponentsInChildren<Image>(true).Where(i => i.type == Image.Type.Sliced))
                    failures += Require(image.sprite != null && image.sprite.border != Vector4.zero,
                        $"{image.name} is sliced but its sprite has no border.");
            }
            else
            {
                failures += Require(false, "HUD is missing.");
            }

            var ring = Object.FindFirstObjectByType<TimingRing>();
            if (ring != null)
            {
                var ringObject = new SerializedObject(ring);
                foreach (var field in new[] { "root", "target", "closer" })
                    failures += Require(ringObject.FindProperty(field).objectReferenceValue != null, $"TimingRing.{field} is not wired.");
            }
            else
            {
                failures += Require(false, "No TimingRing.");
            }

            if (failures == 0) Debug.Log("[Verify] Scene wiring: OK.");
            return failures;
        }

        /// <summary>The stadium recordings and the club's music ship, and the page's mute button talks to the right object.</summary>
        static int CheckAudioAndPage()
        {
            int failures = 0;
            foreach (var clip in new[] { "Crowd", "Whistle", "Cheer", "Ohh", "Applause" })
                failures += Require(Resources.Load<AudioClip>("Audio/" + clip) != null, $"Audio clip '{clip}' is missing.");

            string audioCode = File.ReadAllText("Assets/Scripts/Runtime/HeaderAudio.cs");
            foreach (var (clip, loop) in HeaderAudio.Music)
            {
                failures += Require(HeaderAudio.Load("Music/" + clip) != null, $"Music clip '{clip}' is missing.");
                bool shouldLoop = clip == "MusicAnthem" || clip == "MusicDrums";
                failures += Require(loop == shouldLoop, $"Music clip '{clip}' should {(shouldLoop ? "" : "not ")}loop.");
                if (clip != "MusicStar")
                    failures += Require(audioCode.Contains($"Load(\"Music/{clip}\")"), $"Music clip '{clip}' is not wired into HeaderAudio.");
            }

            string game = File.ReadAllText("Assets/Scripts/Runtime/HeaderGame.cs");
            foreach (var call in new[] { "PlayRoundMusic()", "StopRoundMusic()", "PlayGoal(", "PlayResult(earned)", "PlayHeader(", "PlayCross()" })
                failures += Require(game.Contains(call), $"HeaderGame never calls {call}.");

            string page = File.Exists(TemplatePath) ? File.ReadAllText(TemplatePath) : "";
            failures += Require(page.Contains("SendMessage(\"HeaderAudio\", \"SetMuted\""),
                "The web template does not mute through HeaderAudio.SetMuted.");
            failures += Require(page.Contains("../../app.js"), "The web template does not load the shared app.js.");
            failures += Require(page.Contains("manifest.webmanifest"), "The web template lost its PWA tags.");
            failures += Require(!page.Contains("Dribble"), "The web template still mentions Dribble.");

            if (failures == 0) Debug.Log("[Verify] Audio, music and web page: OK.");
            return failures;
        }

        /// <summary>The stars and bests reach the website.</summary>
        static int CheckPortalBridge()
        {
            int failures = 0;
            const string plugin = "Assets/Plugins/WebGL/PortalBridge.jslib";
            var importer = AssetImporter.GetAtPath(plugin) as PluginImporter;
            failures += Require(importer != null && importer.GetCompatibleWithPlatform(BuildTarget.WebGL),
                "PortalBridge.jslib is missing or not enabled for WebGL.");

            string game = File.ReadAllText("Assets/Scripts/Runtime/HeaderGame.cs");
            string feedback = File.ReadAllText("Assets/Scripts/Runtime/PressFeedback.cs");
            failures += Require(game.Contains("PortalBridge.MarkPlayed(Slug)") && game.Contains("const string Slug = \"header\""),
                "A round start does not mark header as played.");
            failures += Require(game.Contains("PortalBridge.AddStars(earned)"), "Finished rounds do not add stars.");
            failures += Require(game.Contains("PortalBridge.ReportBest(Slug, score)"), "Finished rounds do not report the best.");
            failures += Require(feedback.Contains("PortalBridge.Haptic(10)"), "Button presses do not buzz.");

            if (failures == 0) Debug.Log("[Verify] Portal bridge: OK.");
            return failures;
        }

        static int Require(bool condition, string message)
        {
            if (condition) return 0;
            Debug.LogError($"[Verify] {message}");
            return 1;
        }

        /// <summary>
        /// Renders the scene's camera in portrait and landscape to PNGs under
        /// Builds/Shots, framed the way CameraFramer frames a real window. Run
        /// without -nographics so there is a GPU to render with.
        /// </summary>
        [MenuItem("Headers/Render Shots")]
        public static void RenderShots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory("Builds/Shots");
            var cam = Camera.main;
            var framer = cam.GetComponent<CameraFramer>();

            // A mid-round pose: the ball dropping toward the attacker, defender beside him.
            var attacker = GameObject.Find("Attacker");
            var defender = GameObject.Find("Defender");
            var ball = GameObject.Find("Ball");
            var meet = new Vector3(1.2f, Field.StandingHead + Field.JumpHeight, Field.GoalLineZ - 7.5f);
            attacker.transform.position = new Vector3(meet.x, 0f, meet.z - 0.12f);
            defender.transform.position = new Vector3(meet.x + 0.85f, 0f, meet.z + 0.55f);
            defender.transform.rotation = Quaternion.LookRotation(Vector3.right);
            var cross = new Flight(Field.CornerSpot(1), meet, 1.4f);
            ball.transform.position = cross.At(1.1f);

            // Edit mode does not run the animators, so pose everyone in the idle clip.
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Anim_Idle.fbx").OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__"));
            AnimationMode.StartAnimationMode();
            foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                AnimationMode.SampleAnimationClip(animator.gameObject, idle, 1f);

            foreach (var (w, h) in new[] { (720, 1280), (1280, 720), (750, 1334), (2340, 1080) })
            {
                framer.ApplyFor(w, h);
                cam.aspect = (float)w / h;
                var bar = cam.WorldToViewportPoint(new Vector3(0f, Field.GoalHeight + 0.5f, Field.GoalLineZ));
                var feet = cam.WorldToViewportPoint(new Vector3(0f, 0f, Field.GoalLineZ - 9f));
                Debug.Log($"[Shots] {w}x{h}: fov {cam.fieldOfView:0.0}, top of goal at {bar.y:0.00}, near feet at {feet.y:0.00}");
                var rt = new RenderTexture(w, h, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(w, h, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                image.Apply();
                File.WriteAllBytes($"Builds/Shots/shot_{w}x{h}.png", image.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(image);
            }
            AnimationMode.StopAnimationMode();
            Debug.Log("[Shots] Saved to Builds/Shots");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// WebGL build for the games portal. Compression is switched off so the
        /// files can be served by any static host without special headers.
        /// </summary>
        [MenuItem("Headers/Build WebGL")]
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
            PlayerSettings.productName = "Headers";
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
