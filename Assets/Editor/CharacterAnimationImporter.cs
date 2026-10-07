using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Turns Josue's Mixamo downloads (FBX, without skin, mixamorig bones) into shared Humanoid
    /// clips and one resident Animator Controller. Humanoid retargeting lets every rigged resident use
    /// the same clips regardless of proportions. Clips are copied out, so the Mixamo files are not
    /// needed at runtime.</summary>
    public static class CharacterAnimationImporter
    {
        public const string Output = "Assets/Resources/Art3D/Characters/Animations";
        public const string ControllerPath = Output + "/ResidentAnimator.controller";

        sealed class ClipSpec
        {
            public readonly string State, File; public readonly bool Loop, InPlace;
            public ClipSpec(string state, string file, bool loop, bool inPlace) { State = state; File = file; Loop = loop; InPlace = inPlace; }
        }
        /// <summary>Animator state → Mixamo download in Assets/TripoModels. Missing files are skipped.</summary>
        static readonly ClipSpec[] Clips =
        {
            new ClipSpec("Idle", "Idle", true, true),
            new ClipSpec("Idle2", "Idle-2", true, true),
            new ClipSpec("Walk", "Walking", true, true),
            new ClipSpec("Run", "Running", true, true),
            new ClipSpec("RunScared", "Running-scared", true, true),
            new ClipSpec("LieDown", "Lying Down", false, false),
            new ClipSpec("Attack", "Attack", false, true),
            new ClipSpec("Hurt", "Hit Reaction", false, true),
        };

        /// <summary>Configures each Mixamo FBX as Humanoid, copies its clip to an .anim asset and
        /// builds the controller. Returns the states that have a clip.</summary>
        public static HashSet<string> Bake()
        {
            Directory.CreateDirectory(Output);
            var states = new Dictionary<string, AnimationClip>();
            foreach (var spec in Clips)
            {
                string path = $"Assets/TripoModels/{spec.File}.fbx";
                if (!File.Exists(path)) continue;
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                var clipDefs = importer.defaultClipAnimations;
                if (clipDefs.Length == 0) continue;
                var clip = clipDefs[0];
                clip.name = spec.State; clip.loopTime = spec.Loop; clip.loopPose = spec.Loop;
                // In-place: the game moves residents. Mixamo locomotion travels forward; leave that travel
                // as root motion (the Animator ignores it) instead of baking it into the pose, where the
                // body would drift ahead each loop and snap back. Facing stays baked into the pose.
                clip.lockRootRotation = spec.InPlace; clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false; clip.keepOriginalPositionXZ = false;
                clip.lockRootHeightY = true; clip.keepOriginalPositionY = false; clip.heightFromFeet = true;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (source == null) continue;
                var copy = Object.Instantiate(source); copy.name = spec.State;
                string clipPath = $"{Output}/{spec.State}.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing != null) { EditorUtility.CopySerialized(copy, existing); Object.DestroyImmediate(copy); copy = existing; }
                else AssetDatabase.CreateAsset(copy, clipPath);
                states[spec.State] = copy;
            }
            if (states.Count == 0) return new HashSet<string>();
            // Rebuild in place: replacing the asset would break the GUID the rig prefabs reference.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var old in machine.states.ToArray()) machine.RemoveState(old.state);
            foreach (var spec in Clips)
                if (states.TryGetValue(spec.State, out var clip))
                {
                    var state = machine.AddState(spec.State); state.motion = clip; state.writeDefaultValues = true;
                    if (spec.State == "Idle") machine.defaultState = state;
                }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            Debug.Log("CharacterAnimations: " + string.Join(", ", states.Keys));
            return new HashSet<string>(states.Keys);
        }
    }
}
