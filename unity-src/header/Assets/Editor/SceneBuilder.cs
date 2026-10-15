using System.Collections.Generic;
using System.IO;
using Headers;
using MaccabiShared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Headers.EditorTools
{
    /// <summary>
    /// Builds the entire Headers scene from code so the whole game is reproducible
    /// from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod Headers.EditorTools.SceneBuilder.BuildScene
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        public const string ScenePath = ScenesFolder + "/Headers.unity";

        const float GoalWidth = Field.GoalWidth;
        const float GoalHeight = Field.GoalHeight;
        const float GoalLineZ = Field.GoalLineZ;
        const float PostRadius = Field.PostRadius;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        // Maccabi Netanya play in yellow and black; the defender wears red and
        // white and the keeper teal, so a child can tell friend from foe at a glance.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color DefenderRed = new Color(0.82f, 0.12f, 0.12f);
        static readonly Color DefenderWhite = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color KeeperTeal = new Color(0.10f, 0.42f, 0.40f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        // A floodlit evening, like Dribble.
        static readonly Color Sky = new Color(0.06f, 0.13f, 0.25f);

        [MenuItem("Headers/Build Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);
            MaterialCache.Clear();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildAtmosphere();
            BuildPitch();
            BuildGoal();
            BuildMarkings();
            BuildStands();

            var ball = BuildBall();
            var keeper = SpawnPlayer("Keeper", "Keeper", CharacterImporter.KeeperControllerPath, KeeperTeal,
                new Color(0.06f, 0.24f, 0.23f));
            keeper.transform.position = new Vector3(0f, 0f, Field.KeeperLineZ);
            keeper.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var keeperController = keeper.AddComponent<Keeper>();

            var attacker = Outfield("Attacker", "Maccabi", KitYellow, KitBlack, CameraFramer.Spot);
            var defender = Outfield("Defender", "Defender", DefenderRed, DefenderWhite, CameraFramer.Spot + new Vector3(1f, 0f, 0.6f));
            var taker = Outfield("CornerTaker", "Maccabi", KitYellow, KitBlack, Field.CornerSpot(1) + new Vector3(1.3f, 0f, -1.7f));

            var framer = BuildCameraAndLights();
            var (hud, ring) = BuildHud();

            var audioObject = new GameObject("HeaderAudio", typeof(AudioSource), typeof(HeaderAudio));

            var gameObject = new GameObject("HeaderGame");
            var game = gameObject.AddComponent<HeaderGame>();
            game.Bind(ball, attacker, defender, taker, keeperController, hud, ring,
                audioObject.GetComponent<HeaderAudio>(), framer);
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Headers] Scene built and saved to {ScenePath}");
        }

        // ---------------------------------------------------------------- environment

        static void BuildAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 110f;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.47f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.31f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.13f);
        }

        static void BuildPitch()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.localScale = new Vector3(9f, 1f, 9f);
            pitch.transform.position = new Vector3(0f, 0f, GoalLineZ - 20f);
            var grass = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            grass.mainTexture = GrassTexture();
            grass.mainTextureScale = new Vector2(38f, 38f);
            grass.color = new Color(0.82f, 0.82f, 0.82f);
            pitch.GetComponent<Renderer>().sharedMaterial = grass;

            // Mown stripes across the pitch, so its depth reads at a glance.
            var stripe = GetMaterial("PitchStripe", PitchGreen, 0f, 0.08f);
            stripe.mainTexture = GrassTexture();
            stripe.mainTextureScale = new Vector2(20f, 1.2f);
            stripe.color = new Color(0.97f, 0.97f, 0.9f);
            for (int i = 0; i < 9; i++)
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Quad);
                band.name = $"Stripe_{i}";
                band.transform.position = new Vector3(0f, 0.004f, GoalLineZ - 2.75f - i * 5.5f);
                band.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                band.transform.localScale = new Vector3(80f, 2.75f, 1f);
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.GetComponent<Renderer>().sharedMaterial = stripe;
                band.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static void BuildGoal()
        {
            var goal = new GameObject("Goal");
            var postMaterial = GetMaterial("GoalWhite", Chalk, 0f, 0.45f);

            foreach (int side in new[] { -1, 1 })
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = side < 0 ? "PostLeft" : "PostRight";
                post.transform.SetParent(goal.transform);
                post.transform.position = new Vector3(side * GoalWidth / 2f, GoalHeight / 2f, GoalLineZ);
                post.transform.localScale = new Vector3(PostRadius * 2f, GoalHeight / 2f, PostRadius * 2f);
                post.GetComponent<Renderer>().sharedMaterial = postMaterial;
            }

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Crossbar";
            bar.transform.SetParent(goal.transform);
            bar.transform.position = new Vector3(0f, GoalHeight, GoalLineZ);
            bar.transform.localScale = new Vector3(GoalWidth + PostRadius * 2f, PostRadius * 2f, PostRadius * 2f);
            bar.GetComponent<Renderer>().sharedMaterial = postMaterial;

            // Net: a back wall, two wings and a roof, so a goal dies in the mesh.
            var net = GetMaterial("NetWhite", new Color(0.90f, 0.93f, 0.92f, 0.35f), 0f, 0.2f, true);
            Slab(goal.transform, "NetBack", new Vector3(0f, GoalHeight / 2f, GoalLineZ + 1.8f), new Vector3(GoalWidth, GoalHeight, 0.06f), net);
            Slab(goal.transform, "NetLeft", new Vector3(-GoalWidth / 2f, GoalHeight / 2f, GoalLineZ + 0.9f), new Vector3(0.06f, GoalHeight, 1.8f), net);
            Slab(goal.transform, "NetRight", new Vector3(GoalWidth / 2f, GoalHeight / 2f, GoalLineZ + 0.9f), new Vector3(0.06f, GoalHeight, 1.8f), net);
            Slab(goal.transform, "NetRoof", new Vector3(0f, GoalHeight, GoalLineZ + 0.9f), new Vector3(GoalWidth, 0.06f, 1.8f), net);
        }

        static void Slab(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent);
            slab.transform.position = position;
            slab.transform.localScale = scale;
            if (!collider) Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.GetComponent<Renderer>().sharedMaterial = material;
            slab.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildMarkings()
        {
            var markings = new GameObject("Markings");
            var chalk = GetMaterial("ChalkLine", Chalk, 0f, 0.2f);
            const float y = 0.01f;
            Slab(markings.transform, "GoalLine", new Vector3(0f, y, GoalLineZ), new Vector3(70f, 0.01f, 0.12f), chalk, false);
            Slab(markings.transform, "BoxFront", new Vector3(0f, y, GoalLineZ - 16.5f), new Vector3(40.3f, 0.01f, 0.12f), chalk, false);
            Slab(markings.transform, "BoxLeft", new Vector3(-20.15f, y, GoalLineZ - 8.25f), new Vector3(0.12f, 0.01f, 16.5f), chalk, false);
            Slab(markings.transform, "BoxRight", new Vector3(20.15f, y, GoalLineZ - 8.25f), new Vector3(0.12f, 0.01f, 16.5f), chalk, false);
            Slab(markings.transform, "SixLeft", new Vector3(-9.16f, y, GoalLineZ - 2.75f), new Vector3(0.12f, 0.01f, 5.5f), chalk, false);
            Slab(markings.transform, "SixRight", new Vector3(9.16f, y, GoalLineZ - 2.75f), new Vector3(0.12f, 0.01f, 5.5f), chalk, false);
            Slab(markings.transform, "SixFront", new Vector3(0f, y, GoalLineZ - 5.5f), new Vector3(18.32f, 0.01f, 0.12f), chalk, false);

            var spot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spot.name = "PenaltySpot";
            spot.transform.SetParent(markings.transform);
            spot.transform.position = new Vector3(0f, y, GoalLineZ - 11f);
            spot.transform.localScale = new Vector3(0.22f, 0.005f, 0.22f);
            Object.DestroyImmediate(spot.GetComponent<Collider>());
            spot.GetComponent<Renderer>().sharedMaterial = chalk;

            // A corner flag at each end of the byline the corners are taken from.
            var flagPole = GetMaterial("FlagPole", Chalk, 0f, 0.3f);
            var flagCloth = GetMaterial("FlagCloth", KitYellow, 0f, 0.2f);
            foreach (int side in new[] { -1, 1 })
            {
                var corner = Field.CornerSpot(side);
                var basePoint = new Vector3(corner.x + side * 0.9f, 0f, corner.z + 0.5f);
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = side < 0 ? "FlagLeft" : "FlagRight";
                pole.transform.position = basePoint + Vector3.up * 0.75f;
                pole.transform.localScale = new Vector3(0.04f, 0.75f, 0.04f);
                Object.DestroyImmediate(pole.GetComponent<Collider>());
                pole.GetComponent<Renderer>().sharedMaterial = flagPole;

                var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cloth.name = "Cloth";
                cloth.transform.SetParent(pole.transform, true);
                cloth.transform.position = basePoint + new Vector3(side * 0.22f, 1.32f, 0f);
                cloth.transform.localScale = new Vector3(0.42f, 0.28f, 0.02f);
                cloth.transform.rotation = Quaternion.identity;
                Object.DestroyImmediate(cloth.GetComponent<Collider>());
                cloth.GetComponent<Renderer>().sharedMaterial = flagCloth;
            }
        }

        /// <summary>Hoardings in club colours behind the goal, and packed stands behind them and along the sides.</summary>
        static void BuildStands()
        {
            var stands = new GameObject("Stands");
            var boards = GetMaterial("Hoarding", Color.white, 0f, 0.3f);
            boards.mainTexture = HoardingTexture();
            boards.mainTextureScale = new Vector2(10f, 1f);
            var crowd = GetMaterial("Crowd", Color.white, 0f, 0.05f);
            crowd.mainTexture = CrowdTexture();
            crowd.mainTextureScale = new Vector2(10f, 4.4f);
            var sideCrowd = GetMaterial("CrowdSide", Color.white, 0f, 0.05f);
            sideCrowd.mainTexture = CrowdTexture();
            sideCrowd.mainTextureScale = new Vector2(8f, 2f);

            var behind = GameObject.CreatePrimitive(PrimitiveType.Cube);
            behind.name = "HoardingBehind";
            behind.transform.SetParent(stands.transform);
            behind.transform.position = new Vector3(0f, 0.45f, GoalLineZ + 4f);
            behind.transform.localScale = new Vector3(60f, 0.9f, 0.15f);
            Object.DestroyImmediate(behind.GetComponent<Collider>());
            behind.GetComponent<Renderer>().sharedMaterial = boards;

            Stand(stands.transform, "StandBehind", new Vector3(0f, 0.3f, GoalLineZ + 6f), Vector3.back, 90f, 40f, crowd);
            Stand(stands.transform, "StandLeft", new Vector3(-24f, 0.3f, GoalLineZ - 20f), Vector3.right, 70f, 14f, sideCrowd);
            Stand(stands.transform, "StandRight", new Vector3(24f, 0.3f, GoalLineZ - 20f), Vector3.left, 70f, 14f, sideCrowd);
        }

        /// <summary>A raked stand rising away from the pitch, its face toward <paramref name="facing"/>.</summary>
        static void Stand(Transform parent, string name, Vector3 foot, Vector3 facing, float width, float rise, Material material)
        {
            const float slope = 35f;
            float rad = slope * Mathf.Deg2Rad;
            var away = -facing;
            var up = away * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
            var normal = Vector3.Cross(Vector3.Cross(up, facing), up).normalized;
            if (Vector3.Dot(normal, facing) < 0f) normal = -normal;
            var stand = GameObject.CreatePrimitive(PrimitiveType.Quad);
            stand.name = name;
            stand.transform.SetParent(parent);
            stand.transform.position = foot + up * (rise / 2f);
            stand.transform.rotation = Quaternion.LookRotation(-normal, up);
            stand.transform.localScale = new Vector3(width, rise, 1f);
            Object.DestroyImmediate(stand.GetComponent<Collider>());
            var renderer = stand.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- actors

        static Ball BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.position = Field.CornerSpot(1);
            // A touch bigger than a real ball, so it reads on a small screen.
            ball.transform.localScale = Vector3.one * Field.BallRadius * 2f;
            var material = GetMaterial("BallWhite", Color.white, 0f, 0.30f);
            material.mainTexture = BallTexture();
            ball.GetComponent<Renderer>().sharedMaterial = material;

            var body = ball.AddComponent<Rigidbody>();
            body.mass = 0.43f;
            body.linearDamping = 0.15f;
            body.angularDamping = 0.3f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // The flight is scripted through the transform, which interpolation would fight.
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;

            return ball.AddComponent<Ball>();
        }

        static Footballer Outfield(string name, string kitName, Color shirt, Color shorts, Vector3 position)
        {
            var player = SpawnPlayer(name, kitName, CharacterImporter.PlayerControllerPath, shirt, shorts);
            player.transform.position = position;
            player.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            return player.AddComponent<Footballer>();
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";

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
                Debug.LogError($"[Headers] Character model missing at {ModelPath}");
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
                    instance.transform.localScale = Vector3.one * (Field.PlayerHeight / bounds.size.y);
            }

            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
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

        // ---------------------------------------------------------------- camera, lights, hud

        static CameraFramer BuildCameraAndLights()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            // CameraFramer sets the real pose for the window's shape at runtime.
            cameraObject.transform.position = CameraFramer.Spot + new Vector3(0f, 3.4f, -7.5f);
            cameraObject.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;
            cameraObject.AddComponent<AudioListener>();
            var framer = cameraObject.AddComponent<CameraFramer>();

            // Floodlights: a key from high behind the attacker's shoulder, so the
            // shadows fall toward the goal where they help show depth.
            var keyObject = new GameObject("Floodlight");
            keyObject.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.intensity = 1.2f;
            key.shadows = LightShadows.Hard;
            key.shadowStrength = 0.6f;

            var fillObject = new GameObject("Fill");
            fillObject.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.72f, 0.95f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            return framer;
        }

        static (HudController hud, TimingRing ring) BuildHud()
        {
            var font = BuiltinFont();
            UiKit.Build();

            var canvasObject = new GameObject("HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = HudController.LandscapeReference;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            // --- timing ring (under everything else) --------------------------
            var ringRoot = new GameObject("TimingRing", typeof(RectTransform)).GetComponent<RectTransform>();
            ringRoot.SetParent(canvasObject.transform, false);
            ringRoot.anchorMin = ringRoot.anchorMax = ringRoot.pivot = new Vector2(0.5f, 0.5f);
            ringRoot.sizeDelta = new Vector2(150f, 150f);
            var target = SimpleImage(ringRoot, "Target", UiKit.Ring);
            target.raycastTarget = false;
            var closer = SimpleImage(ringRoot, "Closer", UiKit.Ring);
            closer.raycastTarget = false;
            var ring = canvasObject.AddComponent<TimingRing>();
            ring.Bind(ringRoot, target, closer);
            EditorUtility.SetDirty(ring);

            // --- counters (top) ---------------------------------------------------
            // Three pills across the top, over the stand behind the goal. Score
            // leads on the right, as Hebrew reads.
            var counters = new GameObject("Counters", typeof(RectTransform)).GetComponent<RectTransform>();
            counters.SetParent(canvasObject.transform, false);
            counters.anchorMin = counters.anchorMax = new Vector2(0.5f, 1f);
            counters.pivot = new Vector2(0.5f, 1f);
            counters.sizeDelta = new Vector2(700f, 80f);

            var scoreValue = Counter(counters, "Score", UiKit.IconTrophy, UiKit.Gold400, font);
            var goalsValue = Counter(counters, "Goals", UiKit.IconBall, UiKit.Cream, font);
            var cornerValue = Counter(counters, "Corner", UiKit.IconFlag, UiKit.Cream, font);

            // --- toast and hint ----------------------------------------------
            var toast = Label(canvasObject.transform, "Toast", "", font, 84, UiKit.Gold400, TextAnchor.MiddleCenter);
            Centre(toast.rectTransform, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1000f, 120f));
            TitleEffects(toast);
            toast.enabled = false;

            var hint = Label(canvasObject.transform, "Hint", "", font, 34, UiKit.Cream, TextAnchor.MiddleCenter);
            Centre(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(680f, 60f));
            var hintOutline = hint.gameObject.AddComponent<Outline>();
            hintOutline.effectColor = new Color(UiKit.Navy900.r, UiKit.Navy900.g, UiKit.Navy900.b, 0.85f);
            hintOutline.effectDistance = new Vector2(2f, -2f);
            hint.enabled = false;

            // --- overlay: a dim layer and a card that pops in -----------------
            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlay.transform.SetParent(canvasObject.transform, false);
            Fill(overlay.GetComponent<RectTransform>(), 0f, 0f);
            var dim = overlay.GetComponent<Image>();
            dim.color = new Color(UiKit.Navy900.r, UiKit.Navy900.g, UiKit.Navy900.b, 0.72f);

            var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(overlay.transform, false);

            var shadow = SlicedImage(card, "Shadow", UiKit.PanelShadow, Color.white);
            Fill(shadow.rectTransform, -14f, -6f);
            shadow.raycastTarget = false;
            var panel = SlicedImage(card, "Panel", UiKit.Panel, Color.white);
            Fill(panel.rectTransform, 0f, 0f);

            var title = Label(card, "Title", Rtl.Fix("כל הכבוד!"), font, 96, UiKit.Gold400, TextAnchor.MiddleCenter);
            TitleEffects(title);

            // A red "new best" badge riding the top edge of the card.
            var badgeImage = SlicedImage(card, "NewBestBadge", UiKit.Pill, UiKit.Red400);
            var badgeRect = badgeImage.rectTransform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0.5f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(250f, 62f);
            badgeRect.anchoredPosition = Vector2.zero;
            var badgeText = Label(badgeImage.transform, "Label", Rtl.Fix("שיא חדש!"), font, 32, UiKit.Cream, TextAnchor.MiddleCenter);
            Fill(badgeText.rectTransform, 0f, 0f);
            badgeImage.gameObject.SetActive(false);

            // Three star slots; the earned ones fill in on the end card.
            var starRow = new GameObject("StarRow", typeof(RectTransform)).GetComponent<RectTransform>();
            starRow.SetParent(card, false);
            var starFills = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = SimpleImage(starRow, $"Slot_{i}", UiKit.StarSlot);
                slot.rectTransform.sizeDelta = new Vector2(120f, 120f);
                var fill = SimpleImage(slot.transform, "Fill", UiKit.StarFilled);
                Fill(fill.rectTransform, 0f, 0f);
                fill.enabled = false;
                starFills[i] = fill;
            }

            var body = Label(card, "Body", "", font, 34, UiKit.Cream, TextAnchor.MiddleCenter);
            body.lineSpacing = 1.05f;

            var (button, startFace, _) = GameButton(card, "StartButton", UiKit.GoldFace, UiKit.GoldEdge);
            var buttonLabel = Label(startFace.transform, "Label", Rtl.Fix("שחק שוב"), font, 56,
                UiKit.Navy900, TextAnchor.MiddleCenter);
            Fill(buttonLabel.rectTransform, 0f, 0f);
            buttonLabel.rectTransform.anchoredPosition = new Vector2(0f, 2f);

            var hud = canvasObject.AddComponent<HudController>();
            var wiring = new SerializedObject(hud);
            Wire(wiring, "scaler", scaler);
            Wire(wiring, "counters", counters);
            Wire(wiring, "scoreText", scoreValue);
            Wire(wiring, "goalsText", goalsValue);
            Wire(wiring, "cornerText", cornerValue);
            Wire(wiring, "toastText", toast);
            Wire(wiring, "hintText", hint);
            Wire(wiring, "overlay", overlay);
            Wire(wiring, "dim", dim);
            Wire(wiring, "card", card);
            Wire(wiring, "titleText", title);
            Wire(wiring, "badge", badgeImage.gameObject);
            Wire(wiring, "starRow", starRow);
            Wire(wiring, "starFills", starFills);
            Wire(wiring, "bodyText", body);
            Wire(wiring, "startButton", button);
            Wire(wiring, "startButtonLabel", buttonLabel);
            wiring.ApplyModifiedPropertiesWithoutUndo();

            hud.Layout(false);
            // The game opens on the countdown; the card only appears after a round.
            overlay.SetActive(false);
            ringRoot.gameObject.SetActive(false);
            EditorUtility.SetDirty(hud);
            return (hud, ring);
        }

        // ---------------------------------------------------------------- ui helpers

        static void Wire(SerializedObject target, string field, Object value)
        {
            var property = target.FindProperty(field);
            if (property == null) { Debug.LogError($"[Headers] HUD has no field '{field}'."); return; }
            property.objectReferenceValue = value;
        }

        static void Wire(SerializedObject target, string field, Object[] values)
        {
            var property = target.FindProperty(field);
            if (property == null) { Debug.LogError($"[Headers] HUD has no field '{field}'."); return; }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// A chunky game button: a darker lower edge sitting a few pixels below the
        /// face, so it reads as 3D, and press feedback on the root that scales both.
        /// </summary>
        static (Button button, Image face, Image edge) GameButton(Transform parent, string name, Sprite faceSprite, Sprite edgeSprite)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Button), typeof(PressFeedback));
            root.transform.SetParent(parent, false);

            var edge = SlicedImage(root.transform, "Edge", edgeSprite, Color.white);
            Fill(edge.rectTransform, 0f, 0f);
            edge.rectTransform.offsetMin = new Vector2(0f, -6f);
            edge.rectTransform.offsetMax = new Vector2(0f, -6f);
            edge.raycastTarget = false;

            var face = SlicedImage(root.transform, "Face", faceSprite, Color.white);
            Fill(face.rectTransform, 0f, 0f);

            var button = root.GetComponent<Button>();
            button.targetGraphic = face;
            // The press is shown by PressFeedback's squash, not a colour tint.
            button.transition = Selectable.Transition.None;
            return (button, face, edge);
        }

        /// <summary>A navy pill with its icon on the leading (right) side and a number beside it.</summary>
        static Text Counter(RectTransform parent, string name, Sprite icon, Color ink, Font font)
        {
            var pill = SlicedImage(parent, name, UiKit.Pill,
                new Color(UiKit.Navy700.r, UiKit.Navy700.g, UiKit.Navy700.b, 0.92f));
            pill.raycastTarget = false;
            var rect = pill.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var image = SimpleImage(pill.transform, "Icon", icon);
            var iconRect = image.rectTransform;
            iconRect.anchorMin = new Vector2(1f, 0.12f);
            iconRect.anchorMax = new Vector2(1f, 0.88f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.sizeDelta = new Vector2(0f, 0f);
            iconRect.anchoredPosition = new Vector2(-12f, 0f);
            var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = 1f;

            var value = Label(pill.transform, "Value", "0", font, 40, ink, TextAnchor.MiddleCenter);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(10f, 0f);
            valueRect.offsetMax = new Vector2(-62f, 2f);
            return value;
        }

        /// <summary>Gold title look: a dark gold outline and a drop shadow beneath.</summary>
        static void TitleEffects(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = UiKit.Gold900;
            outline.effectDistance = new Vector2(3f, -3f);
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -6f);
        }

        static Image SlicedImage(Transform parent, string name, Sprite sprite, Color colour)
        {
            var image = SimpleImage(parent, name, sprite);
            image.type = Image.Type.Sliced;
            image.color = colour;
            return image;
        }

        static Image SimpleImage(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = false;
            return image;
        }

        /// <summary>Stretch to fill the parent, grown by <paramref name="grow"/> and nudged down by <paramref name="drop"/>.</summary>
        static void Fill(RectTransform rect, float grow, float drop)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-grow, -grow + drop);
            rect.offsetMax = new Vector2(grow, grow + drop);
        }

        /// <summary>
        /// A Text that never wraps: Hebrew is wrapped and reordered by Rtl before
        /// it gets here, and Unity's own wrapping would break it again.
        /// </summary>
        static Text Label(Transform parent, string name, string content, Font font, int size,
            Color color, TextAnchor anchor)
        {
            var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(parent, false);

            var text = labelObject.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        static void Centre(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ---------------------------------------------------------------- textures

        /// <summary>
        /// Procedural grass: fine noise plus a faint blade streak so the pitch has
        /// surface detail instead of reading as a flat green plane.
        /// </summary>
        static Texture2D GrassTexture()
        {
            return SaveTexture("GrassTexture", 256, 256, TextureWrapMode.Repeat, (x, y, random) =>
            {
                float blade = Mathf.PerlinNoise(x * 0.35f, y * 0.08f) * 0.10f;
                float speckle = (float)random.NextDouble() * 0.09f;
                float shade = 0.80f + blade + speckle;
                return new Color(0.24f * shade, 0.55f * shade, 0.27f * shade);
            });
        }

        /// <summary>A classic panelled ball, so its spin is visible in flight.</summary>
        static Texture2D BallTexture()
        {
            return SaveTexture("BallTexture", 256, 256, TextureWrapMode.Repeat, (x, y, random) =>
            {
                int row = y / 43;
                float offset = (row % 2 == 0) ? 0f : 21f;
                float cx = Mathf.Repeat(x + offset, 43f) - 21.5f;
                float cy = Mathf.Repeat(y, 43f) - 21.5f;
                bool panel = (cx * cx + cy * cy) < 118f;
                return panel ? new Color(0.10f, 0.10f, 0.11f) : new Color(0.97f, 0.97f, 0.95f);
            });
        }

        /// <summary>Pitchside hoardings: yellow and black panels with a chevron, club colours.</summary>
        static Texture2D HoardingTexture()
        {
            return SaveTexture("Hoarding", 256, 32, TextureWrapMode.Repeat, (x, y, random) =>
            {
                bool yellowPanel = x < 128;
                int local = x % 128;
                float chevron = Mathf.Abs(local - 64f) * 0.5f + (y - 16f) * 0.9f;
                bool mark = local > 34 && local < 94 && Mathf.Abs(Mathf.Repeat(chevron, 18f) - 9f) < 3f;
                bool trim = y < 2 || y > 29;
                if (trim) return new Color(0.95f, 0.95f, 0.92f);
                bool yellow = yellowPanel != mark;
                return yellow ? new Color(0.98f, 0.82f, 0.09f) : new Color(0.09f, 0.09f, 0.10f);
            });
        }

        /// <summary>
        /// A packed stand seen from the pitch: rows of fans in yellow, black and
        /// white, with the odd gap, over dark terracing.
        /// </summary>
        static Texture2D CrowdTexture()
        {
            Color[] shirts =
            {
                new Color(0.98f, 0.82f, 0.09f), new Color(0.95f, 0.78f, 0.12f), new Color(0.12f, 0.12f, 0.13f),
                new Color(0.92f, 0.92f, 0.90f), new Color(0.98f, 0.82f, 0.09f), new Color(0.20f, 0.30f, 0.55f)
            };
            var fan = new Dictionary<long, Color>();

            return SaveTexture("CrowdTexture", 256, 128, TextureWrapMode.Repeat, (x, y, random) =>
            {
                const int cell = 8;
                int cx = x / cell, cy = y / cell;
                long key = cx * 1000L + cy;
                if (!fan.TryGetValue(key, out var shirt))
                {
                    var r = new System.Random((int)key * 7919 + 17);
                    shirt = r.NextDouble() < 0.12 ? Color.clear : shirts[r.Next(shirts.Length)];
                    fan[key] = shirt;
                }

                var terrace = (cy % 2 == 0) ? new Color(0.16f, 0.18f, 0.22f) : new Color(0.13f, 0.15f, 0.19f);
                if (shirt.a == 0f) return terrace;

                int lx = x % cell, ly = y % cell;
                bool head = ly >= 5 && ly <= 7 && lx >= 3 && lx <= 4;
                bool torso = ly >= 1 && ly <= 4 && lx >= 1 && lx <= 6;
                if (head) return new Color(0.80f, 0.64f, 0.50f);
                if (torso) return shirt;
                return terrace;
            });
        }

        /// <summary>
        /// Paints a texture once and saves it as a PNG asset. Later builds reuse the
        /// saved file, so the textures are stable from build to build.
        /// </summary>
        static Texture2D SaveTexture(string name, int width, int height, TextureWrapMode wrap,
            System.Func<int, int, System.Random, Color> paint)
        {
            string path = $"{MaterialsFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var random = new System.Random(20261015);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    texture.SetPixel(x, y, paint(x, y, random));

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- materials

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
            material.SetFloat("_Mode", 2f);
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
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
