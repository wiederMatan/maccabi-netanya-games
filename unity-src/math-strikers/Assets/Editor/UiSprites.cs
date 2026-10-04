using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MathStrikers.EditorTools
{
    /// <summary>
    /// Draws the HUD's rounded panels, chunky buttons, pills, stars and icons as
    /// PNGs under Assets/UI/Generated and imports them as 9-sliced sprites, so the
    /// whole look is regenerated from code along with the scene.
    /// Sizes are in canvas units at the 1080p reference (1 texture pixel = 1 unit).
    /// </summary>
    public static class UiSprites
    {
        const string Folder = "Assets/UI/Generated";

        public static Sprite Panel { get; private set; }
        public static Sprite Pill { get; private set; }
        public static Sprite GoldFace { get; private set; }
        public static Sprite GoldEdge { get; private set; }
        public static Sprite NavyFace { get; private set; }
        public static Sprite NavyEdge { get; private set; }
        public static Sprite Star { get; private set; }
        public static Sprite Bolt { get; private set; }
        public static Sprite Ball { get; private set; }
        public static Texture2D Board { get; private set; }

        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/UI", "Generated");

            // Panel: navy-800 at 92%, 4 px gold border at 70%, radius 28.
            Panel = SaveSprite("Panel", RoundRect(96, 96, 28, 4,
                _ => Palette.WithAlpha(Palette.Navy800, 0.92f), Palette.WithAlpha(Palette.Gold400, 0.7f)), 34);

            // White pill, tinted per use (counters, timer track and fill).
            Pill = SaveSprite("Pill", RoundRect(80, 80, 40, 0, _ => Color.white, Color.white), 39);

            // Buttons: gradient face with a 3 px dark outline, and a darker block
            // that shows 8 px below it as the 3D lower edge.
            GoldFace = SaveSprite("ButtonGoldFace", RoundRect(128, 128, 26, 3,
                v => Color.Lerp(Palette.Gold600, Palette.Gold400, v), Palette.Gold900), 34);
            GoldEdge = SaveSprite("ButtonGoldEdge", RoundRect(128, 128, 26, 3,
                _ => Darken(Palette.Gold600, 0.85f), Palette.Gold900), 34);
            NavyFace = SaveSprite("ButtonNavyFace", RoundRect(128, 128, 26, 3,
                v => Color.Lerp(Darken(Palette.Navy700, 0.85f), Lighten(Palette.Navy700, 0.12f), v), Palette.Navy900), 34);
            NavyEdge = SaveSprite("ButtonNavyEdge", RoundRect(128, 128, 26, 3,
                _ => Palette.Navy900, Palette.Navy900), 34);

            Star = SaveSprite("Star", Polygon(128, StarPoints(64f, 64f, 60f, 27f)), 0);
            Bolt = SaveSprite("Bolt", Polygon(96, new[]
            {
                new Vector2(56, 94), new Vector2(22, 44), new Vector2(46, 44),
                new Vector2(38, 2), new Vector2(76, 56), new Vector2(52, 56), new Vector2(72, 94)
            }), 0);
            Ball = SaveSprite("Ball", BallIcon(96), 0);

            Board = SaveTexture("AnswerBoard", RoundRect(256, 112, 26, 0, _ => Color.white, Color.white));
        }

        // ------------------------------------------------------------- shapes

        /// <summary>
        /// Anti-aliased rounded rectangle: an outline ring of the given width around
        /// a fill whose colour can vary from bottom (0) to top (1).
        /// </summary>
        static Texture2D RoundRect(int width, int height, float radius, float outline,
            Func<float, Color> fill, Color outlineColour)
        {
            var texture = NewTexture(width, height);
            var pixels = new Color[width * height];
            var half = new Vector2(width / 2f, height / 2f);

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - half;
                float outer = Mathf.Clamp01(0.5f - RoundRectDistance(p, half, radius));
                float inner = outline <= 0f
                    ? outer
                    : Mathf.Clamp01(0.5f - RoundRectDistance(p, half - Vector2.one * outline, Mathf.Max(1f, radius - outline)));

                Color face = fill((y + 0.5f) / height);
                Color colour = Color.Lerp(outlineColour, face, inner);
                colour.a = outer * Mathf.Lerp(outlineColour.a, face.a, inner);
                pixels[y * width + x] = colour;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static float RoundRectDistance(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - Vector2.one * radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        /// <summary>A white filled polygon, 4x4 supersampled for smooth edges.</summary>
        static Texture2D Polygon(int size, Vector2[] points)
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (Inside(new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f), points)) hits++;
                pixels[y * size + x] = new Color(1f, 1f, 1f, hits / 16f);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static Vector2[] StarPoints(float cx, float cy, float outer, float inner)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i & 1) == 0 ? outer : inner;
                // Nudged down a touch so the star sits optically centred.
                points[i] = new Vector2(cx + Mathf.Cos(angle) * r, cy - 4f + Mathf.Sin(angle) * r);
            }
            return points;
        }

        /// <summary>A football: white disc, navy rim and a navy pentagon in the middle.</summary>
        static Texture2D BallIcon(int size)
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float c = size / 2f, radius = size / 2f - 3f;
            var pentagon = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.PI / 2f + i * 2f * Mathf.PI / 5f;
                pentagon[i] = new Vector2(c + Mathf.Cos(angle) * radius * 0.42f, c + Mathf.Sin(angle) * radius * 0.42f);
            }

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = (p - new Vector2(c, c)).magnitude;
                float disc = Mathf.Clamp01(radius + 0.5f - d);
                float rim = Mathf.Clamp01(d - (radius - 6f) + 0.5f);
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (Inside(new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f), pentagon)) hits++;
                float patch = Mathf.Max(rim, hits / 16f);
                var colour = Color.Lerp(Color.white, Palette.Navy900, patch);
                colour.a = disc;
                pixels[y * size + x] = colour;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            }
            return inside;
        }

        static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);

        // ------------------------------------------------------------- assets

        static Texture2D NewTexture(int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false);

        static Sprite SaveSprite(string name, Texture2D texture, int border)
        {
            string path = Write(name, texture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Texture2D SaveTexture(string name, Texture2D texture)
        {
            string path = Write(name, texture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Write(string name, Texture2D texture)
        {
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }
    }
}
