using System.Collections.Generic;
using System.IO;
using FreeKick;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FreeKick.EditorTools
{
    /// <summary>
    /// Builds the entire Free Kick scene from code so the whole game is
    /// reproducible from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod FreeKick.EditorTools.SceneBuilder.BuildScene
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        public const string ScenePath = ScenesFolder + "/FreeKick.unity";

        // Regulation goal, in metres; the game shares these through Pitch.
        const float GoalWidth = Pitch.GoalWidth;
        const float GoalHeight = Pitch.GoalHeight;
        const float GoalLineZ = Pitch.GoalLineZ;
        const float PostRadius = Pitch.PostRadius;

        public const int WallPlayers = 5;
        public const int GuideDots = 18;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        static readonly Color PitchStripe = new Color(0.12f, 0.34f, 0.17f);
        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        // Maccabi Netanya play in yellow and black. The wall wears the opposition's
        // red and white, and their keeper a contrasting teal, so nobody is confused.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color WallRed = new Color(0.78f, 0.12f, 0.14f);
        static readonly Color WallWhite = new Color(0.93f, 0.93f, 0.91f);
        static readonly Color KeeperTeal = new Color(0.10f, 0.42f, 0.40f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        static readonly Color Gold = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color Navy = new Color(0.05f, 0.11f, 0.17f);

        [MenuItem("Free Kick/Build Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            var ball = BuildBall();
            var keeper = BuildKeeper();
            var striker = BuildStriker();
            var wall = BuildWall();
            var rings = BuildRings();
            var guide = BuildAimGuide();
            var hud = BuildHud();
            var framer = BuildCameraAndLights();

            var audioObject = new GameObject("MatchAudio", typeof(AudioSource), typeof(MatchAudio));

            var managerObject = new GameObject("FreeKickManager");
            var manager = managerObject.AddComponent<FreeKickManager>();
            manager.Bind(ball, keeper, wall, rings, guide, hud, striker.transform, framer,
                audioObject.GetComponent<MatchAudio>());
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Free Kick] Scene built and saved to {ScenePath}");
        }

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.localScale = new Vector3(8f, 1f, 8f);
            pitch.transform.position = new Vector3(0f, 0f, 0f);
            var pitchMaterial = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            pitchMaterial.mainTexture = GrassTexture();
            pitchMaterial.mainTextureScale = new Vector2(34f, 34f);
            pitchMaterial.color = Color.white;
            pitch.GetComponent<Renderer>().sharedMaterial = pitchMaterial;

            // Mown stripes - subtle, but they sell the scale of the pitch and give
            // the eye a sense of how far out the kick is.
            for (int i = -14; i <= 8; i++)
            {
                if (i % 2 != 0) continue;
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Stripe_{i}";
                stripe.transform.localScale = new Vector3(70f, 0.02f, 2.6f);
                stripe.transform.position = new Vector3(0f, 0.01f, GoalLineZ - 12f + i * 2.6f);
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
                new Vector3(PostRadius * 2f, GoalHeight / 2f, PostRadius * 2f), postMaterial);

            BuildPost(goal.transform, "PostRight",
                new Vector3(GoalWidth / 2f, GoalHeight / 2f, GoalLineZ),
                new Vector3(PostRadius * 2f, GoalHeight / 2f, PostRadius * 2f), postMaterial);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Crossbar";
            bar.transform.SetParent(goal.transform);
            bar.transform.position = new Vector3(0f, GoalHeight, GoalLineZ);
            bar.transform.localScale = new Vector3(GoalWidth + PostRadius * 2f, PostRadius * 2f, PostRadius * 2f);
            bar.GetComponent<Renderer>().sharedMaterial = postMaterial;

            // Net: a back wall plus two wings and a roof, so a goal dies in the mesh.
            var netMaterial = GetMaterial("NetWhite", new Color(0.90f, 0.93f, 0.92f, 0.40f), 0f, 0.2f, true);

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

        // ---------------------------------------------------------------- actors

        static BallController BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.position = new Vector3(0f, Pitch.BallRadius, GoalLineZ - 20f);
            ball.transform.localScale = Vector3.one * Pitch.BallRadius * 2f;
            var ballMaterial = GetMaterial("BallWhite", Color.white, 0f, 0.30f);
            ballMaterial.mainTexture = BallTexture();
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            var body = ball.AddComponent<Rigidbody>();
            body.mass = 0.43f;
            body.linearDamping = 0.08f;
            body.angularDamping = 0.2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // The flight is scripted through the transform, which interpolation would fight.
            body.interpolation = RigidbodyInterpolation.None;

            return ball.AddComponent<BallController>();
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";
        const string StrikerController = "Assets/Characters/StrikerAnimator.controller";
        const string KeeperController = "Assets/Characters/KeeperAnimator.controller";
        const float PlayerHeight = Pitch.PlayerHeight;

        static GoalkeeperController BuildKeeper()
        {
            var keeper = SpawnPlayer("Goalkeeper", "Goalkeeper", KeeperController, KeeperTeal, new Color(0.06f, 0.24f, 0.23f));
            keeper.transform.position = new Vector3(0f, 0f, GoalLineZ - 0.35f);
            keeper.transform.rotation = Quaternion.LookRotation(Vector3.back);
            return keeper.AddComponent<GoalkeeperController>();
        }

        static GameObject BuildStriker()
        {
            var striker = SpawnPlayer("Striker", "Striker", StrikerController, KitYellow, KitBlack);
            striker.transform.position = new Vector3(-1.5f, 0f, GoalLineZ - 22.4f);
            striker.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            return striker;
        }

        /// <summary>
        /// Five opposition players for the wall. The level decides how many of them
        /// line up and how tall they stand; the ones not needed are switched off.
        /// They only ever idle, so they share the keeper's controller.
        /// </summary>
        static WallController BuildWall()
        {
            var root = new GameObject("Wall");
            var players = new Transform[WallPlayers];
            for (int i = 0; i < WallPlayers; i++)
            {
                var player = SpawnPlayer($"WallPlayer_{i}", "Wall", KeeperController, WallRed, WallWhite);
                player.transform.SetParent(root.transform, true);
                player.transform.position = new Vector3((i - 2f) * Pitch.WallSpacing, 0f, GoalLineZ - 11f);
                player.transform.rotation = Quaternion.LookRotation(Vector3.back);
                players[i] = player.transform;
            }

            var wall = root.AddComponent<WallController>();
            wall.Bind(players);
            EditorUtility.SetDirty(wall);
            return wall;
        }

        /// <summary>
        /// Drops in the rigged character, scales it to a believable height and
        /// dresses it in a kit. The Mixamo rig imports several metres tall, so the
        /// scale is measured from the model rather than hard-coded.
        /// </summary>
        static GameObject SpawnPlayer(string name, string kitName, string controllerPath, Color shirt, Color shorts)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Free Kick] Character model missing at {ModelPath}");
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

            Dress(instance, kitName, shirt, shorts);
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

        // ---------------------------------------------------------------- rings and aim guide

        /// <summary>A gold ring in each top corner, worth bonus points.</summary>
        static TargetRing[] BuildRings()
        {
            var root = new GameObject("TargetRings");
            var material = GetUnlitMaterial("RingGold", RingTexture("RingGoldTexture", KitYellow, 0.18f));
            var font = BuiltinFont();
            var rings = new TargetRing[2];

            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var ringObject = new GameObject(side < 0 ? "RingLeft" : "RingRight");
                ringObject.transform.SetParent(root.transform);

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Ring";
                quad.transform.SetParent(ringObject.transform, false);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                // Unity's Quad already faces -z, toward the taker.
                quad.GetComponent<Renderer>().sharedMaterial = material;
                quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var labelObject = new GameObject("Points");
                labelObject.transform.SetParent(ringObject.transform, false);
                labelObject.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                labelObject.transform.localScale = Vector3.one * 0.03f;
                var label = labelObject.AddComponent<TextMesh>();
                label.text = $"+{FreeKickManager.RingPoints}";
                label.font = font;
                label.fontSize = 96;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = KitYellow;
                labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                var ring = ringObject.AddComponent<TargetRing>();
                ring.Bind(side, quad.transform);
                ring.Configure(LevelSettings.For(Level.Easy).RingRadius);
                EditorUtility.SetDirty(ring);
                rings[i] = ring;
            }

            return rings;
        }

        /// <summary>The dotted arc and the crossing marker. Unlit, so they read in any light.</summary>
        static AimGuide BuildAimGuide()
        {
            var root = new GameObject("AimGuide");
            var dotMaterial = GetUnlitColorMaterial("GuideDot", new Color(1f, 0.97f, 0.82f));
            var dots = new Transform[GuideDots];

            for (int i = 0; i < GuideDots; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"Dot_{i}";
                dot.transform.SetParent(root.transform);
                dot.transform.localScale = Vector3.one * 0.15f;
                Object.DestroyImmediate(dot.GetComponent<Collider>());
                var renderer = dot.GetComponent<Renderer>();
                renderer.sharedMaterial = dotMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dots[i] = dot.transform;
            }

            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "CrossingMarker";
            marker.transform.SetParent(root.transform);
            marker.transform.localScale = Vector3.one * 0.55f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial =
                GetUnlitMaterial("GuideMarker", RingTexture("GuideMarkerTexture", Color.white, 0f));
            marker.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var guide = root.AddComponent<AimGuide>();
            guide.Bind(dots, marker.transform);
            EditorUtility.SetDirty(guide);
            return guide;
        }

        // ---------------------------------------------------------------- camera, lights, hud

        static CameraFramer BuildCameraAndLights()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 2.3f, GoalLineZ - 27f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(new Vector3(0f, 1.2f, GoalLineZ) - cameraObject.transform.position);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            // Frames every kick so the taker, the wall and the goal fit between the
            // HUD's card and its score bar, in any window shape.
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

            // --- score bar (bottom) ---------------------------------------------
            // The page's back and sound buttons sit just above its right end.
            var bar = Panel(canvasObject.transform, "ScoreBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, HudController.BarHeight),
                new Color(0.05f, 0.11f, 0.17f, 0.85f));
            SetStretchWidth(bar, HudController.BarSide, HudController.BarSide, HudController.BarBottom, HudController.BarHeight);
            bar.GetComponent<Image>().raycastTarget = false;

            Text Column(string name, string caption, float x, string value, Color colour)
            {
                Label(bar.transform, name + "Label", caption, font, 28, Chalk * 0.7f,
                    TextAnchor.MiddleLeft, new Vector2(x, -14f), new Vector2(190f, 34f));
                return Label(bar.transform, name + "Value", value, font, 54, colour,
                    TextAnchor.MiddleLeft, new Vector2(x, -50f), new Vector2(190f, 64f));
            }

            var goalsValue = Column("Goals", "GOALS", 28f, "0/0", Gold);
            var scoreValue = Column("Score", "SCORE", 218f, "0", Chalk);
            var bestValue = Column("Best", "BEST", 408f, "0", Chalk);

            var levelValue = Label(bar.transform, "Level", "EASY", font, 28, Gold,
                TextAnchor.MiddleRight, new Vector2(-28f, -14f), new Vector2(300f, 34f));
            SetAnchor(levelValue.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));

            // One square per kick, filled in gold for a goal and red for a miss.
            var marks = new Image[FreeKickManager.KicksPerRound];
            for (int i = 0; i < marks.Length; i++)
            {
                var mark = Panel(bar.transform, $"Kick_{i}", new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(38f, 38f), HudController.MarkPending);
                var rect = mark.GetComponent<RectTransform>();
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(38f, 38f);
                rect.anchoredPosition = new Vector2(-28f - (marks.Length - 1 - i) * 50f, -62f);
                marks[i] = mark.GetComponent<Image>();
                marks[i].raycastTarget = false;
            }

            // --- message card (top centre) -------------------------------------
            var card = Panel(canvasObject.transform, "MessageCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(920f, HudController.CardHeight),
                new Color(0.05f, 0.11f, 0.17f, 0.85f));
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(920f, HudController.CardHeight);
            cardRect.anchoredPosition = new Vector2(0f, -HudController.CardTop);
            card.GetComponent<Image>().raycastTarget = false;

            var message = Label(card.transform, "Message", "", font, 50, Chalk,
                TextAnchor.MiddleCenter, new Vector2(0f, -12f), new Vector2(880f, 66f));
            SetAnchor(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

            var hint = Label(card.transform, "Hint", "", font, 32, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, -84f), new Vector2(880f, 50f));
            SetAnchor(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

            // --- the big word after each kick ----------------------------------
            var flash = Label(canvasObject.transform, "Flash", "GOAL!", font, 140, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 120f), new Vector2(900f, 180f));
            SetAnchor(flash.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            flash.fontStyle = FontStyle.Bold;
            // Long words like TOP CORNER! shrink to fit a portrait phone's width.
            flash.resizeTextForBestFit = true;
            flash.resizeTextMinSize = 60;
            flash.resizeTextMaxSize = 140;
            var outline = flash.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.06f, 0.10f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);
            flash.gameObject.SetActive(false);

            // --- overlay -------------------------------------------------------
            var overlay = Panel(canvasObject.transform, "Overlay",
                Vector2.zero, Vector2.one, Vector2.zero, new Color(0.04f, 0.09f, 0.14f, 0.92f));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            Text Centred(Transform parent, string name, string content, int size, Color colour,
                TextAnchor anchor, Vector2 position, Vector2 sizeDelta)
            {
                var text = Label(parent, name, content, font, size, colour, anchor, position, sizeDelta);
                SetAnchor(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                return text;
            }

            var title = Centred(overlay.transform, "Title", "FREE KICK", 96, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 330f), new Vector2(940f, 120f));
            title.fontStyle = FontStyle.Bold;

            var body = Centred(overlay.transform, "Body", "", 36, Chalk * 0.9f,
                TextAnchor.MiddleCenter, new Vector2(0f, 165f), new Vector2(900f, 200f));

            Centred(overlay.transform, "LevelCaption", "CHOOSE YOUR LEVEL", 28, Chalk * 0.65f,
                TextAnchor.MiddleCenter, new Vector2(0f, 22f), new Vector2(600f, 36f));

            var levels = LevelSettings.All;
            var levelButtons = new Button[levels.Length];
            var levelBackgrounds = new Image[levels.Length];
            var levelLabels = new Text[levels.Length];
            var levelHints = new Text[levels.Length];

            for (int i = 0; i < levels.Length; i++)
            {
                var levelObject = new GameObject($"Level{levels[i].Name}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                levelObject.transform.SetParent(overlay.transform, false);

                var levelRect = levelObject.GetComponent<RectTransform>();
                SetAnchor(levelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                levelRect.sizeDelta = new Vector2(210f, 100f);
                levelRect.anchoredPosition = new Vector2((i - 1.5f) * 226f, -78f);

                var levelImage = levelObject.GetComponent<Image>();
                levelImage.color = new Color(0.10f, 0.20f, 0.28f);
                var levelButton = levelObject.GetComponent<Button>();
                levelButton.targetGraphic = levelImage;

                var levelLabel = Centred(levelObject.transform, "Label", levels[i].Name, 36,
                    new Color(0.86f, 0.88f, 0.86f), TextAnchor.MiddleCenter, new Vector2(0f, 16f), new Vector2(210f, 44f));
                // Recoloured by HighlightLevel, dark on the picked gold and pale on navy.
                levelHints[i] = Centred(levelObject.transform, "Hint", levels[i].Hint, 24, new Color(0.86f, 0.88f, 0.86f, 0.6f),
                    TextAnchor.MiddleCenter, new Vector2(0f, -22f), new Vector2(210f, 30f));

                levelButtons[i] = levelButton;
                levelBackgrounds[i] = levelImage;
                levelLabels[i] = levelLabel;
            }

            var buttonObject = new GameObject("StartButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(overlay.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            SetAnchor(buttonRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            buttonRect.sizeDelta = new Vector2(360f, 100f);
            buttonRect.anchoredPosition = new Vector2(0f, -232f);
            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = KitYellow;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;

            var buttonLabel = Centred(buttonObject.transform, "Label", "Play", 44, Navy,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(360f, 100f));
            buttonLabel.fontStyle = FontStyle.Bold;

            var hud = canvasObject.AddComponent<HudController>();
            hud.Bind(message, hint, goalsValue, scoreValue, bestValue, levelValue, marks, flash);
            hud.BindOverlay(overlay, title, body, button, buttonLabel, levelButtons, levelBackgrounds, levelLabels, levelHints);
            EditorUtility.SetDirty(hud);

            return hud;
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

        /// <summary>
        /// A soft-edged ring on a transparent square, for the bonus targets and the
        /// aim marker. <paramref name="fill"/> is the opacity of the disc inside it.
        /// </summary>
        static Texture2D RingTexture(string name, Color colour, float fill)
        {
            string path = $"{MaterialsFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            const float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    // Band from 0.80 to 0.98 of the radius, antialiased over a pixel or two.
                    float band = Mathf.Clamp01((r - 0.80f) * 60f) * Mathf.Clamp01((0.98f - r) * 60f);
                    float inside = r < 0.80f ? fill : 0f;
                    float alpha = Mathf.Max(band, inside);
                    texture.SetPixel(x, y, new Color(colour.r, colour.g, colour.b, alpha));
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Unlit and alpha-blended, so the rings glow the same in sun or shade.</summary>
        static Material GetUnlitMaterial(string name, Texture2D texture)
        {
            var material = UnlitAsset(name, "Unlit/Transparent");
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetUnlitColorMaterial(string name, Color colour)
        {
            var material = UnlitAsset(name, "Unlit/Color");
            material.color = colour;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material UnlitAsset(string name, string shader)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(shader);
            return material;
        }

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

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
            // Unity renamed the bundled font; fall back for older editors.
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
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
