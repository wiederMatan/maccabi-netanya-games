using System.IO;
using MathStrikers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MathStrikers.EditorTools
{
    /// <summary>
    /// Renders the match scene to a PNG so the presentation can be checked without
    /// opening the editor. The HUD canvas is temporarily switched to camera space
    /// so overlay UI lands in the capture too.
    /// </summary>
    public static class ScreenshotTool
    {
        const string ScenePath = "Assets/Scenes/Match.unity";
        const int Width = 1600;
        const int Height = 900;

        [MenuItem("Math Strikers/Capture Screenshot")]
        public static void Capture()
        {
            string output = CommandLineArg("-outputPath") ?? "Builds/match-preview.png";

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[Screenshot] No camera in the scene.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Populate the HUD with representative values so the capture shows the
            // game mid-match rather than an empty shell.
            var hud = Object.FindFirstObjectByType<HudController>();
            if (hud != null)
            {
                hud.SetProblem("7 × 8 = ?");
                hud.SetScore(140);
                hud.SetStreak(3);
                hud.SetScoreline(3, 1);
                hud.SetBanner("Match 2 — vs Vantage United");
                hud.SetFeedback("GOAL! Streak ×3 — +38 points");
                hud.SetTimer(21f, MatchManager.AnswerSeconds);
                hud.SetStriker(Roster.Squad[6]);

                // -showOverlay captures the start menu instead of live play.
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-showOverlay") >= 0)
                {
                    hud.ShowOverlay("MATH STRIKERS",
                        "Every shot brings a math problem and thirty seconds on the clock. " +
                        "Solve it, then strike the board holding the right answer.",
                        "Kick Off");
                    hud.HighlightDifficulty(1);
                }
                else
                {
                    hud.HideOverlay();
                }

            }

            var zones = Object.FindObjectsByType<TargetZone>(FindObjectsSortMode.None);
            int[] sample = { 54, 56, 63 };
            for (int i = 0; i < zones.Length; i++)
            {
                zones[i].Configure(i, new Color(0.07f, 0.17f, 0.27f, 0.92f), Color.white);
                zones[i].SetValue(sample[i % sample.Length]);
            }

            var canvas = Object.FindFirstObjectByType<Canvas>();
            RenderMode previousMode = RenderMode.ScreenSpaceOverlay;
            if (canvas != null)
            {
                previousMode = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
            }

            var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };

            camera.targetTexture = texture;
            camera.Render();

            RenderTexture.active = texture;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;
            if (canvas != null) canvas.renderMode = previousMode;

            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(output, image.EncodeToPNG());

            Object.DestroyImmediate(image);
            texture.Release();
            Object.DestroyImmediate(texture);

            Debug.Log($"[Screenshot] Wrote {output}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string CommandLineArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
