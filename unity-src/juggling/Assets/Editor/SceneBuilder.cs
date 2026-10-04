using System.Collections.Generic;
using System.IO;
using Juggling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Juggling.EditorTools
{
    /// <summary>
    /// Builds the entire Juggling scene from code so the whole game is
    /// reproducible from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod Juggling.EditorTools.SceneBuilder.BuildScene
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        const string ScenePath = ScenesFolder + "/Juggling.unity";

        // The juggling happens in the plane z = 0, inside the penalty box; the goal
        // stands in the background.
        const float GoalWidth = 7.32f;
        const float GoalHeight = 2.44f;
        const float GoalLineZ = 12f;
        const float PostRadius = 0.13f;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        static readonly Color PitchStripe = new Color(0.12f, 0.34f, 0.17f);
        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        // Maccabi Netanya play in yellow and black.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        static readonly Color Gold = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color Navy = new Color(0.05f, 0.11f, 0.17f);

        [MenuItem("Juggling/Build Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            var ball = BuildBall();
            var player = BuildPlayer();
            var hud = BuildHud();
            var framer = BuildCameraAndLights();

            // The web page's sound button reaches the game through
            // SendMessage("JuggleAudio", "SetMuted", ...), so the name matters.
            var audioObject = new GameObject("JuggleAudio", typeof(AudioSource), typeof(JuggleAudio));

            var managerObject = new GameObject("JuggleManager");
            var manager = managerObject.AddComponent<JuggleManager>();
            manager.Bind(ball, hud, audioObject.GetComponent<JuggleAudio>(), framer, player);
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Juggling] Scene built and saved to {ScenePath}");
        }

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.localScale = new Vector3(6f, 1f, 6f);
            pitch.transform.position = Vector3.zero;
            Object.DestroyImmediate(pitch.GetComponent<Collider>());
            var pitchMaterial = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            pitchMaterial.mainTexture = GrassTexture();
            pitchMaterial.mainTextureScale = new Vector2(26f, 26f);
            // Seen square-on the grass reads brighter than from behind a penalty
            // taker, so it is toned down to stay a pitch green rather than lime.
            pitchMaterial.color = new Color(0.70f, 0.80f, 0.70f);
            pitch.GetComponent<Renderer>().sharedMaterial = pitchMaterial;

            // Mown stripes - subtle, but they sell the scale of the pitch.
            for (int i = -6; i <= 8; i++)
            {
                if (i % 2 != 0) continue;
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Stripe_{i}";
                stripe.transform.localScale = new Vector3(60f, 0.02f, 2.6f);
                stripe.transform.position = new Vector3(0f, 0.01f, i * 2.6f);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
                var stripeMaterial = GetMaterial("PitchStripe", PitchStripe, 0f, 0.08f);
                stripeMaterial.mainTexture = GrassTexture();
                stripeMaterial.mainTextureScale = new Vector2(20f, 1.2f);
                stripeMaterial.color = new Color(0.56f, 0.66f, 0.56f);
                stripe.GetComponent<Renderer>().sharedMaterial = stripeMaterial;
            }

            BuildGoal();
            BuildPitchMarkings();
            BuildAdBoards();
            BuildStands();
        }

        static void BuildGoal()
        {
            var goal = new GameObject("Goal");
            goal.transform.position = new Vector3(0f, 0f, GoalLineZ);

            var postMaterial = GetMaterial("GoalWhite", Chalk, 0f, 0.45f);

            BuildPost(goal.transform, "PostLeft",
                new Vector3(-GoalWidth / 2f, GoalHeight / 2f, GoalLineZ),
                new Vector3(PostRadius, GoalHeight / 2f, PostRadius), postMaterial);

            BuildPost(goal.transform, "PostRight",
                new Vector3(GoalWidth / 2f, GoalHeight / 2f, GoalLineZ),
                new Vector3(PostRadius, GoalHeight / 2f, PostRadius), postMaterial);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Crossbar";
            bar.transform.SetParent(goal.transform);
            bar.transform.position = new Vector3(0f, GoalHeight, GoalLineZ);
            bar.transform.localScale = new Vector3(GoalWidth + PostRadius * 2f, PostRadius * 2f, PostRadius * 2f);
            bar.GetComponent<Renderer>().sharedMaterial = postMaterial;

            // Net: a back wall plus two wings, angled back so shots die in the mesh.
            var netMaterial = GetMaterial("NetWhite", new Color(0.90f, 0.93f, 0.92f, 0.44f), 0f, 0.2f, true);

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "NetBack";
            back.transform.SetParent(goal.transform);
            back.transform.position = new Vector3(0f, GoalHeight / 2f, GoalLineZ + 1.6f);
            back.transform.localScale = new Vector3(GoalWidth, GoalHeight, 0.06f);
            back.GetComponent<Renderer>().sharedMaterial = netMaterial;

            foreach (int side in new[] { -1, 1 })
            {
                var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wing.name = side < 0 ? "NetLeft" : "NetRight";
                wing.transform.SetParent(goal.transform);
                wing.transform.position = new Vector3(side * GoalWidth / 2f, GoalHeight / 2f, GoalLineZ + 0.8f);
                wing.transform.localScale = new Vector3(0.06f, GoalHeight, 1.6f);
                wing.GetComponent<Renderer>().sharedMaterial = netMaterial;
            }

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "NetRoof";
            roof.transform.SetParent(goal.transform);
            roof.transform.position = new Vector3(0f, GoalHeight, GoalLineZ + 0.8f);
            roof.transform.localScale = new Vector3(GoalWidth, 0.06f, 1.6f);
            roof.GetComponent<Renderer>().sharedMaterial = netMaterial;
        }

        static void BuildPost(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = name;
            post.transform.SetParent(parent);
            post.transform.position = position;
            post.transform.localScale = scale;
            post.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void BuildPitchMarkings()
        {
            var markings = new GameObject("Markings");
            var chalk = GetMaterial("ChalkLine", Chalk, 0f, 0.2f);

            // Six-yard box and penalty box, drawn as thin slabs.
            AddLine(markings.transform, "GoalLine", new Vector3(0f, 0.012f, GoalLineZ), new Vector3(40f, 0.02f, 0.12f), chalk);
            AddLine(markings.transform, "BoxFront", new Vector3(0f, 0.012f, GoalLineZ - 16.5f), new Vector3(40.3f, 0.02f, 0.12f), chalk);
            AddLine(markings.transform, "BoxLeft", new Vector3(-20.15f, 0.012f, GoalLineZ - 8.25f), new Vector3(0.12f, 0.02f, 16.5f), chalk);
            AddLine(markings.transform, "BoxRight", new Vector3(20.15f, 0.012f, GoalLineZ - 8.25f), new Vector3(0.12f, 0.02f, 16.5f), chalk);
            AddLine(markings.transform, "SixLeft", new Vector3(-9.16f, 0.012f, GoalLineZ - 2.75f), new Vector3(0.12f, 0.02f, 5.5f), chalk);
            AddLine(markings.transform, "SixRight", new Vector3(9.16f, 0.012f, GoalLineZ - 2.75f), new Vector3(0.12f, 0.02f, 5.5f), chalk);
            AddLine(markings.transform, "SixFront", new Vector3(0f, 0.012f, GoalLineZ - 5.5f), new Vector3(18.32f, 0.02f, 0.12f), chalk);

            var spot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spot.name = "PenaltySpot";
            spot.transform.SetParent(markings.transform);
            spot.transform.position = new Vector3(0f, 0.012f, GoalLineZ - 11f);
            spot.transform.localScale = new Vector3(0.22f, 0.01f, 0.22f);
            Object.DestroyImmediate(spot.GetComponent<Collider>());
            spot.GetComponent<Renderer>().sharedMaterial = chalk;
        }

        static void AddLine(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = name;
            line.transform.SetParent(parent);
            line.transform.position = position;
            line.transform.localScale = scale;
            Object.DestroyImmediate(line.GetComponent<Collider>());
            line.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Pitchside hoardings in club colours, with the club name repeated along
        /// them. At this distance the boards read as branding rather than text, which
        /// is exactly how they read on a real broadcast.
        /// </summary>
        static void BuildAdBoards()
        {
            var boards = new GameObject("AdBoards");
            var font = BuiltinFont();

            var yellowBoard = GetMaterial("BoardYellow", KitYellow, 0f, 0.25f);
            var blackBoard = GetMaterial("BoardBlack", KitBlack, 0f, 0.25f);

            const float boardHeight = 0.58f;
            const float segmentWidth = 5.2f;

            for (int i = -4; i <= 4; i++)
            {
                bool yellow = (i & 1) == 0;

                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Board_{i}";
                segment.transform.SetParent(boards.transform);
                segment.transform.position = new Vector3(i * segmentWidth, boardHeight / 2f, GoalLineZ + 4.6f);
                segment.transform.localScale = new Vector3(segmentWidth - 0.12f, boardHeight, 0.16f);
                Object.DestroyImmediate(segment.GetComponent<Collider>());
                segment.GetComponent<Renderer>().sharedMaterial = yellow ? yellowBoard : blackBoard;

                var caption = new GameObject("Caption");
                caption.transform.SetParent(segment.transform, false);
                // Undo the parent's non-uniform scale so the text is not stretched.
                caption.transform.localScale = new Vector3(
                    0.032f / segment.transform.localScale.x,
                    0.032f / segment.transform.localScale.y,
                    0.032f / segment.transform.localScale.z);
                caption.transform.localPosition = new Vector3(0f, 0f, -0.55f);

                var text = caption.AddComponent<TextMesh>();
                text.text = "MACCABI NETANYA";
                text.font = font;
                text.fontSize = 72;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = yellow ? KitBlack : new Color(0.75f, 0.63f, 0.12f);
                caption.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        static void BuildStands()
        {
            var stands = new GameObject("Stands");
            var concrete = GetMaterial("Concrete", new Color(0.18f, 0.20f, 0.24f), 0f, 0.12f);
            var crowdColors = new[]
            {
                new Color(0.72f, 0.60f, 0.12f), new Color(0.16f, 0.16f, 0.17f),
                new Color(0.62f, 0.52f, 0.14f), new Color(0.48f, 0.48f, 0.47f)
            };

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "StandBack";
            back.transform.SetParent(stands.transform);
            back.transform.position = new Vector3(0f, 3.6f, GoalLineZ + 15f);
            back.transform.localScale = new Vector3(52f, 6.4f, 1f);
            back.GetComponent<Renderer>().sharedMaterial = concrete;

            // Crowd, stamped in as coloured blocks - cheap, and reads correctly at distance.
            int index = 0;
            for (int row = 0; row < 7; row++)
            {
                for (int col = -13; col <= 13; col++)
                {
                    var person = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    person.name = "Fan";
                    person.transform.SetParent(stands.transform);
                    person.transform.position = new Vector3(
                        col * 1.8f + (row % 2 == 0 ? 0.4f : -0.4f),
                        1.6f + row * 0.80f,
                        GoalLineZ + 14.4f - row * 0.08f);
                    person.transform.localScale = new Vector3(0.5f, 0.5f, 0.3f);
                    Object.DestroyImmediate(person.GetComponent<Collider>());
                    person.GetComponent<Renderer>().sharedMaterial =
                        GetMaterial($"Crowd{index % crowdColors.Length}",
                            crowdColors[index % crowdColors.Length], 0f, 0.15f);
                    index++;
                }
            }
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";
        const string StrikerController = "Assets/Characters/StrikerAnimator.controller";
        const float PlayerHeight = 1.82f;

        /// <summary>
        /// Drops in the rigged character, scales it to a believable height and
        /// dresses it in a kit. The Mixamo rig imports several metres tall, so the
        /// scale is measured from the model rather than hard-coded.
        /// </summary>
        static GameObject SpawnPlayer(string name, string controllerPath, Color shirt, Color shorts)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Juggling] Character model missing at {ModelPath}");
                return new GameObject(name);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = name;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                if (bounds.size.y > 0.01f)
                    instance.transform.localScale = Vector3.one * (PlayerHeight / bounds.size.y);
            }

            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            Dress(instance, name, shirt, shorts);
            return instance;
        }

        /// <summary>
        /// The rig's materials arrive untextured and split by body part, so each one
        /// can simply be painted - which is how the kit becomes club colours.
        /// </summary>
        static void Dress(GameObject player, string kitName, Color shirt, Color shorts)
        {
            foreach (var renderer in player.GetComponentsInChildren<Renderer>())
            {
                Color colour;
                switch (renderer.name)
                {
                    case "Tops":      colour = shirt; break;
                    case "Bottoms":   colour = shorts; break;
                    case "Shoes":     colour = new Color(0.94f, 0.94f, 0.92f); break;
                    case "Hair":      colour = new Color(0.16f, 0.11f, 0.08f); break;
                    case "Eyelashes": colour = new Color(0.10f, 0.08f, 0.07f); break;
                    default:          colour = Skin; break;   // Body and Eyes
                }

                float smoothness = renderer.name == "Shoes" ? 0.3f : 0.18f;
                renderer.sharedMaterial = GetMaterial($"{kitName}_{renderer.name}", colour, 0f, smoothness);
            }
        }

        // ---------------------------------------------------------------- actors

        static JuggleBall BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            var ballMaterial = GetMaterial("BallWhite", Color.white, 0f, 0.30f);
            ballMaterial.mainTexture = BallTexture();
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            // A soft dark disc on the grass under the ball; JuggleBall moves, sizes
            // and fades it as the ball climbs and falls.
            var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "BallShadow";
            Object.DestroyImmediate(shadow.GetComponent<Collider>());
            var shadowRenderer = shadow.GetComponent<Renderer>();
            shadowRenderer.sharedMaterial = GetMaterial("BallShadow", new Color(0f, 0f, 0f, 0.4f), 0f, 0f, true);
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            // The sun's own shadow of the ball falls off to one side, which reads as
            // the ball being somewhere it is not; the blob directly below replaces it.
            ball.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var juggleBall = ball.AddComponent<JuggleBall>();
            juggleBall.Bind(shadowRenderer);
            juggleBall.Position = new Vector2(0f, 0.5f);
            juggleBall.SetRadius(Tiers.All[0].BallRadius);
            EditorUtility.SetDirty(juggleBall);
            return juggleBall;
        }

        static PlayerCheer BuildPlayer()
        {
            var player = SpawnPlayer("Player", StrikerController, KitYellow, KitBlack);
            player.transform.position = new Vector3(-4.3f, 0f, 1.5f);
            player.transform.rotation = Quaternion.Euler(0f, 155f, 0f);
            return player.AddComponent<PlayerCheer>();
        }

        // ---------------------------------------------------------------- camera, lights, hud

        static CameraFramer BuildCameraAndLights()
        {
            // Square-on to the juggling plane; CameraFramer sets the height and zoom
            // for the window it finds itself in.
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 2.7f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            var framer = cameraObject.AddComponent<CameraFramer>();

            var sunObject = new GameObject("Sun");
            sunObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.90f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            var fillObject = new GameObject("Fill");
            fillObject.transform.rotation = Quaternion.Euler(18f, 160f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.76f, 0.92f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.44f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.29f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.15f, 0.12f);

            return framer;
        }

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        static HudController BuildHud()
        {
            var font = BuiltinFont();

            var canvasObject = new GameObject("HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            // A square reference, matched to the short side at runtime by
            // HudController, so portrait phones get a full-size HUD.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HudController.ReferenceSize, HudController.ReferenceSize);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            // --- score bar (top) ------------------------------------------------
            // Three columns across the top. The ball's flight stays below it
            // (HudController.TopReserve), and the page's buttons live down in the
            // grass strip, so nothing ever covers the ball.
            const float barWidth = 688f;
            const float column = barWidth / 3f;
            var bar = Panel(canvasObject.transform, "ScoreBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(barWidth, 110f),
                new Color(0.05f, 0.11f, 0.17f, 0.82f));
            bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -16f);

            Label(bar.transform, "ScoreLabel", "SCORE", font, 22, Chalk * 0.7f,
                TextAnchor.MiddleCenter, new Vector2(0f, -10f), new Vector2(column, 28f));
            var scoreValue = Label(bar.transform, "ScoreValue", "0", font, 54, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, -40f), new Vector2(column, 62f));

            Label(bar.transform, "ComboLabel", "COMBO", font, 22, Chalk * 0.7f,
                TextAnchor.MiddleCenter, new Vector2(column, -10f), new Vector2(column, 28f));
            var comboValue = Label(bar.transform, "ComboValue", "-", font, 44, Chalk,
                TextAnchor.MiddleCenter, new Vector2(column, -44f), new Vector2(column, 54f));

            Label(bar.transform, "BestLabel", "BEST", font, 22, Chalk * 0.7f,
                TextAnchor.MiddleCenter, new Vector2(column * 2f, -10f), new Vector2(column, 28f));
            var bestValue = Label(bar.transform, "BestValue", "0", font, 44, Chalk,
                TextAnchor.MiddleCenter, new Vector2(column * 2f, -44f), new Vector2(column, 54f));

            // --- in-play messages -----------------------------------------------
            var toast = Label(canvasObject.transform, "Toast", "", font, 40, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, -150f), new Vector2(680f, 56f));
            SetAnchor(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            toast.fontStyle = FontStyle.Bold;
            Outline(toast);

            var banner = Label(canvasObject.transform, "Banner", "", font, 70, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 60f), new Vector2(700f, 90f));
            SetAnchor(banner.rectTransform, Centre, Centre, Centre);
            banner.fontStyle = FontStyle.Bold;
            Outline(banner);

            var hint = Label(canvasObject.transform, "Hint", "", font, 36, Chalk,
                TextAnchor.MiddleCenter, new Vector2(0f, -20f), new Vector2(680f, 100f));
            SetAnchor(hint.rectTransform, Centre, Centre, Centre);
            hint.fontStyle = FontStyle.Bold;
            Outline(hint);

            // --- overlay -------------------------------------------------------
            // Laid out to fit a 720 x 720 square, so it fits a phone held either way.
            var overlay = Panel(canvasObject.transform, "Overlay",
                Vector2.zero, Vector2.one, Vector2.zero, new Color(0.04f, 0.09f, 0.14f, 0.88f));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var title = Label(overlay.transform, "Title", "JUGGLING", font, 66, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 290f), new Vector2(700f, 84f));
            SetAnchor(title.rectTransform, Centre, Centre, Centre);
            title.fontStyle = FontStyle.Bold;

            var body = Label(overlay.transform, "Body", "", font, 25, Chalk * 0.9f,
                TextAnchor.MiddleCenter, new Vector2(0f, 172f), new Vector2(700f, 136f));
            SetAnchor(body.rectTransform, Centre, Centre, Centre);

            var tierCaption = Label(overlay.transform, "DifficultyCaption", "CHOOSE YOUR LEVEL",
                font, 22, Chalk * 0.65f, TextAnchor.MiddleCenter, new Vector2(0f, 72f), new Vector2(600f, 30f));
            SetAnchor(tierCaption.rectTransform, Centre, Centre, Centre);

            var tierButtons = new Button[Tiers.All.Length];
            var tierBackgrounds = new Image[Tiers.All.Length];
            var tierLabels = new Text[Tiers.All.Length];

            for (int i = 0; i < Tiers.All.Length; i++)
            {
                var tier = Tiers.All[i];
                var tierObject = new GameObject($"Difficulty{tier.Name}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                tierObject.transform.SetParent(overlay.transform, false);

                // Two by two: big targets for small fingers.
                var tierRect = tierObject.GetComponent<RectTransform>();
                SetAnchor(tierRect, Centre, Centre, Centre);
                tierRect.sizeDelta = new Vector2(300f, 86f);
                tierRect.anchoredPosition = new Vector2(i % 2 == 0 ? -160f : 160f, i < 2 ? 0f : -100f);

                var tierImage = tierObject.GetComponent<Image>();
                tierImage.color = new Color(0.10f, 0.20f, 0.28f);
                var tierButton = tierObject.GetComponent<Button>();
                tierButton.targetGraphic = tierImage;

                var tierLabel = Label(tierObject.transform, "Label", tier.Name, font, 32,
                    new Color(0.86f, 0.88f, 0.86f), TextAnchor.MiddleCenter, new Vector2(0f, 14f), new Vector2(300f, 40f));
                SetAnchor(tierLabel.rectTransform, Centre, Centre, Centre);

                var tierHint = Label(tierObject.transform, "Hint", tier.Hint, font, 20,
                    new Color(0.86f, 0.88f, 0.86f, 0.7f), TextAnchor.MiddleCenter, new Vector2(0f, -20f), new Vector2(300f, 26f));
                SetAnchor(tierHint.rectTransform, Centre, Centre, Centre);

                tierButtons[i] = tierButton;
                tierBackgrounds[i] = tierImage;
                tierLabels[i] = tierLabel;
            }

            var buttonObject = new GameObject("StartButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(overlay.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            SetAnchor(buttonRect, Centre, Centre, Centre);
            buttonRect.sizeDelta = new Vector2(340f, 92f);
            buttonRect.anchoredPosition = new Vector2(0f, -232f);
            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = Gold;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;

            var buttonLabel = Label(buttonObject.transform, "Label", "Kick Off", font, 38,
                Navy, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(340f, 92f));
            SetAnchor(buttonLabel.rectTransform, Centre, Centre, Centre);
            buttonLabel.fontStyle = FontStyle.Bold;

            var hud = canvasObject.AddComponent<HudController>();
            hud.Bind(scaler, scoreValue, comboValue, bestValue, toast, banner, hint,
                overlay, title, body, button, buttonLabel);
            hud.BindDifficulty(tierButtons, tierBackgrounds, tierLabels);
            hud.BindScoreBar(bar);
            EditorUtility.SetDirty(hud);

            return hud;
        }

        /// <summary>A dark edge so text floating over the grass and sky stays readable.</summary>
        static void Outline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.06f, 0.10f, 0.85f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
        }

        // ---------------------------------------------------------------- ui helpers

        static GameObject Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 sizeDelta, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, anchorMax.y > 0.5f ? 1f : 0f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = Vector2.zero;

            panel.GetComponent<Image>().color = color;
            return panel;
        }

        static void SetStretchWidth(GameObject panel, float left, float right, float yOffset, float height)
        {
            var rect = panel.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(0f, rect.pivot.y > 0.5f ? -30f : 30f);
        }

        static Text Label(Transform parent, string name, string content, Font font, int size,
            Color color, TextAnchor anchor, Vector2 position, Vector2 sizeDelta)
        {
            var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = position;

            var text = labelObject.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        static void SetAnchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot)
        {
            Vector2 position = rect.anchoredPosition;
            Vector2 size = rect.sizeDelta;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        // ---------------------------------------------------------------- assets

        // ------------------------------------------------------------- textures

        /// <summary>
        /// Procedural grass: fine noise plus a faint blade streak so the pitch has
        /// surface detail instead of reading as a flat green plane.
        /// </summary>
        static Texture2D GrassTexture()
        {
            const string path = MaterialsFolder + "/GrassTexture.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var random = new System.Random(20260930);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float blade = Mathf.PerlinNoise(x * 0.35f, y * 0.08f) * 0.10f;
                    float speckle = (float)random.NextDouble() * 0.09f;
                    float shade = 0.80f + blade + speckle;
                    texture.SetPixel(x, y, new Color(0.24f * shade, 0.55f * shade, 0.27f * shade));
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A classic panelled ball, so spin is visible as the ball flies.</summary>
        static Texture2D BallTexture()
        {
            const string path = MaterialsFolder + "/BallTexture.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);

            // Dark panels laid out on a grid, nudged per row so they interlock the
            // way the panels on a real ball do.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int row = y / 43;
                    float offset = (row % 2 == 0) ? 0f : 21f;
                    float cx = Mathf.Repeat(x + offset, 43f) - 21.5f;
                    float cy = Mathf.Repeat(y, 43f) - 21.5f;
                    bool panel = (cx * cx + cy * cy) < 118f;

                    texture.SetPixel(x, y, panel
                        ? new Color(0.10f, 0.10f, 0.11f)
                        : new Color(0.97f, 0.97f, 0.95f));
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        static void Paint(GameObject target, string materialName, Color color, float metallic, float smoothness)
        {
            target.GetComponent<Renderer>().sharedMaterial = GetMaterial(materialName, color, metallic, smoothness);
        }

        static Material GetMaterial(string name, Color color, float metallic, float smoothness, bool transparent = false)
        {
            if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);

            if (transparent) ConfigureTransparent(material);

            EditorUtility.SetDirty(material);
            MaterialCache[name] = material;
            return material;
        }

        /// <summary>Flip a Standard material into its alpha-blended mode.</summary>
        static void ConfigureTransparent(Material material)
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static Font BuiltinFont()
        {
            // Fredoka (Assets/Fonts, SIL Open Font License): rounded and friendly,
            // the same face as the website. Falls back to Unity's bundled font.
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Fredoka-SemiBold.ttf");
            if (font != null) return font;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
