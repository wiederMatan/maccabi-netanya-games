using System.Collections.Generic;
using System.IO;
using PenaltyDuel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PenaltyDuel.EditorTools
{
    /// <summary>
    /// Builds the entire Penalty Duel scene from code so the whole game is
    /// reproducible from source - no hand-placed objects to drift out of sync.
    /// Run from the menu, or headlessly via
    /// -executeMethod PenaltyDuel.EditorTools.SceneBuilder.BuildScene
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
        static readonly Color Skin = new Color(0.85f, 0.70f, 0.55f);
        static readonly Color Gold = new Color(0.91f, 0.71f, 0.30f);

        [MenuItem("Penalty Duel/Build Match Scene")]
        public static void BuildScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            var ball = BuildBall();
            var keeper = BuildKeeper();
            var striker = BuildStriker();
            var zones = BuildTargetZones();
            var framer = BuildCameraAndLights();
            var hud = BuildHud(framer);

            // Named for the web page, which mutes it with SendMessage("MatchAudio", ...).
            var audioObject = new GameObject("MatchAudio", typeof(AudioSource), typeof(MatchAudio));

            var managerObject = new GameObject("MatchManager");
            var manager = managerObject.AddComponent<MatchManager>();
            manager.Bind(ball, keeper, zones, hud, striker.transform, framer,
                audioObject.GetComponent<MatchAudio>());
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Penalty Duel] Scene built and saved to {ScenePath}");
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
            // Kits are repainted every kick (KitPainter); these are only the defaults.
            var keeper = SpawnPlayer("Goalkeeper", KeeperController, MatchManager.Player2Colour, Chalk);
            keeper.transform.position = new Vector3(0f, 0f, GoalLineZ - 0.35f);
            keeper.transform.rotation = Quaternion.LookRotation(Vector3.back);
            return keeper.AddComponent<GoalkeeperController>();
        }

        static GameObject BuildStriker()
        {
            // Waiting just behind and left of the ball, like a penalty taker. Keep in
            // step with CameraFramer.WideSubject.
            var striker = SpawnPlayer("Striker", StrikerController, KitYellow, KitBlack);
            striker.transform.position = new Vector3(-1.3f, 0f, -1.2f);
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
                Debug.LogError($"[Penalty Duel] Character model missing at {ModelPath}");
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

        // ---------------------------------------------------------------- target zones

        /// <summary>
        /// Six spots in the goal mouth - top and bottom, left, middle and right - as
        /// see-through boards just in front of the keeper. The shooter and then the
        /// keeper tap them; each carries the number of its key.
        /// </summary>
        static TargetZone[] BuildTargetZones()
        {
            var root = new GameObject("TargetZones");
            var font = BuiltinFont();
            var panelMaterial = GetMaterial("ZonePanel", new Color(1f, 1f, 1f, 0.22f), 0f, 0.1f, true);

            const float CellWidth = GoalWidth / 3f;
            const float CellHeight = GoalHeight / 2f;
            const float PanelZ = GoalLineZ - 0.9f;
            // Where shots finish: inside the posts and under the bar, into the net.
            float[] aimX = { -2.25f, 0f, 2.25f };
            float[] aimY = { 1.78f, 0.42f };

            var zones = new TargetZone[Shootout.Spots];

            for (int i = 0; i < zones.Length; i++)
            {
                int column = Shootout.Column(i);
                int row = Shootout.IsHigh(i) ? 0 : 1;
                float centreX = (column - 1) * CellWidth;
                float centreY = row == 0 ? CellHeight * 1.5f : CellHeight * 0.5f;

                var zoneObject = new GameObject($"Zone_{i + 1}");
                zoneObject.transform.SetParent(root.transform);
                zoneObject.transform.position = new Vector3(centreX, centreY, PanelZ);

                // The tap area covers the whole cell with no gaps, and spills past
                // the frame at the top and bottom so a near miss still counts.
                var collider = zoneObject.AddComponent<BoxCollider>();
                float extraUp = row == 0 ? 0.35f : 0f;
                float extraDown = row == 1 ? 0.25f : 0f;
                collider.size = new Vector3(CellWidth, CellHeight + extraUp + extraDown, 0.2f);
                collider.center = new Vector3(0f, (extraUp - extraDown) / 2f, 0f);
                collider.isTrigger = true;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "Panel";
                panel.transform.SetParent(zoneObject.transform, false);
                panel.transform.localScale = new Vector3(CellWidth - 0.14f, CellHeight - 0.12f, 1f);
                // Unity's Quad already faces -z, which is the camera side.
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                var panelRenderer = panel.GetComponent<Renderer>();
                panelRenderer.sharedMaterial = panelMaterial;
                panelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // TextMesh already reads correctly from the -z side, where the camera is.
                var labelObject = new GameObject("Label");
                labelObject.transform.SetParent(zoneObject.transform, false);
                labelObject.transform.localPosition = new Vector3(0f, 0f, -0.03f);
                labelObject.transform.localScale = Vector3.one * 0.075f;

                var label = labelObject.AddComponent<TextMesh>();
                label.text = (i + 1).ToString();
                label.font = font;
                label.fontSize = 96;
                label.fontStyle = FontStyle.Bold;
                label.characterSize = 1f;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = new Color(1f, 1f, 1f, 0.9f);
                labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                // A dark copy just behind, so the number reads against the crowd and
                // the boards showing through the net.
                var shadowObject = Object.Instantiate(labelObject, labelObject.transform.parent);
                shadowObject.name = "LabelShadow";
                shadowObject.transform.localPosition = new Vector3(0.04f, -0.04f, -0.02f);
                shadowObject.GetComponent<TextMesh>().color = new Color(0.03f, 0.08f, 0.13f, 0.75f);

                var aim = new GameObject("AimPoint");
                aim.transform.SetParent(zoneObject.transform, false);
                aim.transform.position = new Vector3(aimX[column], aimY[row], GoalLineZ + 0.5f);

                var zone = zoneObject.AddComponent<TargetZone>();
                zone.Bind(i, panelRenderer, label, aim.transform);
                EditorUtility.SetDirty(zone);
                zones[i] = zone;
            }

            return zones;
        }

        // ---------------------------------------------------------------- camera, lights, hud

        static CameraFramer BuildCameraAndLights()
        {
            // CameraFramer places and aims the camera at runtime; this is just a
            // sensible starting pose behind the taker for the editor.
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 2.6f, -8f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(new Vector3(0f, 1.0f, GoalLineZ) - cameraObject.transform.position);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 34f;
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

        static readonly Color Navy = new Color(0.05f, 0.11f, 0.17f);

        /// <summary>
        /// The HUD: the shootout scoreboard and the turn prompt along the top, a
        /// score bar along the bottom, the menu / end screen, and the pass-the-phone
        /// screen. HudController moves the top pieces around to suit the screen, so
        /// positions here are only the wide-screen starting layout.
        /// </summary>
        static HudController BuildHud(CameraFramer framer)
        {
            var font = BuiltinFont();
            var ring = SpriteAsset("Ring", (x, y) => Ring(x, y, 0.47f, 0.08f));
            var disc = SpriteAsset("Disc", (x, y) => Disc(x, y, 0.48f));
            var tick = SpriteAsset("Tick", (x, y) => Stroke(x, y, 0.075f,
                new Vector2(0.24f, 0.52f), new Vector2(0.43f, 0.32f), new Vector2(0.77f, 0.70f)));
            var cross = SpriteAsset("Cross", (x, y) => Mathf.Max(
                Stroke(x, y, 0.075f, new Vector2(0.30f, 0.30f), new Vector2(0.70f, 0.70f)),
                Stroke(x, y, 0.075f, new Vector2(0.30f, 0.70f), new Vector2(0.70f, 0.30f))));

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

            // --- scoreboard (top left / top centre) ----------------------------
            var board = Panel(canvasObject.transform, "Scoreboard",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(900f, 200f), Tint(Navy, 0.86f));
            var boardRect = board.GetComponent<RectTransform>();
            boardRect.pivot = new Vector2(0f, 1f);
            boardRect.anchoredPosition = new Vector2(30f, -24f);

            var rows = new ScoreRow[2];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = BuildScoreRow(board.transform, i, font, ring, cross);

            // --- prompt card (top right / under the scoreboard) ----------------
            var card = Panel(canvasObject.transform, "Prompt",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(900f, 200f), Tint(Navy, 0.86f));
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.pivot = new Vector2(1f, 1f);
            cardRect.anchoredPosition = new Vector2(-30f, -24f);

            var promptTitle = Label(card.transform, "Title", "PLAYER 1: SHOOT!", font, 78, Gold,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            Stretch(promptTitle.rectTransform, 20f, 20f, 10f, 96f);
            promptTitle.fontStyle = FontStyle.Bold;
            BestFit(promptTitle, 40, 78);

            var promptBody = Label(card.transform, "Body", "", font, 40, Chalk,
                TextAnchor.UpperCenter, Vector2.zero, Vector2.zero);
            Stretch(promptBody.rectTransform, 24f, 24f, 108f, 80f);
            BestFit(promptBody, 28, 40);

            // --- score bar (bottom) --------------------------------------------
            // The page's back and sound buttons sit just above its right end.
            var bar = Panel(canvasObject.transform, "ScoreBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 110f), Tint(Navy, 0.82f));
            SetStretchWidth(bar, 40f, 40f, 30f, 110f);

            var round = Label(bar.transform, "Round", "KICK 1 OF 5", font, 44, Gold,
                TextAnchor.MiddleLeft, new Vector2(30f, 0f), new Vector2(430f, 110f));
            round.fontStyle = FontStyle.Bold;
            BestFit(round, 28, 44);

            var hint = Label(bar.transform, "Hint", "", font, 36, Tint(Chalk, 0.8f),
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            Stretch(hint.rectTransform, 480f, 30f, 0f, 110f);

            // --- menu / end screen ---------------------------------------------
            var overlay = Panel(canvasObject.transform, "Overlay",
                Vector2.zero, Vector2.one, Vector2.zero, Tint(new Color(0.04f, 0.09f, 0.14f), 0.95f));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var title = Label(overlay.transform, "Title", "PENALTY DUEL", font, 112, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 330f), new Vector2(1400f, 130f));
            SetAnchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            title.fontStyle = FontStyle.Bold;
            BestFit(title, 56, 112);

            var body = Label(overlay.transform, "Body", "", font, 44, Tint(Chalk, 0.92f),
                TextAnchor.MiddleCenter, new Vector2(0f, 225f), new Vector2(1100f, 150f));
            SetAnchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            BestFit(body, 30, 44);

            var portraitObject = new GameObject("WinnerPortrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            portraitObject.transform.SetParent(overlay.transform, false);
            var portraitRect = portraitObject.GetComponent<RectTransform>();
            portraitRect.sizeDelta = new Vector2(210f, 210f);
            portraitRect.anchoredPosition = new Vector2(0f, 30f);
            var portrait = portraitObject.GetComponent<RawImage>();
            portrait.raycastTarget = false;
            portraitObject.SetActive(false);

            var primary = BigButton(overlay.transform, "PrimaryButton", "2 PLAYERS", font, Gold, Navy,
                new Vector2(-330f, -240f), out var primaryLabel);
            var secondary = BigButton(overlay.transform, "SecondaryButton", "VS COMPUTER", font,
                new Color(0.16f, 0.32f, 0.48f), Chalk, new Vector2(330f, -240f), out var secondaryLabel);

            // --- pass the phone --------------------------------------------------
            // Drawn last, so it covers everything - fully opaque, so nothing of the
            // shooter's pick shows through while the phone changes hands.
            var pass = Panel(canvasObject.transform, "PassPhone",
                Vector2.zero, Vector2.one, Vector2.zero, new Color(0.03f, 0.08f, 0.13f, 1f));
            var passRect = pass.GetComponent<RectTransform>();
            passRect.offsetMin = Vector2.zero;
            passRect.offsetMax = Vector2.zero;

            var passTitle = Label(pass.transform, "Title", "PASS TO PLAYER 2", font, 100, Gold,
                TextAnchor.MiddleCenter, new Vector2(0f, 250f), new Vector2(1000f, 130f));
            SetAnchor(passTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            passTitle.fontStyle = FontStyle.Bold;
            BestFit(passTitle, 50, 100);

            var passBody = Label(pass.transform, "Body", "", font, 46, Chalk,
                TextAnchor.MiddleCenter, new Vector2(0f, 80f), new Vector2(940f, 170f));
            SetAnchor(passBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            BestFit(passBody, 30, 46);

            var passButton = BigButton(pass.transform, "ReadyButton", "READY TO SAVE!", font, Gold, Navy,
                new Vector2(0f, -150f), out var passLabel);
            ((RectTransform)passButton.transform).sizeDelta = new Vector2(660f, 150f);
            pass.SetActive(false);

            var hud = canvasObject.AddComponent<HudController>();
            hud.BindLayout(canvasObject.GetComponent<RectTransform>(), framer);
            hud.BindScoreboard(boardRect, rows, ring, disc, tick, cross);
            hud.BindPrompt(cardRect, promptTitle, promptBody, round, hint);
            hud.BindOverlay(overlay, title, body, portrait, primary, primaryLabel, secondary, secondaryLabel);
            hud.BindPass(pass, passTitle, passBody, passButton, passButton.GetComponent<Image>(), passLabel);
            EditorUtility.SetDirty(hud);

            return hud;
        }

        /// <summary>
        /// One scoreboard row: colour stripe, squad portrait, name, five kick marks
        /// and the goal count. Marks and goals hang off the right edge and the name
        /// takes what is left, so the row survives the board changing width.
        /// </summary>
        static ScoreRow BuildScoreRow(Transform board, int index, Font font, Sprite ring, Sprite cross)
        {
            const float RowHeight = 86f;
            Color colour = index == 0 ? MatchManager.Player1Colour : MatchManager.Player2Colour;

            var rowObject = Panel(board, $"Row{index + 1}", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, RowHeight), new Color(1f, 1f, 1f, 0.04f));
            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(10f, 0f);
            rowRect.offsetMax = new Vector2(-10f, 0f);
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, RowHeight);
            rowRect.anchoredPosition = new Vector2(0f, -10f - index * (RowHeight + 8f));

            var row = new ScoreRow { background = rowObject.GetComponent<Image>() };

            var stripe = Panel(rowObject.transform, "Stripe", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(12f, 0f), colour);
            var stripeRect = stripe.GetComponent<RectTransform>();
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.offsetMin = new Vector2(0f, 0f);
            stripeRect.offsetMax = new Vector2(12f, 0f);
            row.stripe = stripe.GetComponent<Image>();

            // Head and shoulders only: the club portraits are waist-up on a clear ground.
            var portraitObject = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            portraitObject.transform.SetParent(rowObject.transform, false);
            var portraitRect = portraitObject.GetComponent<RectTransform>();
            SetAnchor(portraitRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            portraitRect.sizeDelta = new Vector2(78f, 78f);
            portraitRect.anchoredPosition = new Vector2(22f, 0f);
            row.portrait = portraitObject.GetComponent<RawImage>();
            row.portrait.uvRect = new Rect(0.22f, 0.44f, 0.56f, 0.56f);
            row.portrait.raycastTarget = false;

            row.name = Label(rowObject.transform, "Name", index == 0 ? "PLAYER 1" : "PLAYER 2", font, 42, Chalk,
                TextAnchor.LowerLeft, Vector2.zero, Vector2.zero);
            row.name.fontStyle = FontStyle.Bold;
            Stretch(row.name.rectTransform, 112f, 450f, 2f, 48f);
            BestFit(row.name, 28, 42);

            row.squadName = Label(rowObject.transform, "Squad", "", font, 30, Tint(Chalk, 0.7f),
                TextAnchor.UpperLeft, Vector2.zero, Vector2.zero);
            Stretch(row.squadName.rectTransform, 112f, 450f, 50f, 34f);
            BestFit(row.squadName, 20, 30);

            row.goals = Label(rowObject.transform, "Goals", "0", font, 66, Gold,
                TextAnchor.MiddleRight, Vector2.zero, new Vector2(100f, RowHeight));
            row.goals.fontStyle = FontStyle.Bold;
            SetAnchor(row.goals.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            row.goals.rectTransform.anchoredPosition = new Vector2(-16f, 0f);

            row.marks = new Image[Shootout.Regulation];
            row.glyphs = new Image[Shootout.Regulation];
            for (int slot = 0; slot < row.marks.Length; slot++)
            {
                var markObject = new GameObject($"Kick{slot + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                markObject.transform.SetParent(rowObject.transform, false);
                var markRect = markObject.GetComponent<RectTransform>();
                SetAnchor(markRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
                markRect.sizeDelta = new Vector2(54f, 54f);
                markRect.anchoredPosition = new Vector2(-150f - (row.marks.Length - 1 - slot) * 60f, 0f);
                var mark = markObject.GetComponent<Image>();
                mark.sprite = ring;
                mark.color = new Color(0.95f, 0.95f, 0.92f, 0.35f);
                mark.raycastTarget = false;

                var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                glyphObject.transform.SetParent(markObject.transform, false);
                var glyphRect = glyphObject.GetComponent<RectTransform>();
                glyphRect.anchorMin = Vector2.zero;
                glyphRect.anchorMax = Vector2.one;
                glyphRect.offsetMin = Vector2.zero;
                glyphRect.offsetMax = Vector2.zero;
                var glyph = glyphObject.GetComponent<Image>();
                glyph.sprite = cross;
                glyph.color = Color.white;
                glyph.raycastTarget = false;
                glyph.enabled = false;

                row.marks[slot] = mark;
                row.glyphs[slot] = glyph;
            }

            return row;
        }

        static Button BigButton(Transform parent, string name, string text, Font font, Color fill, Color ink,
            Vector2 position, out Text label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            SetAnchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rect.sizeDelta = new Vector2(600f, 130f);
            rect.anchoredPosition = position;

            var image = buttonObject.GetComponent<Image>();
            image.color = fill;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            label = Label(buttonObject.transform, "Label", text, font, 54, ink, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.zero);
            label.fontStyle = FontStyle.Bold;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 0f);
            labelRect.offsetMax = new Vector2(-16f, 0f);
            BestFit(label, 32, 54);
            return button;
        }

        static Color Tint(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        /// <summary>Stretch across the parent's width, at a fixed height down from its top.</summary>
        static void Stretch(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Shrink long text to fit rather than spill out of its box.</summary>
        static void BestFit(Text text, int min, int max)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = max;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ---------------------------------------------------------------- ui sprites

        const string SpritesFolder = MaterialsFolder + "/UI";

        /// <summary>
        /// A white anti-aliased shape, drawn once into a PNG and imported as a sprite
        /// so UI images can tint it. <paramref name="coverage"/> maps a point in the
        /// unit square to 0-1 alpha.
        /// </summary>
        static Sprite SpriteAsset(string name, System.Func<float, float, float> coverage)
        {
            EnsureFolder(SpritesFolder);
            string path = $"{SpritesFolder}/{name}.png";

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = Mathf.Clamp01(coverage((x + 0.5f) / size, (y + 0.5f) / size));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // Edge softness, in unit-square units, for the drawn shapes.
        const float Feather = 1.5f / 128f;

        static float Disc(float x, float y, float radius)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
            return (radius - d) / Feather + 0.5f;
        }

        static float Ring(float x, float y, float radius, float thickness)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
            return (thickness * 0.5f - Mathf.Abs(d - (radius - thickness * 0.5f))) / Feather + 0.5f;
        }

        /// <summary>A thick round-capped polyline through the given points.</summary>
        static float Stroke(float x, float y, float halfWidth, params Vector2[] points)
        {
            var p = new Vector2(x, y);
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + t * (b - a)));
            }
            return (halfWidth - best) / Feather + 0.5f;
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
