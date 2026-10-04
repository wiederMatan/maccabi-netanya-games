using System.Collections.Generic;
using System.IO;
using Dribble;
using MaccabiShared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dribble.EditorTools
{
    /// <summary>
    /// Builds the entire Dribble scene from code so the whole game is reproducible
    /// from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod Dribble.EditorTools.SceneBuilder.BuildScene
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Materials";
        const string MeshesFolder = "Assets/Meshes";
        public const string ScenePath = ScenesFolder + "/Run.unity";

        // Pool sizes. A row sits every 7 m or more and rows spawn 68 m ahead, so
        // about ten rows are ever live; these cover the busiest level with room over.
        public const int SegmentCount = 6;
        public const int DefenderPool = 10;
        public const int ConePool = 10;
        public const int StarPool = 36;

        static readonly Color PitchGreen = new Color(0.09f, 0.28f, 0.13f);
        // Maccabi Netanya play in yellow and black; the defenders wear red and white
        // so a child can tell friend from foe at a glance.
        static readonly Color KitYellow = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color KitBlack = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color DefenderRed = new Color(0.82f, 0.12f, 0.12f);
        static readonly Color DefenderWhite = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color ConeOrange = new Color(1f, 0.45f, 0.05f);
        static readonly Color StarGold = new Color(1f, 0.80f, 0.15f);
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        // A floodlit evening: the far end of the pitch fades into the night sky.
        static readonly Color Sky = new Color(0.06f, 0.13f, 0.25f);

        [MenuItem("Dribble/Build Run Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(MeshesFolder);
            MaterialCache.Clear();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildAtmosphere();
            BuildGround();
            var course = BuildCourse();
            var runner = BuildRunner();
            var framer = BuildCameraAndLights();
            var hud = BuildHud();

            var audioObject = new GameObject("DribbleAudio", typeof(AudioSource), typeof(DribbleAudio));

            var gameObject = new GameObject("DribbleGame");
            var game = gameObject.AddComponent<DribbleGame>();
            game.Bind(course, runner, hud, audioObject.GetComponent<DribbleAudio>(), framer);
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Dribble] Scene built and saved to {ScenePath}");
        }

        // ---------------------------------------------------------------- environment

        static void BuildAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 28f;
            RenderSettings.fogEndDistance = 78f;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.47f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.31f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.13f);
        }

        /// <summary>A static floor for the loose ball to roll on once the run is over.</summary>
        static void BuildGround()
        {
            var ground = new GameObject("Ground");
            var box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.5f, 20f);
            box.size = new Vector3(60f, 1f, 200f);
        }

        /// <summary>
        /// Six pitch segments end to end, plus the pools of defenders, cones and
        /// stars. Everything sits under the Course, which slides it all toward the
        /// camera.
        /// </summary>
        static Course BuildCourse()
        {
            var root = new GameObject("Course");
            var course = root.AddComponent<Course>();

            var segments = new Transform[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
                segments[i] = BuildSegment(root.transform, i);

            var defenders = new PitchItem[DefenderPool];
            for (int i = 0; i < DefenderPool; i++)
            {
                var defender = SpawnPlayer($"Defender_{i}", CharacterImporter.DefenderControllerPath,
                    "Defender", DefenderRed, DefenderWhite);
                defender.transform.SetParent(root.transform, false);
                defender.transform.localRotation = Quaternion.LookRotation(Vector3.back);
                // Off screen they need not animate; on screen they always do.
                var animator = defender.GetComponent<Animator>();
                if (animator != null) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                defenders[i] = AddItem(defender, ItemKind.Defender);
            }

            var coneMesh = ConeMesh();
            var coneMaterial = GetMaterial("Cone", ConeOrange, 0f, 0.35f);
            var baseMaterial = GetMaterial("ConeBase", new Color(0.12f, 0.12f, 0.13f), 0f, 0.2f);
            var cones = new PitchItem[ConePool];
            for (int i = 0; i < ConePool; i++)
            {
                var cone = new GameObject($"Cone_{i}");
                cone.transform.SetParent(root.transform, false);
                cone.AddComponent<MeshFilter>().sharedMesh = coneMesh;
                cone.AddComponent<MeshRenderer>().sharedMaterial = coneMaterial;
                AddPart(cone.transform, "Base", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f),
                    new Vector3(0.62f, 0.04f, 0.62f), baseMaterial);
                cones[i] = AddItem(cone, ItemKind.Cone);
            }

            var starMesh = StarMesh();
            var starMaterial = GetMaterial("Star", StarGold, 0.2f, 0.75f);
            starMaterial.EnableKeyword("_EMISSION");
            starMaterial.SetColor("_EmissionColor", StarGold * 0.55f);
            starMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var stars = new PitchItem[StarPool];
            for (int i = 0; i < StarPool; i++)
            {
                var star = new GameObject($"Star_{i}");
                star.transform.SetParent(root.transform, false);
                star.AddComponent<MeshFilter>().sharedMesh = starMesh;
                var renderer = star.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = starMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                stars[i] = AddItem(star, ItemKind.Star);
            }

            course.Bind(segments, defenders, cones, stars);
            EditorUtility.SetDirty(course);
            return course;
        }

        static PitchItem AddItem(GameObject target, ItemKind kind)
        {
            var item = target.AddComponent<PitchItem>();
            item.Configure(kind);
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// One 20 m stretch of pitch: grass with a mown stripe across it, the three
        /// lanes chalked out, club-coloured hoardings and a stand of fans each side.
        /// </summary>
        static Transform BuildSegment(Transform parent, int index)
        {
            var segment = new GameObject($"Segment_{index}");
            segment.transform.SetParent(parent, false);
            // Pivot at the segment's centre; the first one starts just behind the camera.
            segment.transform.localPosition = new Vector3(0f, 0f, -10f + index * Course.SegmentLength);

            var grass = GetMaterial("PitchGreen", PitchGreen, 0f, 0.06f);
            grass.mainTexture = GrassTexture();
            grass.mainTextureScale = new Vector2(15f, 5f);
            grass.color = Color.white;
            FlatQuad(segment.transform, "Grass", new Vector3(0f, 0f, 0f), new Vector2(60f, Course.SegmentLength), grass);

            // Stripes across the run, so the speed reads even on an empty stretch.
            var stripe = GetMaterial("PitchStripe", PitchGreen, 0f, 0.08f);
            stripe.mainTexture = GrassTexture();
            stripe.mainTextureScale = new Vector2(15f, 1.25f);
            stripe.color = new Color(1.22f, 1.22f, 1.12f);
            FlatQuad(segment.transform, "StripeA", new Vector3(0f, 0.004f, -7.5f), new Vector2(60f, 5f), stripe);
            FlatQuad(segment.transform, "StripeB", new Vector3(0f, 0.004f, 2.5f), new Vector2(60f, 5f), stripe);

            var lines = GetMaterial("LaneLines", Color.white, 0f, 0.1f, true);
            lines.mainTexture = LaneTexture();
            lines.mainTextureScale = new Vector2(1f, 5f);
            var laneQuad = FlatQuad(segment.transform, "Lanes", new Vector3(0f, 0.008f, 0f),
                new Vector2(LaneSpan, Course.SegmentLength), lines);
            laneQuad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var boards = GetMaterial("Hoarding", Color.white, 0f, 0.3f);
            boards.mainTexture = HoardingTexture();
            boards.mainTextureScale = new Vector2(4f, 1f);
            var crowd = GetMaterial("Crowd", Color.white, 0f, 0.05f);
            crowd.mainTexture = CrowdTexture();
            crowd.mainTextureScale = new Vector2(2f, 1.5f);

            foreach (int side in new[] { -1, 1 })
            {
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = side < 0 ? "HoardingLeft" : "HoardingRight";
                board.transform.SetParent(segment.transform, false);
                board.transform.localPosition = new Vector3(side * 5.6f, 0.45f, 0f);
                board.transform.localScale = new Vector3(0.15f, 0.9f, Course.SegmentLength);
                Object.DestroyImmediate(board.GetComponent<Collider>());
                board.GetComponent<Renderer>().sharedMaterial = boards;

                // A raked stand rising away from the pitch, its face toward the runner.
                const float slope = 38f;
                float rad = slope * Mathf.Deg2Rad;
                const float rise = 14f;
                var up = new Vector3(side * Mathf.Cos(rad), Mathf.Sin(rad), 0f);
                var normal = new Vector3(-side * Mathf.Sin(rad), Mathf.Cos(rad), 0f);
                var stand = GameObject.CreatePrimitive(PrimitiveType.Quad);
                stand.name = side < 0 ? "StandLeft" : "StandRight";
                stand.transform.SetParent(segment.transform, false);
                stand.transform.localPosition = new Vector3(side * 7f, 0.2f, 0f) + up * (rise / 2f);
                stand.transform.localRotation = Quaternion.LookRotation(-normal, up);
                stand.transform.localScale = new Vector3(Course.SegmentLength, rise, 1f);
                Object.DestroyImmediate(stand.GetComponent<Collider>());
                var standRenderer = stand.GetComponent<Renderer>();
                standRenderer.sharedMaterial = crowd;
                standRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return segment.transform;
        }

        // Width of the chalked channel quad: the three lanes plus a little grass.
        const float LaneSpan = 6f;

        static GameObject FlatQuad(Transform parent, string name, Vector3 position, Vector2 size, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            // A Quad faces -z; tipped forward 90 degrees it faces the sky.
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad;
        }

        // ---------------------------------------------------------------- actors

        static Runner BuildRunner()
        {
            var root = new GameObject("Runner");

            var player = SpawnPlayer("Player", CharacterImporter.RunnerControllerPath, "Runner", KitYellow, KitBlack);
            player.transform.SetParent(root.transform, false);

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.SetParent(root.transform, false);
            // A touch bigger than a real ball, so it reads on a small screen.
            ball.transform.localPosition = new Vector3(0.3f, 0.13f, 0.55f);
            ball.transform.localScale = Vector3.one * 0.26f;
            var ballMaterial = GetMaterial("BallWhite", Color.white, 0f, 0.30f);
            ballMaterial.mainTexture = BallTexture();
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            var body = ball.AddComponent<Rigidbody>();
            body.mass = 0.43f;
            body.linearDamping = 0.25f;
            body.angularDamping = 0.6f;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var runner = root.AddComponent<Runner>();
            runner.Bind(player.transform, body);
            EditorUtility.SetDirty(runner);
            return runner;
        }

        const string ModelPath = "Assets/Characters/Remy.fbx";
        const float PlayerHeight = 1.82f;

        /// <summary>
        /// Drops in the rigged character, scales it to a believable height and
        /// dresses it in a kit. The Mixamo rig imports several metres tall, so the
        /// scale is measured from the model rather than hard-coded.
        /// </summary>
        static GameObject SpawnPlayer(string name, string controllerPath, string kitName, Color shirt, Color shorts)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Dribble] Character model missing at {ModelPath}");
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

        // ---------------------------------------------------------------- camera, lights, hud

        static CameraFramer BuildCameraAndLights()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            // CameraFramer sets the real pose for the window's shape at runtime.
            cameraObject.transform.position = new Vector3(0f, 4.2f, -5f);
            cameraObject.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;
            cameraObject.AddComponent<AudioListener>();
            var framer = cameraObject.AddComponent<CameraFramer>();

            // Floodlights: a key from high behind the runner's shoulder, so the
            // shadows fall forward where they help show depth.
            var sunObject = new GameObject("Floodlight");
            sunObject.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.6f;

            var fillObject = new GameObject("Fill");
            fillObject.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.72f, 0.95f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            return framer;
        }

        static HudController BuildHud()
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

            // --- counters (top) ---------------------------------------------------
            // Three pills across the top, where they cover only the far end of the
            // pitch; the bottom of the screen is the runner's, and the page's
            // buttons. Score leads on the right, as Hebrew reads.
            var counters = new GameObject("Counters", typeof(RectTransform)).GetComponent<RectTransform>();
            counters.SetParent(canvasObject.transform, false);
            counters.anchorMin = counters.anchorMax = new Vector2(0.5f, 1f);
            counters.pivot = new Vector2(0.5f, 1f);
            counters.sizeDelta = new Vector2(700f, 80f);

            var scoreValue = Counter(counters, "Score", UiKit.IconTrophy, UiKit.Gold400, font);
            var distanceValue = Counter(counters, "Distance", UiKit.IconBall, UiKit.Cream, font);
            var starsValue = Counter(counters, "Stars", UiKit.StarFilled, UiKit.Cream, font);

            // --- toast and hint ----------------------------------------------
            var toast = Label(canvasObject.transform, "Toast", "", font, 84, UiKit.Gold400, TextAnchor.MiddleCenter);
            Centre(toast.rectTransform, new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(1000f, 120f));
            TitleEffects(toast);
            toast.enabled = false;

            var hint = Label(canvasObject.transform, "Hint", "", font, 34, UiKit.Cream, TextAnchor.MiddleCenter);
            Centre(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(680f, 60f));
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
            Wire(wiring, "distanceText", distanceValue);
            Wire(wiring, "starsText", starsValue);
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
            // The game opens on the countdown; the card only appears after a run.
            overlay.SetActive(false);
            EditorUtility.SetDirty(hud);
            return hud;
        }

        // ---------------------------------------------------------------- ui helpers

        static void Wire(SerializedObject target, string field, Object value)
        {
            var property = target.FindProperty(field);
            if (property == null) { Debug.LogError($"[Dribble] HUD has no field '{field}'."); return; }
            property.objectReferenceValue = value;
        }

        static void Wire(SerializedObject target, string field, Object[] values)
        {
            var property = target.FindProperty(field);
            if (property == null) { Debug.LogError($"[Dribble] HUD has no field '{field}'."); return; }
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

        /// <summary>Fill the parent's width, between two fractions of its height.</summary>
        static Text Stretch(Text text, float bottom, float top)
        {
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        static void Centre(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ---------------------------------------------------------------- meshes

        /// <summary>A training cone: a tapered, open-topped cylinder.</summary>
        static Mesh ConeMesh()
        {
            const int sides = 20;
            const float height = 0.62f;
            const float bottom = 0.26f;
            const float top = 0.035f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            float slope = (bottom - top) / height;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var normal = new Vector3(dir.x, slope, dir.z).normalized;
                vertices.Add(dir * bottom + Vector3.up * 0.04f);
                vertices.Add(dir * top + Vector3.up * height);
                normals.Add(normal);
                normals.Add(normal);
            }

            for (int i = 0; i < sides; i++)
            {
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                triangles.AddRange(new[] { b0, t0, b1, b1, t0, t1 });
            }

            // A cap on top so it is not hollow from above.
            int centre = vertices.Count;
            vertices.Add(Vector3.up * height);
            normals.Add(Vector3.up);
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * top, height, Mathf.Sin(a) * top));
                normals.Add(Vector3.up);
            }
            for (int i = 0; i < sides; i++)
                triangles.AddRange(new[] { centre, centre + 2 + i, centre + 1 + i });

            return SaveMesh("Cone", vertices, normals, triangles);
        }

        /// <summary>A chunky five-pointed star, standing upright, thick enough to read edge-on as it spins.</summary>
        static Mesh StarMesh()
        {
            const int points = 5;
            const float outer = 0.4f;
            const float inner = 0.18f;
            const float depth = 0.07f;

            var outline = new Vector3[points * 2];
            for (int i = 0; i < outline.Length; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / points;
                float r = i % 2 == 0 ? outer : inner;
                outline[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            // Front and back faces, each a fan around a raised centre so the star has
            // a bevelled look under the lights.
            foreach (float face in new[] { -1f, 1f })
            {
                int centre = vertices.Count;
                vertices.Add(new Vector3(0f, 0f, face * depth * 1.6f));
                normals.Add(new Vector3(0f, 0f, face));
                for (int i = 0; i < outline.Length; i++)
                {
                    vertices.Add(outline[i] + new Vector3(0f, 0f, face * depth * 0.5f));
                    normals.Add((new Vector3(0f, 0f, face) + outline[i].normalized * 0.6f).normalized);
                }
                for (int i = 0; i < outline.Length; i++)
                {
                    int a = centre + 1 + i;
                    int b = centre + 1 + (i + 1) % outline.Length;
                    // Clockwise as seen from the side the face points to.
                    if (face < 0f) triangles.AddRange(new[] { centre, b, a });
                    else triangles.AddRange(new[] { centre, a, b });
                }
            }

            // The rim.
            for (int i = 0; i < outline.Length; i++)
            {
                var p0 = outline[i];
                var p1 = outline[(i + 1) % outline.Length];
                var edgeNormal = Vector3.Cross(p1 - p0, Vector3.forward).normalized;
                int start = vertices.Count;
                vertices.Add(p0 + Vector3.back * depth * 0.5f);
                vertices.Add(p1 + Vector3.back * depth * 0.5f);
                vertices.Add(p0 + Vector3.forward * depth * 0.5f);
                vertices.Add(p1 + Vector3.forward * depth * 0.5f);
                for (int k = 0; k < 4; k++) normals.Add(edgeNormal);
                triangles.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }

            return SaveMesh("Star", vertices, normals, triangles);
        }

        static Mesh SaveMesh(string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            string path = $"{MeshesFolder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
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

        /// <summary>A classic panelled ball, so its roll is visible as it is dribbled.</summary>
        static Texture2D BallTexture()
        {
            return SaveTexture("BallTexture", 256, 256, TextureWrapMode.Repeat, (x, y, random) =>
            {
                // Dark panels laid out on a grid, nudged per row so they interlock the
                // way the panels on a real ball do.
                int row = y / 43;
                float offset = (row % 2 == 0) ? 0f : 21f;
                float cx = Mathf.Repeat(x + offset, 43f) - 21.5f;
                float cy = Mathf.Repeat(y, 43f) - 21.5f;
                bool panel = (cx * cx + cy * cy) < 118f;
                return panel ? new Color(0.10f, 0.10f, 0.11f) : new Color(0.97f, 0.97f, 0.95f);
            });
        }

        /// <summary>
        /// Chalk for the three lanes: solid lines down the outside, dashed ones
        /// between. One tile is 4 m of pitch; the dashes are 2.4 m long.
        /// </summary>
        static Texture2D LaneTexture()
        {
            const int width = 256;
            const int height = 64;
            float inner = Course.LaneWidth / 2f / LaneSpan;
            float outerEdge = Course.LaneWidth * 1.5f / LaneSpan;
            const float halfLine = 0.06f / LaneSpan;

            return SaveTexture("LaneLines", width, height, TextureWrapMode.Repeat, (x, y, random) =>
            {
                float u = (x + 0.5f) / width - 0.5f;
                float v = (y + 0.5f) / height;
                float d = Mathf.Abs(u);
                bool solid = Mathf.Abs(d - outerEdge) < halfLine * 1.3f;
                bool dashed = Mathf.Abs(d - inner) < halfLine && v < 0.6f;
                float alpha = solid ? 0.95f : dashed ? 0.75f : 0f;
                return new Color(0.96f, 0.96f, 0.93f, alpha);
            }, alpha: true);
        }

        /// <summary>Pitchside hoardings: yellow and black panels with a chevron, club colours.</summary>
        static Texture2D HoardingTexture()
        {
            return SaveTexture("Hoarding", 256, 32, TextureWrapMode.Repeat, (x, y, random) =>
            {
                bool yellowPanel = x < 128;
                int local = x % 128;
                // A chevron in the middle of each panel.
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
                // Head on top, shoulders below.
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
            System.Func<int, int, System.Random, Color> paint, bool alpha = false, bool sprite = false)
        {
            string path = $"{MaterialsFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(width, height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            var random = new System.Random(20261003);
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
            importer.alphaIsTransparency = alpha;
            if (sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
            }
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
