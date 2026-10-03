using System.Collections.Generic;
using System.IO;
using Keeper;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Keeper.EditorTools
{
    /// <summary>
    /// Builds the entire Keeper scene from code so the whole game is reproducible
    /// from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod Keeper.EditorTools.SceneBuilder.BuildScene
    ///
    /// The camera stands behind the goal, looking out over the keeper's shoulders at
    /// the penalty taker, so x runs left to right exactly as the player dives.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        public const string ScenePath = ScenesFolder + "/Keeper.unity";

        const float PostRadius = 0.13f;
        const float NetDepth = 1.8f;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        static readonly Color PitchStripe = new Color(0.12f, 0.34f, 0.17f);
        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        // The player is Maccabi Netanya's keeper, in the club's yellow and black;
        // the visitors' striker is in red and white so the two are never confused.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color AwayRed = new Color(0.78f, 0.12f, 0.14f);
        static readonly Color AwayWhite = new Color(0.94f, 0.94f, 0.92f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        static readonly Color Gold = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color Navy = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color PanelNavy = new Color(0.04f, 0.10f, 0.17f, 0.86f);

        [MenuItem("Keeper/Build Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            var ball = BuildBall();
            var keeper = BuildKeeper();
            var striker = BuildStriker();
            var markers = BuildSpotMarkers();
            var camera = BuildCameraAndLights();
            var hud = BuildHud();

            var audioObject = new GameObject("MatchAudio", typeof(AudioSource), typeof(MatchAudio));

            var gameObject = new GameObject("KeeperGame");
            var game = gameObject.AddComponent<KeeperGame>();
            game.Bind(ball, keeper, striker, markers, hud, audioObject.GetComponent<MatchAudio>(), camera);
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Keeper] Scene built and saved to {ScenePath}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.localScale = new Vector3(8f, 1f, 8f);
            pitch.transform.position = new Vector3(0f, 0f, 12f);
            var pitchMaterial = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            pitchMaterial.mainTexture = GrassTexture();
            pitchMaterial.mainTextureScale = new Vector2(34f, 34f);
            pitchMaterial.color = Color.white;
            pitch.GetComponent<Renderer>().sharedMaterial = pitchMaterial;

            // Mown stripes across the pitch, so its depth reads from behind the goal.
            var stripeMaterial = GetMaterial("PitchStripe", PitchStripe, 0f, 0.08f);
            stripeMaterial.mainTexture = GrassTexture();
            stripeMaterial.mainTextureScale = new Vector2(20f, 1.2f);
            stripeMaterial.color = new Color(0.78f, 0.86f, 0.78f);
            for (int i = -2; i <= 12; i += 2)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Stripe_{i}";
                stripe.transform.localScale = new Vector3(80f, 0.02f, 2.8f);
                stripe.transform.position = new Vector3(0f, 0.01f, i * 2.8f + 1.4f);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
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
            var postMaterial = GetMaterial("GoalWhite", Chalk, 0f, 0.45f);
            float w = Goal.Width, h = Goal.Height;

            BuildPost(goal.transform, "PostLeft", new Vector3(-w / 2f, h / 2f, 0f),
                new Vector3(PostRadius * 2f, h / 2f, PostRadius * 2f), postMaterial);
            BuildPost(goal.transform, "PostRight", new Vector3(w / 2f, h / 2f, 0f),
                new Vector3(PostRadius * 2f, h / 2f, PostRadius * 2f), postMaterial);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Crossbar";
            bar.transform.SetParent(goal.transform);
            bar.transform.position = new Vector3(0f, h, 0f);
            bar.transform.localScale = new Vector3(w + PostRadius * 2f, PostRadius * 2f, PostRadius * 2f);
            bar.GetComponent<Renderer>().sharedMaterial = postMaterial;

            // The net is seen from behind, so it is drawn as an unlit mesh texture: it
            // reads as netting without fogging the view of the keeper and the striker.
            var net = GetUnlitMaterial("Net", NetTexture(), new Color(1f, 1f, 1f, 0.3f));

            NetPanel(goal.transform, "NetBack", new Vector3(0f, h / 2f, -NetDepth),
                Quaternion.identity, new Vector2(w, h), net, new Vector3(w, h, 0.05f));
            NetPanel(goal.transform, "NetRoof", new Vector3(0f, h, -NetDepth / 2f),
                Quaternion.Euler(90f, 0f, 0f), new Vector2(w, NetDepth), net, null);
            foreach (int side in new[] { -1, 1 })
            {
                NetPanel(goal.transform, side < 0 ? "NetLeft" : "NetRight",
                    new Vector3(side * w / 2f, h / 2f, -NetDepth / 2f), Quaternion.Euler(0f, 90f, 0f),
                    new Vector2(NetDepth, h), net, new Vector3(NetDepth, h, 0.05f));

                // Back stanchions, so the net has a frame to hang from.
                BuildPost(goal.transform, side < 0 ? "StanchionLeft" : "StanchionRight",
                    new Vector3(side * w / 2f, h / 2f, -NetDepth), new Vector3(0.06f, h / 2f, 0.06f), postMaterial);
            }
        }

        /// <summary>
        /// One face of the net: a textured quad, plus a thin invisible box for the
        /// ball to hit so a goal ends up in the net instead of sailing through it.
        /// </summary>
        static void NetPanel(Transform parent, string name, Vector3 position, Quaternion rotation,
            Vector2 size, Material material, Vector3? colliderSize)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = name;
            panel.transform.SetParent(parent);
            panel.transform.SetPositionAndRotation(position, rotation);
            panel.transform.localScale = new Vector3(size.x, size.y, 1f);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.GetComponent<Renderer>().sharedMaterial = material;
            panel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (colliderSize == null) return;
            var wall = new GameObject(name + "Wall");
            wall.transform.SetParent(parent);
            wall.transform.SetPositionAndRotation(position, rotation);
            var box = wall.AddComponent<BoxCollider>();
            box.size = colliderSize.Value;
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

            // Goal line, six-yard box and penalty box, drawn as thin slabs.
            AddLine(markings.transform, "GoalLine", new Vector3(0f, 0.012f, 0f), new Vector3(70f, 0.02f, 0.12f), chalk);
            AddLine(markings.transform, "BoxFront", new Vector3(0f, 0.012f, 16.5f), new Vector3(40.3f, 0.02f, 0.12f), chalk);
            AddLine(markings.transform, "BoxLeft", new Vector3(-20.15f, 0.012f, 8.25f), new Vector3(0.12f, 0.02f, 16.5f), chalk);
            AddLine(markings.transform, "BoxRight", new Vector3(20.15f, 0.012f, 8.25f), new Vector3(0.12f, 0.02f, 16.5f), chalk);
            AddLine(markings.transform, "SixLeft", new Vector3(-9.16f, 0.012f, 2.75f), new Vector3(0.12f, 0.02f, 5.5f), chalk);
            AddLine(markings.transform, "SixRight", new Vector3(9.16f, 0.012f, 2.75f), new Vector3(0.12f, 0.02f, 5.5f), chalk);
            AddLine(markings.transform, "SixFront", new Vector3(0f, 0.012f, 5.5f), new Vector3(18.32f, 0.02f, 0.12f), chalk);
            AddLine(markings.transform, "HalfwayLine", new Vector3(0f, 0.012f, 52f), new Vector3(70f, 0.02f, 0.12f), chalk);

            var spot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spot.name = "PenaltySpot";
            spot.transform.SetParent(markings.transform);
            spot.transform.position = new Vector3(0f, 0.012f, Goal.PenaltySpotZ);
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
        /// Hoardings in club colours along the far end, behind the penalty taker, with
        /// the club name repeated along them.
        /// </summary>
        static void BuildAdBoards()
        {
            var boards = new GameObject("AdBoards");
            var font = BuiltinFont();

            var yellowBoard = GetMaterial("BoardYellow", KitYellow, 0f, 0.25f);
            var blackBoard = GetMaterial("BoardBlack", KitBlack, 0f, 0.25f);

            const float boardHeight = 0.9f;
            const float segmentWidth = 7f;
            const float boardZ = 30f;

            for (int i = -5; i <= 5; i++)
            {
                bool yellow = (i & 1) == 0;

                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Board_{i}";
                segment.transform.SetParent(boards.transform);
                segment.transform.position = new Vector3(i * segmentWidth, boardHeight / 2f, boardZ);
                segment.transform.localScale = new Vector3(segmentWidth - 0.15f, boardHeight, 0.2f);
                Object.DestroyImmediate(segment.GetComponent<Collider>());
                segment.GetComponent<Renderer>().sharedMaterial = yellow ? yellowBoard : blackBoard;

                // TextMesh reads correctly from the -z side, which is where the camera sits.
                var caption = new GameObject("Caption");
                caption.transform.SetParent(segment.transform, false);
                // Undo the parent's non-uniform scale so the text is not stretched.
                caption.transform.localScale = new Vector3(
                    0.05f / segment.transform.localScale.x,
                    0.05f / segment.transform.localScale.y,
                    0.05f / segment.transform.localScale.z);
                caption.transform.localPosition = new Vector3(0f, 0f, -0.55f);

                var text = caption.AddComponent<TextMesh>();
                text.text = "MACCABI NETANYA";
                text.font = font;
                text.fontSize = 72;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = yellow ? KitBlack : new Color(0.85f, 0.70f, 0.12f);
                caption.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        static void BuildStands()
        {
            var stands = new GameObject("Stands");
            var concrete = GetMaterial("Concrete", new Color(0.18f, 0.20f, 0.24f), 0f, 0.12f);
            var crowdColors = new[]
            {
                new Color(0.80f, 0.66f, 0.10f), new Color(0.16f, 0.16f, 0.17f),
                new Color(0.70f, 0.58f, 0.14f), new Color(0.48f, 0.48f, 0.47f)
            };
            const float standZ = 36f;

            // Raked: each row a step higher and further back.
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "StandBack";
            back.transform.SetParent(stands.transform);
            back.transform.position = new Vector3(0f, 5.5f, standZ + 6f);
            back.transform.localScale = new Vector3(84f, 11f, 1f);
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.GetComponent<Renderer>().sharedMaterial = concrete;

            // Crowd, stamped in as coloured blocks - cheap, and reads correctly at distance.
            int index = 0;
            for (int row = 0; row < 9; row++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Step_{row}";
                step.transform.SetParent(stands.transform);
                step.transform.position = new Vector3(0f, 0.6f + row * 1.1f, standZ + row * 0.7f);
                step.transform.localScale = new Vector3(84f, 0.35f, 0.7f);
                Object.DestroyImmediate(step.GetComponent<Collider>());
                step.GetComponent<Renderer>().sharedMaterial = concrete;

                for (int col = -21; col <= 21; col++)
                {
                    var fan = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fan.name = "Fan";
                    fan.transform.SetParent(stands.transform);
                    fan.transform.position = new Vector3(
                        col * 1.9f + (row % 2 == 0 ? 0.45f : -0.45f),
                        1.15f + row * 1.1f,
                        standZ + row * 0.7f);
                    fan.transform.localScale = new Vector3(0.7f, 0.75f, 0.4f);
                    Object.DestroyImmediate(fan.GetComponent<Collider>());
                    var renderer = fan.GetComponent<Renderer>();
                    renderer.sharedMaterial = GetMaterial($"Crowd{index % crowdColors.Length}",
                        crowdColors[index % crowdColors.Length], 0f, 0.15f);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    index = (index * 7 + 3) % 101;
                }
            }
        }

        // ---------------------------------------------------------------- actors

        static BallController BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.position = new Vector3(0f, 0.11f, Goal.PenaltySpotZ);
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

            // A little bounce, so a parried ball skips away across the grass.
            const string bouncePath = MaterialsFolder + "/Ball.physicMaterial";
            var bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(bouncePath);
            if (bounce == null)
            {
                bounce = new PhysicsMaterial("Ball");
                AssetDatabase.CreateAsset(bounce, bouncePath);
            }
            bounce.bounciness = 0.45f;
            bounce.dynamicFriction = 0.5f;
            bounce.staticFriction = 0.5f;
            EditorUtility.SetDirty(bounce);
            ball.GetComponent<Collider>().sharedMaterial = bounce;

            return ball.AddComponent<BallController>();
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";
        const string StrikerController_ = "Assets/Characters/StrikerAnimator.controller";
        const string KeeperController_ = "Assets/Characters/KeeperAnimator.controller";
        const float PlayerHeight = 1.82f;

        static GoalkeeperController BuildKeeper()
        {
            var keeper = SpawnPlayer("Goalkeeper", KeeperController_, KitYellow, KitBlack);
            keeper.transform.position = new Vector3(0f, 0f, 0.35f);
            keeper.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            return keeper.AddComponent<GoalkeeperController>();
        }

        static StrikerController BuildStriker()
        {
            var striker = SpawnPlayer("Striker", StrikerController_, AwayRed, AwayWhite);
            striker.transform.position = new Vector3(0.35f, 0f, Goal.PenaltySpotZ + 3.2f);
            striker.transform.rotation = Quaternion.LookRotation(Vector3.back);
            return striker.AddComponent<StrikerController>();
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
                Debug.LogError($"[Keeper] Character model missing at {ModelPath}");
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

        /// <summary>A ring in the goal mouth for every spot a shot can go to.</summary>
        static SpotMarker[] BuildSpotMarkers()
        {
            var root = new GameObject("SpotMarkers");
            var material = GetUnlitMaterial("SpotRing", RingTexture(), Color.white);
            var markers = new SpotMarker[Goal.Spots.Length];

            for (int i = 0; i < markers.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                marker.name = $"Spot_{i}";
                marker.transform.SetParent(root.transform);
                // Just behind the goal line, between the keeper and the camera.
                marker.transform.position = Goal.Spots[i] + new Vector3(0f, 0f, -0.08f);
                marker.transform.localScale = Vector3.one * 1.05f;
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                var renderer = marker.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                markers[i] = marker.AddComponent<SpotMarker>();
                markers[i].Bind(renderer);
                EditorUtility.SetDirty(markers[i]);
            }

            return markers;
        }

        // ---------------------------------------------------------------- camera, lights

        static Camera BuildCameraAndLights()
        {
            // High behind the goal, looking down over the keeper at the taker.
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 5.0f, -7.5f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(new Vector3(0f, 1f, 8f) - cameraObject.transform.position);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<CameraFramer>();

            // Sun over the camera's shoulder, so the striker is lit from the front.
            var sunObject = new GameObject("Sun");
            sunObject.transform.rotation = Quaternion.Euler(48f, 24f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.90f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            var fillObject = new GameObject("Fill");
            fillObject.transform.rotation = Quaternion.Euler(20f, 200f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.76f, 0.92f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.45f, 0.54f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.30f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.13f);

            return camera;
        }

        // ---------------------------------------------------------------- hud

        static HudController BuildHud()
        {
            var font = BuiltinFont();
            var rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

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
            // Along the bottom edge; the page's back and sound buttons float just
            // above its right end (placeCorner in the web template).
            var bar = Panel(canvasObject.transform, "ScoreBar", rounded, PanelNavy);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.offsetMin = new Vector2(24f, 24f);
            barRect.offsetMax = new Vector2(-24f, 24f + 130f);

            string[] captions = { "SAVES", "STREAK", "BEST", "LEVEL" };
            var values = new Text[captions.Length];
            for (int i = 0; i < captions.Length; i++)
            {
                var column = new GameObject(captions[i], typeof(RectTransform));
                column.transform.SetParent(bar.transform, false);
                var columnRect = column.GetComponent<RectTransform>();
                columnRect.anchorMin = new Vector2(i / (float)captions.Length, 0f);
                columnRect.anchorMax = new Vector2((i + 1) / (float)captions.Length, 1f);
                columnRect.offsetMin = Vector2.zero;
                columnRect.offsetMax = Vector2.zero;

                var caption = Label(column.transform, "Caption", captions[i], font, 30, Chalk * 0.72f, TextAnchor.MiddleCenter);
                Stretch(caption.rectTransform, new Vector2(0f, 0.62f), new Vector2(1f, 1f), new Vector2(0f, -8f));
                values[i] = Label(column.transform, "Value", i == 3 ? "Easy" : "0", font, i == 3 ? 44 : 54,
                    i == 0 ? Gold : Chalk, TextAnchor.MiddleCenter);
                values[i].fontStyle = FontStyle.Bold;
                Stretch(values[i].rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.66f), new Vector2(0f, 6f));
            }

            // --- message card (top) ---------------------------------------------
            var card = Panel(canvasObject.transform, "MessageCard", rounded, PanelNavy);
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(1100f, 168f);
            cardRect.anchoredPosition = new Vector2(0f, -24f);

            var message = Label(card.transform, "Message", "", font, 46, Chalk, TextAnchor.MiddleCenter);
            Stretch(message.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 1f), new Vector2(0f, -6f));
            message.rectTransform.offsetMin = new Vector2(24f, message.rectTransform.offsetMin.y);
            message.rectTransform.offsetMax = new Vector2(-24f, message.rectTransform.offsetMax.y);
            // Truncate lets best-fit shrink a long message instead of spilling onto the pips.
            message.verticalOverflow = VerticalWrapMode.Truncate;
            message.resizeTextForBestFit = true;
            message.resizeTextMinSize = 28;
            message.resizeTextMaxSize = 46;

            var pips = new Image[KeeperGame.ShotsPerRound];
            for (int i = 0; i < pips.Length; i++)
            {
                var pip = new GameObject($"Pip_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                pip.transform.SetParent(card.transform, false);
                var rect = pip.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(38f, 38f);
                rect.anchoredPosition = new Vector2((i - (pips.Length - 1) / 2f) * 62f, 36f);
                pips[i] = pip.GetComponent<Image>();
                pips[i].sprite = knob;
                pips[i].color = new Color(1f, 1f, 1f, 0.22f);
                pips[i].raycastTarget = false;
            }

            // --- callout (SAVE! / GOAL) -----------------------------------------
            var callout = Label(canvasObject.transform, "Callout", "", font, 170, Gold, TextAnchor.MiddleCenter);
            callout.fontStyle = FontStyle.Bold;
            callout.horizontalOverflow = HorizontalWrapMode.Overflow;
            var calloutRect = callout.rectTransform;
            calloutRect.anchorMin = calloutRect.anchorMax = new Vector2(0.5f, 0.5f);
            calloutRect.pivot = new Vector2(0.5f, 0.5f);
            calloutRect.sizeDelta = new Vector2(1200f, 240f);
            calloutRect.anchoredPosition = new Vector2(0f, 40f);
            var outline = callout.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(5f, -5f);
            callout.enabled = false;

            // --- overlay --------------------------------------------------------
            var overlay = Panel(canvasObject.transform, "Overlay", null, new Color(0.03f, 0.08f, 0.14f, 0.97f));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            // HudController.Layout stacks everything in here and centres the stack.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(overlay.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(1200f, 10f);

            var title = Label(content.transform, "Title", "KEEPER", font, 110, Gold, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Centre(title.rectTransform, new Vector2(1200f, 130f));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;

            var body = Label(content.transform, "Body", "", font, 40, Chalk * 0.92f, TextAnchor.MiddleCenter);
            Centre(body.rectTransform, new Vector2(1100f, 170f));
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 28;
            body.resizeTextMaxSize = 40;

            var caption_ = Label(content.transform, "LevelCaption", "CHOOSE YOUR LEVEL", font, 32,
                Chalk * 0.7f, TextAnchor.MiddleCenter);
            Centre(caption_.rectTransform, new Vector2(800f, 50f));

            var levelButtons = new Button[Levels.All.Length];
            var levelBackgrounds = new Image[Levels.All.Length];
            var levelLabels = new Text[Levels.All.Length];
            var levelHints = new Text[Levels.All.Length];

            for (int i = 0; i < Levels.All.Length; i++)
            {
                var buttonObject = new GameObject($"Level{Levels.All[i].Name}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(content.transform, false);
                Centre(buttonObject.GetComponent<RectTransform>(), new Vector2(250f, 120f));

                var image = buttonObject.GetComponent<Image>();
                image.sprite = rounded;
                image.type = Image.Type.Sliced;
                var button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;

                var label = Label(buttonObject.transform, "Label", Levels.All[i].Name, font, 46, Chalk, TextAnchor.MiddleCenter);
                label.fontStyle = FontStyle.Bold;
                Centre(label.rectTransform, new Vector2(250f, 60f));
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(0f, 18f);

                var hint = Label(buttonObject.transform, "Hint", Levels.All[i].Hint, font, 28, Chalk, TextAnchor.MiddleCenter);
                Centre(hint.rectTransform, new Vector2(250f, 36f));
                hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hint.rectTransform.anchoredPosition = new Vector2(0f, -28f);

                levelButtons[i] = button;
                levelBackgrounds[i] = image;
                levelLabels[i] = label;
                levelHints[i] = hint;
            }

            var startObject = new GameObject("StartButton",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            startObject.transform.SetParent(content.transform, false);
            Centre(startObject.GetComponent<RectTransform>(), new Vector2(440f, 124f));
            var startImage = startObject.GetComponent<Image>();
            startImage.sprite = rounded;
            startImage.type = Image.Type.Sliced;
            startImage.color = Gold;
            var startButton = startObject.GetComponent<Button>();
            startButton.targetGraphic = startImage;
            var startLabel = Label(startObject.transform, "Label", "Play!", font, 58, Navy, TextAnchor.MiddleCenter);
            startLabel.fontStyle = FontStyle.Bold;
            Stretch(startLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero);

            var hud = canvasObject.AddComponent<HudController>();
            hud.Bind(canvasObject.GetComponent<RectTransform>(), cardRect, message, pips,
                values[0], values[1], values[2], values[3], callout);
            hud.BindOverlay(overlay, contentRect, title, body, caption_.rectTransform, startButton, startLabel,
                levelButtons, levelBackgrounds, levelLabels, levelHints);
            EditorUtility.SetDirty(hud);

            return hud;
        }

        // ---------------------------------------------------------------- ui helpers

        static GameObject Panel(Transform parent, string name, Sprite sprite, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            return panel;
        }

        static Text Label(Transform parent, string name, string content, Font font, int size,
            Color color, TextAnchor anchor)
        {
            var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(parent, false);

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

        /// <summary>Fill the given anchor box, nudged by <paramref name="offset"/>.</summary>
        static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offset)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offset;
            rect.offsetMax = offset;
        }

        /// <summary>A fixed-size box hung from the top centre of its parent (the overlay's stack).</summary>
        static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
        }

        // ---------------------------------------------------------------- textures

        /// <summary>
        /// Procedural grass: fine noise plus a faint blade streak so the pitch has
        /// surface detail instead of reading as a flat green plane.
        /// </summary>
        static Texture2D GrassTexture()
        {
            return CachedTexture("GrassTexture", 256, false, (x, y, random) =>
            {
                float blade = Mathf.PerlinNoise(x * 0.35f, y * 0.08f) * 0.10f;
                float speckle = (float)random.NextDouble() * 0.09f;
                float shade = 0.80f + blade + speckle;
                return new Color(0.24f * shade, 0.55f * shade, 0.27f * shade);
            }, TextureWrapMode.Repeat);
        }

        /// <summary>A classic panelled ball, so spin is visible as the ball flies.</summary>
        static Texture2D BallTexture()
        {
            // Dark panels laid out on a grid, nudged per row so they interlock the
            // way the panels on a real ball do.
            return CachedTexture("BallTexture", 256, false, (x, y, random) =>
            {
                int row = y / 43;
                float offset = (row % 2 == 0) ? 0f : 21f;
                float cx = Mathf.Repeat(x + offset, 43f) - 21.5f;
                float cy = Mathf.Repeat(y, 43f) - 21.5f;
                bool panel = (cx * cx + cy * cy) < 118f;
                return panel ? new Color(0.10f, 0.10f, 0.11f) : new Color(0.97f, 0.97f, 0.95f);
            }, TextureWrapMode.Repeat);
        }

        /// <summary>Square netting: soft white cords on transparent, one mesh per 32 pixels.</summary>
        static Texture2D NetTexture()
        {
            return CachedTexture("NetTexture", 256, true, (x, y, random) =>
            {
                float dx = Mathf.Abs(Mathf.Repeat(x + 0.5f, 32f) - 16f);
                float dy = Mathf.Abs(Mathf.Repeat(y + 0.5f, 32f) - 16f);
                float cord = Mathf.Max(Mathf.Clamp01(dx - 13.5f), Mathf.Clamp01(dy - 13.5f));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(cord * 1.2f));
            }, TextureWrapMode.Repeat, 4f);
        }

        /// <summary>A bold ring with a soft glow inside it, for the spot markers.</summary>
        static Texture2D RingTexture()
        {
            return CachedTexture("RingTexture", 128, true, (x, y, random) =>
            {
                float r = new Vector2(x - 63.5f, y - 63.5f).magnitude / 63.5f;
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.84f) / 0.09f);
                float inner = r < 0.78f ? 0.22f * (1f - r / 0.78f) + 0.08f : 0f;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(ring, inner) * (r < 1f ? 1f : 0f)));
            }, TextureWrapMode.Clamp);
        }

        static Texture2D CachedTexture(string name, int size, bool alpha,
            System.Func<int, int, System.Random, Color> pixel, TextureWrapMode wrap, float tiles = 1f)
        {
            string path = $"{MaterialsFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(size, size, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            var random = new System.Random(20260930);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, pixel(x, y, random));

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.alphaIsTransparency = alpha;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- materials

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        static Material GetMaterial(string name, Color color, float metallic, float smoothness)
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

            EditorUtility.SetDirty(material);
            MaterialCache[name] = material;
            return material;
        }

        /// <summary>The unlit, alpha-blended material the net and the spot rings use.</summary>
        static Material GetUnlitMaterial(string name, Texture2D texture, Color tint)
        {
            if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

            string path = $"{MaterialsFolder}/{name}.mat";
            var shader = Shader.Find("Keeper/UnlitTint");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.mainTexture = texture;
            material.color = tint;
            if (name == "Net") material.mainTextureScale = new Vector2(Goal.Width / 1.0f, Goal.Height / 1.0f) * 0.5f;

            EditorUtility.SetDirty(material);
            MaterialCache[name] = material;
            return material;
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
