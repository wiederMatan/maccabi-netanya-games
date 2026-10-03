using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dribble.EditorTools
{
    /// <summary>
    /// Imports the Mixamo rig and its animations as Humanoid so the clips retarget
    /// onto the one avatar, and builds the animator controllers the runner and the
    /// defenders drive at runtime.
    /// Run headlessly via
    /// -executeMethod Dribble.EditorTools.CharacterImporter.Setup
    /// </summary>
    public static class CharacterImporter
    {
        const string Folder = "Assets/Characters";
        const string ModelPath = Folder + "/Remy.fbx";
        public const string RunnerControllerPath = Folder + "/RunnerAnimator.controller";
        public const string DefenderControllerPath = Folder + "/DefenderAnimator.controller";

        // Clip file -> should it loop
        static readonly (string file, bool loop)[] Clips =
        {
            ("Anim_Idle", true),
            ("Anim_Run", true),
            ("Anim_Dive", false)
        };

        [MenuItem("Dribble/Import Character")]
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
                    // Every clip is played in place; the runner's lane changes are
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
        /// The runner starts idle on the start screen, runs on the "Run" trigger and
        /// tumbles (the dive clip) on "Fall" when a defender wins the ball. Defenders
        /// simply stand their ground in the idle loop.
        /// </summary>
        static bool BuildControllers()
        {
            var idle = Clip("Anim_Idle");
            var run = Clip("Anim_Run");
            var dive = Clip("Anim_Dive");

            if (idle == null || run == null || dive == null)
            {
                Debug.LogError("[Character] Missing a clip needed by the controllers.");
                return false;
            }

            BuildRunner(idle, run, dive);
            BuildDefender(idle);
            return true;
        }

        static void BuildRunner(AnimationClip idle, AnimationClip run, AnimationClip dive)
        {
            AssetDatabase.DeleteAsset(RunnerControllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(RunnerControllerPath);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            var runState = machine.AddState("Run");
            runState.motion = run;

            var fallState = machine.AddState("Fall");
            fallState.motion = dive;

            AddTrigger(controller, machine, "Idle", idleState, 0.2f);
            AddTrigger(controller, machine, "Run", runState, 0.15f);
            AddTrigger(controller, machine, "Fall", fallState, 0.06f);

            EditorUtility.SetDirty(controller);
        }

        static void BuildDefender(AnimationClip idle)
        {
            AssetDatabase.DeleteAsset(DefenderControllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(DefenderControllerPath);
            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

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
