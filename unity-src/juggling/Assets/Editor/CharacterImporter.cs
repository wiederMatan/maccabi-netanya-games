using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Juggling.EditorTools
{
    /// <summary>
    /// Imports the Mixamo rig and its animations as Humanoid so the clips
    /// retarget onto the one avatar, and builds the animator controller the
    /// cheering player drives at runtime.
    /// Run headlessly via
    /// -executeMethod Juggling.EditorTools.CharacterImporter.Setup
    /// </summary>
    public static class CharacterImporter
    {
        const string Folder = "Assets/Characters";
        const string ModelPath = Folder + "/Remy.fbx";
        const string StrikerControllerPath = Folder + "/StrikerAnimator.controller";

        // Clip file -> should it loop
        static readonly (string file, bool loop)[] Clips =
        {
            ("Anim_Idle", true),
            ("Anim_Run", true),
            ("Anim_Kick", false)
        };

        [MenuItem("Juggling/Import Character")]
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
                    // The run-up and idle are played in place; the character is moved
                    // by the game, not by root motion baked into the clip.
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
        /// A small controller driven by triggers: idle -> run or kick, returning to
        /// idle on its own. The player kicks for joy at the bigger milestones.
        /// </summary>
        static bool BuildControllers()
        {
            var idle = Clip("Anim_Idle");
            var run = Clip("Anim_Run");
            var kick = Clip("Anim_Kick");

            if (idle == null || kick == null)
            {
                Debug.LogError("[Character] Missing a clip needed by the controllers.");
                return false;
            }

            BuildController(StrikerControllerPath, idle,
                ("Kick", kick), run != null ? ("Run", run) : default);
            return true;
        }

        static void BuildController(string path, AnimationClip idle,
            (string trigger, AnimationClip clip) first,
            (string trigger, AnimationClip clip) second)
        {
            AssetDatabase.DeleteAsset(path);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            AddActionState(controller, machine, idleState, first);
            AddActionState(controller, machine, idleState, second);

            EditorUtility.SetDirty(controller);
        }

        static void AddActionState(UnityEditor.Animations.AnimatorController controller,
            UnityEditor.Animations.AnimatorStateMachine machine,
            UnityEditor.Animations.AnimatorState idleState,
            (string trigger, AnimationClip clip) action)
        {
            if (action.clip == null || string.IsNullOrEmpty(action.trigger)) return;

            controller.AddParameter(action.trigger, AnimatorControllerParameterType.Trigger);

            var state = machine.AddState(action.trigger);
            state.motion = action.clip;

            // From any state, not just Idle: the striker is mid-run when the kick
            // fires, and an Idle-only transition made the kick wait for the run
            // cycle to finish, so the ball left before the boot swung.
            var into = machine.AddAnyStateTransition(state);
            into.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, action.trigger);
            into.hasExitTime = false;
            into.duration = 0.08f;
            into.canTransitionToSelf = false;

            var back = state.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.25f;
        }
    }
}
