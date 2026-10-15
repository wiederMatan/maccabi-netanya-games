using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Headers.EditorTools
{
    /// <summary>
    /// Imports the Mixamo rig and its animations as Humanoid so the clips retarget
    /// onto the one avatar, and builds the animator controllers the outfield players
    /// and the keeper drive at runtime.
    /// Run headlessly via
    /// -executeMethod Headers.EditorTools.CharacterImporter.Setup
    /// </summary>
    public static class CharacterImporter
    {
        const string Folder = "Assets/Characters";
        const string ModelPath = Folder + "/Remy.fbx";
        public const string PlayerControllerPath = Folder + "/PlayerAnimator.controller";
        public const string KeeperControllerPath = Folder + "/KeeperAnimator.controller";

        // Clip file -> should it loop
        static readonly (string file, bool loop)[] Clips =
        {
            ("Anim_Idle", true),
            ("Anim_Run", true),
            ("Anim_Kick", false),
            ("Anim_Dive", false)
        };

        [MenuItem("Headers/Import Character")]
        public static void Setup()
        {
            if (!ConfigureModel()) { Fail("model import failed"); return; }
            if (!ConfigureClips()) { Fail("clip import failed"); return; }
            if (!BuildControllers()) { Fail("controller build failed"); return; }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Character] Rig, clips and controllers ready.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Fail(string reason)
        {
            Debug.LogError($"[Character] {reason}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>The character itself defines the avatar every clip retargets to.</summary>
        static bool ConfigureModel()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[Character] No model at {ModelPath}");
                return false;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.importNormals = ModelImporterNormals.Import;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid)
            {
                Debug.LogError("[Character] Humanoid avatar was not created - the rig did not map.");
                return false;
            }

            Debug.Log($"[Character] Avatar '{avatar.name}' is valid.");
            return true;
        }

        /// <summary>
        /// Each animation file carries no mesh, so it is imported as Humanoid with
        /// the avatar copied from the character - that is what makes retargeting work.
        /// </summary>
        static bool ConfigureClips()
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null) return false;

            bool ok = true;

            foreach (var (file, loop) in Clips)
            {
                string path = $"{Folder}/{file}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogError($"[Character] No animation at {path}");
                    ok = false;
                    continue;
                }

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;

                var clips = importer.defaultClipAnimations;
                if (clips.Length > 0)
                {
                    clips[0].name = file;
                    clips[0].loopTime = loop;
                    // Every clip is played in place; where the players go is
                    // driven by the game, not by root motion baked into the clip.
                    clips[0].lockRootHeightY = true;
                    clips[0].keepOriginalPositionY = true;
                    importer.clipAnimations = clips;
                }

                importer.SaveAndReimport();

                var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (imported == null)
                {
                    Debug.LogError($"[Character] {file}: no clip came out of the import.");
                    ok = false;
                    continue;
                }

                Debug.Log($"[Character] {file}: {imported.length:0.00}s, loop={loop}");
            }

            return ok;
        }

        static AnimationClip Clip(string file)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"{Folder}/{file}.fbx")
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        /// <summary>
        /// Outfield players (the attacker, the defender and the corner taker) idle,
        /// jog and kick; the jump and the header are posed by Footballer on top of
        /// the idle clip. The keeper idles and dives; a Mirror bool flips the dive
        /// (which only goes to his left), and it plays faster than recorded so he
        /// is stretched out within a header's flight.
        /// </summary>
        static bool BuildControllers()
        {
            var idle = Clip("Anim_Idle");
            var run = Clip("Anim_Run");
            var kick = Clip("Anim_Kick");
            var dive = Clip("Anim_Dive");

            if (idle == null || run == null || kick == null || dive == null)
            {
                Debug.LogError("[Character] Missing a clip needed by the controllers.");
                return false;
            }

            BuildPlayer(idle, run, kick);
            BuildKeeper(idle, dive);
            return true;
        }

        static void BuildPlayer(AnimationClip idle, AnimationClip run, AnimationClip kick)
        {
            AssetDatabase.DeleteAsset(PlayerControllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            var runState = machine.AddState("Run");
            runState.motion = run;

            var kickState = machine.AddState("Kick");
            kickState.motion = kick;
            var back = kickState.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.25f;

            EditorUtility.SetDirty(controller);
        }

        static void BuildKeeper(AnimationClip idle, AnimationClip dive)
        {
            AssetDatabase.DeleteAsset(KeeperControllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(KeeperControllerPath);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            var diveState = machine.AddState("Dive");
            diveState.motion = dive;
            diveState.speed = 1.6f;
            controller.AddParameter("Mirror", AnimatorControllerParameterType.Bool);
            diveState.mirrorParameterActive = true;
            diveState.mirrorParameter = "Mirror";
            AddTrigger(controller, machine, "Dive", diveState, 0.06f);

            EditorUtility.SetDirty(controller);
        }

        /// <summary>From any state, so a trigger is never held up by the clip playing now.</summary>
        static void AddTrigger(UnityEditor.Animations.AnimatorController controller,
            UnityEditor.Animations.AnimatorStateMachine machine, string trigger,
            UnityEditor.Animations.AnimatorState state, float blend)
        {
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

            var into = machine.AddAnyStateTransition(state);
            into.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, trigger);
            into.hasExitTime = false;
            into.duration = blend;
            into.canTransitionToSelf = false;
        }
    }
}
