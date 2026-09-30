using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MathStrikers.EditorTools
{
    /// <summary>Prints what came out of the rig import, so materials can be matched by name.</summary>
    public static class CharacterReport
    {
        [MenuItem("Math Strikers/Report Character")]
        public static void Report()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Remy.fbx");
            if (model == null) { Debug.LogError("[Report] no model"); EditorApplication.Exit(1); return; }

            var instance = Object.Instantiate(model);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Debug.Log($"[Report] renderer '{renderer.name}' materials: " +
                          string.Join(", ", renderer.sharedMaterials.Select(m => m == null ? "null" : $"{m.name} (tex={(m.mainTexture ? m.mainTexture.name : "none")})")));
            }

            var animator = instance.GetComponent<Animator>();
            Debug.Log($"[Report] animator={(animator != null)} humanoid={(animator != null && animator.isHuman)}");
            var bounds = instance.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Debug.Log($"[Report] height={bounds.size.y:0.00}m size={bounds.size}");

            Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }
    }
}
