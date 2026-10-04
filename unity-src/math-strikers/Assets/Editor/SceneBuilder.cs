using System.Collections.Generic;
using System.IO;
using MaccabiShared;
using MathStrikers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MathStrikers.EditorTools
{
    /// <summary>
    /// Builds the entire Math Strikers scene from code so the whole game is
    /// reproducible from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod MathStrikers.EditorTools.SceneBuilder.BuildScene
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        const string ScenePath = ScenesFolder + "/Match.unity";

        // Regulation-ish goal, in metres.
        const float GoalWidth = 7.32f;
        const float GoalHeight = 2.44f;
        const float GoalLineZ = 12f;
        const float PostRadius = 0.13f;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        static readonly Color PitchStripe = new Color(0.12f, 0.34f, 0.17f);
        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        // Maccabi Netanya play in yellow and black; the keeper opposite them wears a
        // contrasting kit so the two are never confused mid-shot.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color KeeperTeal = new Color(0.10f, 0.42f, 0.40f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);

        [MenuItem("Math Strikers/Build Match Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UiSprites.Generate();

            BuildEnvironment();
            var ball = BuildBall();
            var keeper = BuildKeeper();
            var striker = BuildStriker();
            var zones = BuildTargetZones();
            var hud = BuildHud();
            BuildCameraAndLights();

            var audioObject = new GameObject("MatchAudio", typeof(AudioSource), typeof(MatchAudio));

            var managerObject = new GameObject("MatchManager");
            var manager = managerObject.AddComponent<MatchManager>();
            manager.Bind(ball, keeper, zones, hud, striker.transform);
            _ = audioObject;
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Math Strikers] Scene built and saved to {ScenePath}");
        }

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.localScale = new Vector3(6f, 1f, 6f);
            pitch.transform.position = Vector3.zero;
            var pitchMaterial = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            pitchMaterial.mainTexture = GrassTexture();
            pitchMaterial.mainTextureScale = new Vector2(26f, 26f);
            pitchMaterial.color = Color.white;
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
                stripeMaterial.color = new Color(0.78f, 0.86f, 0.78f);
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
                text.text = Rtl.Fix("מכבי נתניה");
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

        // ---------------------------------------------------------------- actors

        static BallController BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.position = new Vector3(0f, 0.11f, GoalLineZ - 11f);
            ball.transform.localScale = Vector3.one * 0.22f;
            var ballMaterial = GetMaterial("BallWhite", Color.white, 0f, 0.30f);
            ballMaterial.mainTexture = BallTexture();
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            var body = ball.AddComponent<Rigidbody>();
            body.mass = 0.43f;
            body.linearDamping = 0.08f;
            body.angularDamping = 0.2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            return ball.AddComponent<BallController>();
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";
        const string StrikerController = "Assets/Characters/StrikerAnimator.controller";
        const string KeeperController = "Assets/Characters/KeeperAnimator.controller";
        const float PlayerHeight = 1.82f;

        static GoalkeeperController BuildKeeper()
        {
            var keeper = SpawnPlayer("Goalkeeper", KeeperController, KeeperTeal, new Color(0.06f, 0.24f, 0.23f));
            keeper.transform.position = new Vector3(0f, 0f, GoalLineZ - 0.35f);
            keeper.transform.rotation = Quaternion.LookRotation(Vector3.back);
            return keeper.AddComponent<GoalkeeperController>();
        }

        static GameObject BuildStriker()
        {
            var striker = SpawnPlayer("Striker", StrikerController, KitYellow, KitBlack);
            striker.transform.position = new Vector3(-2.0f, 0f, GoalLineZ - 11.4f);
            striker.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 0f, GoalLineZ) - striker.transform.position);
            return striker;
        }

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
                Debug.LogError($"[Math Strikers] Character model missing at {ModelPath}");
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

        static void AddPart(Transform parent, string name, PrimitiveType type,
            Vector3 localPosition, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        // ---------------------------------------------------------------- target zones

        static TargetZone[] BuildTargetZones()
        {
            var root = new GameObject("TargetZones");
            var font = BuiltinFont();
            // Rounded boards with a gold rim, matching the HUD's panels. Alpha-cut
            // rather than blended so the rim and face sort cleanly against each other.
            var panelMaterial = GetCutoutMaterial("ZoneBoard", Palette.Navy700, UiSprites.Board);
            var rimMaterial = GetCutoutMaterial("ZoneRim", Palette.Gold400, UiSprites.Board);

            // The answer boards stand on the grass in front of the goal rather than
            // hanging in the goal mouth, where the keeper and the net hid them.
            float[] boardLanes = { -2.3f, 0f, 2.3f };
            float[] goalLanes = { -GoalWidth / 3.1f, 0f, GoalWidth / 3.1f };
            const float BoardZ = 4.5f;
            const float BoardWidth = 1.55f;
            const float BoardHeight = 0.66f;
            const float BoardBase = 0.06f;

            float centreY = BoardBase + BoardHeight / 2f;
            var zones = new TargetZone[boardLanes.Length];

            for (int i = 0; i < boardLanes.Length; i++)
            {
                var zoneObject = new GameObject($"Zone_{i}");
                zoneObject.transform.SetParent(root.transform);
                zoneObject.transform.position = new Vector3(boardLanes[i], centreY, BoardZ);

                var collider = zoneObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(BoardWidth, BoardHeight, 0.16f);
                collider.isTrigger = true;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "Panel";
                panel.transform.SetParent(zoneObject.transform, false);
                panel.transform.localScale = new Vector3(BoardWidth, BoardHeight, 1f);
                // Unity's Quad already faces -z (the camera side); flipping it 180
                // pointed the visible face away and the board vanished to backface culling.
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                var panelRenderer = panel.GetComponent<Renderer>();
                panelRenderer.sharedMaterial = panelMaterial;

                // A gold rim just behind the face so each board stands off the grass.
                var rim = GameObject.CreatePrimitive(PrimitiveType.Quad);
                rim.name = "Rim";
                rim.transform.SetParent(zoneObject.transform, false);
                rim.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                rim.transform.localScale = new Vector3(BoardWidth + 0.09f, BoardHeight + 0.09f, 1f);
                Object.DestroyImmediate(rim.GetComponent<Collider>());
                rim.GetComponent<Renderer>().sharedMaterial = rimMaterial;

                // TextMesh already reads correctly from the -z side, which is where the
                // camera sits - rotating it to "face" the camera mirrors the glyphs.
                var labelObject = new GameObject("Label");
                labelObject.transform.SetParent(zoneObject.transform, false);
                labelObject.transform.localPosition = new Vector3(0f, BoardHeight * 0.16f, -0.10f);
                labelObject.transform.localScale = Vector3.one * 0.055f;

                var label = labelObject.AddComponent<TextMesh>();
                label.text = "0";
                label.font = font;
                label.fontSize = 96;
                label.characterSize = 1f;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = Chalk;
                labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                var keyHint = new GameObject("KeyHint");
                keyHint.transform.SetParent(zoneObject.transform, false);
                keyHint.transform.localPosition = new Vector3(0f, -BoardHeight * 0.30f, -0.10f);
                keyHint.transform.localScale = Vector3.one * 0.026f;
                var hint = keyHint.AddComponent<TextMesh>();
                hint.text = Rtl.Fix($"מקש {i + 1}");
                hint.font = font;
                hint.fontSize = 72;
                hint.anchor = TextAnchor.MiddleCenter;
                hint.alignment = TextAlignment.Center;
                hint.color = new Color(0.95f, 0.95f, 0.92f, 0.85f);
                keyHint.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                // The board is the control; the shot still has to finish in the goal,
                // so the aim point stays on the matching lane of the goal mouth.
                var aim = new GameObject("AimPoint");
                aim.transform.SetParent(zoneObject.transform, false);
                aim.transform.localPosition = new Vector3(
                    goalLanes[i] - boardLanes[i],
                    GoalHeight * 0.5f - centreY,
                    GoalLineZ + 0.6f - BoardZ);

                var zone = zoneObject.AddComponent<TargetZone>();
                zone.Bind(panelRenderer, label, aim.transform, keyHint);
                EditorUtility.SetDirty(zone);
                zones[i] = zone;
            }

            return zones;
        }

        // ---------------------------------------------------------------- camera, lights, hud

        static void BuildCameraAndLights()
        {
            // Shoulder height behind the taker: keeps the ball clear of the bottom
            // HUD bar while still framing the whole goal mouth.
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 2.05f, GoalLineZ - 18f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(new Vector3(0f, 1.25f, GoalLineZ - 0.5f) - cameraObject.transform.position);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            // Hold the horizontal framing steady across window shapes, so the
            // striker and the outer answer boards never fall off the sides.
            cameraObject.AddComponent<CameraFramer>().Configure(camera.fieldOfView, 1.25f);

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
        }

        static HudController BuildHud()
        {
            var font = BuiltinFont();

            var canvasObject = new GameObject("HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            var cream = Palette.Cream;
            var soft = Palette.WithAlpha(Palette.Cream, 0.75f);

            // --- score bar (bottom) ---------------------------------------------
            // Along the bottom edge, 30 units up and 110 tall: the page's round
            // buttons sit just above its right end (placeCorner in the page).
            // Laid out right to left: the pills start at the right, where a Hebrew
            // reader starts, and the match banner fills what is left.
            var bar = Sliced(canvasObject.transform, "ScoreBar", UiSprites.Panel, Color.white);
            var barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.offsetMin = new Vector2(40f, 30f);
            barRect.offsetMax = new Vector2(-40f, 140f);
            AddShadow(bar.gameObject);

            float pillRight = 14f;
            var scoreValue = Counter(bar.transform, "Score", "נקודות", UiSprites.Star, Palette.Gold400, 220f, ref pillRight, font);
            var streakValue = Counter(bar.transform, "Streak", "רצף", UiSprites.Bolt, Palette.Gold400, 160f, ref pillRight, font);
            var scorelineValue = Counter(bar.transform, "Match", "תוצאה", UiSprites.Ball, Color.white, 200f, ref pillRight, font);
            scorelineValue.supportRichText = true;

            var matchLine = Label(bar.transform, "BannerMatch", "", font, 24, soft,
                TextAnchor.MiddleRight, Vector2.zero, new Vector2(10f, 30f));
            Stretch(matchLine.rectTransform, 24f, pillRight + 18f, 18f, 30f);
            var opponentLine = Label(bar.transform, "Banner", "", font, 32, Palette.Gold400,
                TextAnchor.MiddleRight, Vector2.zero, new Vector2(10f, 40f));
            Stretch(opponentLine.rectTransform, 24f, pillRight + 18f, -16f, 40f);

            // --- problem card (top centre) -------------------------------------
            // The question is the first thing to read, so it gets the top of the
            // screen. 200 tall and 30 down: CameraFramer frames the pitch below it.
            var card = Sliced(canvasObject.transform, "ProblemCard", UiSprites.Panel, Color.white);
            Place(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(920f, 200f));
            AddShadow(card.gameObject);

            var shot = Label(card.transform, "Shot", "", font, 24, soft,
                TextAnchor.MiddleRight, Vector2.zero, new Vector2(300f, 34f));
            Place(shot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -14f), new Vector2(300f, 34f));

            // The sum itself stays left to right ("7 + 3 = ?").
            var problem = Label(card.transform, "Problem", "", font, 72, cream,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900f, 86f));
            Place(problem.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(900f, 86f));

            // The clock is the pressure, so it gets its own bar rather than a number
            // tucked in a corner. It drains toward the right.
            var track = Sliced(card.transform, "TimerTrack", UiSprites.Pill, Palette.Navy900);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 1f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.pivot = new Vector2(0.5f, 1f);
            trackRect.offsetMin = new Vector2(104f, -126f);
            trackRect.offsetMax = new Vector2(-34f, -106f);

            var fillImage = Sliced(track.transform, "TimerFill", UiSprites.Pill, Palette.Gold400);
            Stretch(fillImage.rectTransform);
            FitPill(track, 20f);
            FitPill(fillImage, 20f);

            var timerText = Label(card.transform, "TimerText", "30", font, 36, Palette.Gold400,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(70f, 44f));
            Place(timerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -116f), new Vector2(70f, 44f), new Vector2(0.5f, 0.5f));

            var feedback = Label(card.transform, "Feedback", "", font, 30, Palette.Gold400,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900f, 40f));
            Place(feedback.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 40f));

            // --- the big result word (גול! / החמצה) ------------------------------
            // Below centre, so it lands on the striker rather than over the boards
            // that show which answer was right.
            var resultWord = Label(canvasObject.transform, "ResultWord", "", font, 150, Palette.Green400,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1000f, 200f));
            Place(resultWord.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1000f, 200f), new Vector2(0.5f, 0.5f));
            AddOutline(resultWord.gameObject, Palette.Navy900, 5f);
            AddShadow(resultWord.gameObject, 8f);
            var resultPop = resultWord.gameObject.AddComponent<ResultPop>();
            resultPop.Bind(resultWord);

            // --- overlay: dim layer plus a card that pops in --------------------
            var overlayImage = Sliced(canvasObject.transform, "Overlay", null, Palette.WithAlpha(Palette.Navy900, 0.8f));
            var overlay = overlayImage.gameObject;
            Stretch(overlayImage.rectTransform);
            overlay.AddComponent<CanvasGroup>();

            var sheet = Sliced(overlay.transform, "Card", UiSprites.Panel, Color.white);
            Place(sheet.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 840f), new Vector2(0.5f, 0.5f));
            AddShadow(sheet.gameObject);
            overlay.AddComponent<OverlayPop>().Bind(sheet.rectTransform);
            var sheetT = sheet.transform;

            var title = Centred(sheetT, "Title", Rtl.Fix(MatchManager.Title), font, 84, Palette.Gold400, 0f, 330f, 860f, 110f);
            AddOutline(title.gameObject, Palette.Gold900, 3f);
            AddShadow(title.gameObject, 6f);

            var body = Centred(sheetT, "Body", "", font, 30, cream, 0f, 180f, 840f, 170f);
            body.lineSpacing = 1.1f;

            // Three star slots, filled from the right (the first one a Hebrew reader sees).
            var starRow = new GameObject("Stars", typeof(RectTransform));
            starRow.transform.SetParent(sheetT, false);
            Place((RectTransform)starRow.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(420f, 130f), new Vector2(0.5f, 0.5f));
            var stars = new Image[3];
            for (int i = 0; i < stars.Length; i++)
            {
                var star = Sliced(starRow.transform, $"Star{i + 1}", UiSprites.Star, Palette.Navy700);
                float size = i == 1 ? 120f : 100f;
                Place(star.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2((1 - i) * 135f, i == 1 ? 10f : -4f), new Vector2(size, size), new Vector2(0.5f, 0.5f));
                AddOutline(star.gameObject, Palette.Navy900, 3f);
                stars[i] = star;
            }
            starRow.SetActive(false);

            Centred(sheetT, "DifficultyCaption", Rtl.Fix("בחר רמה"), font, 28, soft, 0f, -62f, 600f, 36f);

            string[] tierNames = { "מתחילים", "קל", "בינוני", "קשה" };
            string[] tierHints = { "+ − עד 20", "+ − עד 80", "+ − ×", "× ÷" };
            var tierButtons = new Button[tierNames.Length];
            var tierFaces = new Image[tierNames.Length];
            var tierEdges = new Image[tierNames.Length];
            var tierLabels = new Text[tierNames.Length];
            var tierHintTexts = new Text[tierNames.Length];

            for (int i = 0; i < tierNames.Length; i++)
            {
                // Right to left: the easiest level is the first one on the right.
                var tier = ChunkyButton(sheetT, $"Difficulty{i}", new Vector2((1.5f - i) * 212f, -150f),
                    new Vector2(196f, 104f), UiSprites.NavyFace, UiSprites.NavyEdge);
                var faceT = tier.face.transform;
                tierLabels[i] = Centred(faceT, "Label", Rtl.Fix(tierNames[i]), font, 32, cream, 0f, 13f, 190f, 40f);
                tierHintTexts[i] = Centred(faceT, "Hint", Rtl.Fix(tierHints[i]), font, 22, soft, 0f, -21f, 190f, 28f);

                tierButtons[i] = tier.button;
                tierFaces[i] = tier.face;
                tierEdges[i] = tier.edge;
            }

            var kickOff = ChunkyButton(sheetT, "StartButton", new Vector2(0f, -300f), new Vector2(380f, 112f),
                UiSprites.GoldFace, UiSprites.GoldEdge);
            var buttonLabel = Centred(kickOff.face.transform, "Label", Rtl.Fix(MatchManager.KickOffLabel), font, 42,
                Palette.Navy900, 0f, 0f, 370f, 60f);

            var hud = canvasObject.AddComponent<HudController>();
            hud.Bind(problem, shot, feedback, timerText, fillImage);
            hud.BindScoreBar(scoreValue, streakValue, scorelineValue, matchLine, opponentLine);
            hud.BindOverlay(overlay, title, body, kickOff.button, buttonLabel, starRow, stars, resultPop);
            hud.BindDifficulty(tierButtons, tierFaces, tierEdges, tierLabels, tierHintTexts,
                UiSprites.GoldFace, UiSprites.GoldEdge, UiSprites.NavyFace, UiSprites.NavyEdge);
            EditorUtility.SetDirty(hud);

            return hud;
        }

        // ---------------------------------------------------------------- ui helpers

        /// <summary>
        /// A pill counter in the score bar: icon in a dark circle on the leading
        /// (right) side, a small label over the value. Placed leftward from
        /// <paramref name="right"/>, which is advanced past it.
        /// </summary>
        static Text Counter(Transform bar, string name, string label, Sprite icon, Color iconColour, float width,
            ref float right, Font font)
        {
            var pill = Sliced(bar, name + "Pill", UiSprites.Pill, Palette.Navy700);
            Place(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-right, 0f),
                new Vector2(width, 84f), new Vector2(1f, 0.5f));
            right += width + 12f;

            var badge = Sliced(pill.transform, "IconBadge", UiSprites.Pill, Palette.Navy900);
            Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                new Vector2(62f, 62f), new Vector2(1f, 0.5f));
            FitPill(badge, 62f);
            var glyph = Sliced(badge.transform, "Icon", icon, iconColour);
            Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));

            var caption = Label(pill.transform, name + "Label", Rtl.Fix(label), font, 22,
                Palette.WithAlpha(Palette.Cream, 0.75f), TextAnchor.MiddleRight, Vector2.zero, Vector2.zero);
            Place(caption.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-86f, 17f),
                new Vector2(width - 100f, 28f), new Vector2(1f, 0.5f));

            var value = Label(pill.transform, name + "Value", "0", font, 36, Palette.Cream,
                TextAnchor.MiddleRight, Vector2.zero, Vector2.zero);
            Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-86f, -14f),
                new Vector2(width - 100f, 42f), new Vector2(1f, 0.5f));
            return value;
        }

        struct Chunky
        {
            public Button button;
            public Image face;
            public Image edge;
        }

        /// <summary>
        /// A chunky casual-game button: a darker lower edge showing 8 units below
        /// the face so it looks 3D, and press feedback (sink, tick, buzz).
        /// </summary>
        static Chunky ChunkyButton(Transform parent, string name, Vector2 position, Vector2 size, Sprite face, Sprite edge)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Place((RectTransform)root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size, new Vector2(0.5f, 0.5f));

            var edgeImage = Sliced(root.transform, "Edge", edge, Color.white);
            Stretch(edgeImage.rectTransform);
            edgeImage.rectTransform.offsetMax = new Vector2(0f, -8f);

            var faceImage = Sliced(root.transform, "Face", face, Color.white);
            Stretch(faceImage.rectTransform);
            faceImage.rectTransform.offsetMin = new Vector2(0f, 8f);
            edgeImage.raycastTarget = faceImage.raycastTarget = true;

            var button = root.AddComponent<Button>();
            button.targetGraphic = faceImage;
            var colours = button.colors;
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colours.selectedColor = Color.white;
            button.colors = colours;
            root.AddComponent<PressFeedback>();

            return new Chunky { button = button, face = faceImage, edge = edgeImage };
        }

        /// <summary>An Image, 9-sliced when the sprite has borders (or a flat colour with no sprite).</summary>
        static Image Sliced(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Shrink the pill sprite's round ends to a pill shorter than the sprite;
        /// otherwise the horizontal borders stay full size and the ends go pointy.
        /// </summary>
        static void FitPill(Image image, float height)
        {
            image.pixelsPerUnitMultiplier = UiSprites.Pill.rect.height / height;
        }

        static Text Centred(Transform parent, string name, string content, Font font, int size, Color color,
            float x, float y, float width, float height)
        {
            var text = Label(parent, name, content, font, size, color, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y),
                new Vector2(width, height), new Vector2(0.5f, 0.5f));
            return text;
        }

        static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
            Vector2? pivot = null)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(anchorMin.x, anchorMax.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Full width between the given insets, centred vertically at y.</summary>
        static void Stretch(RectTransform rect, float left, float right, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(-(left + right), height);
            rect.anchoredPosition = new Vector2((left - right) / 2f, y);
        }

        static void AddShadow(GameObject target, float drop = 6f)
        {
            var shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -drop);
        }

        static void AddOutline(GameObject target, Color color, float width)
        {
            var outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(width, -width);
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
            // Never wrap: Hebrew is pre-wrapped by Rtl.Wrap, since wrapping after the
            // RTL reversal would put the end of a sentence on the first line.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
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

        /// <summary>A Standard material in cutout mode, its shape taken from the texture's alpha.</summary>
        static Material GetCutoutMaterial(string name, Color color, Texture2D texture)
        {
            var material = GetMaterial(name, color, 0f, 0.2f);
            material.mainTexture = texture;
            material.SetFloat("_Mode", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
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
