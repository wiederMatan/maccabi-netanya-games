using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dribble.EditorTools
{
    /// <summary>
    /// Paints the shared "Maccabi Arcade" UI pieces (rounded panels and buttons,
    /// pills, stars and counter icons) as sprite assets in Assets/UI. Panels and
    /// buttons carry 9-slice borders, so one small texture draws a box of any size
    /// with crisp rounded corners. Everything is regenerated on each scene build,
    /// so a tweak here shows up the next time the scene is built.
    /// </summary>
    public static class UiKit
    {
        const string Folder = "Assets/UI";
        const int Supersample = 4;

        // Palette from the shared spec.
        public static readonly Color Navy900 = Hex(0x0A1A36);
        public static readonly Color Navy800 = Hex(0x10264D);
        public static readonly Color Navy700 = Hex(0x1A3A73);
        public static readonly Color Gold400 = Hex(0xFFD23F);
        public static readonly Color Gold600 = Hex(0xF2A900);
        public static readonly Color Gold900 = Hex(0x8A5A00);
        public static readonly Color Red400 = Hex(0xFF5C5C);
        public static readonly Color Cream = Hex(0xFFF6DD);

        public static Sprite Panel, PanelShadow, GoldFace, GoldEdge, NavyFace, NavyEdge, Pill;
        public static Sprite StarFilled, StarSlot, IconBall, IconTrophy;

        /// <summary>
        /// Sizes are in pixels at the HUD's reference resolution, which is two
        /// thirds of the spec's 1080p: radius 28 -> 19, 4 px border -> 3, and so on.
        /// </summary>
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UI");

            Panel = RoundedRect("Panel", 64, 19f, 3f, v => Alpha(Navy800, 0.92f), Alpha(Gold400, 0.7f));
            PanelShadow = SoftShadow("PanelShadow", 96, 24f, 12f, 0.35f);
            GoldFace = RoundedRect("ButtonGold", 64, 17f, 2.5f, v => Color.Lerp(Gold600, Gold400, Mathf.SmoothStep(0.1f, 0.9f, v)), Gold900);
            GoldEdge = RoundedRect("ButtonGoldEdge", 64, 17f, 2.5f, v => Shade(Gold600, 0.92f), Gold900);
            NavyFace = RoundedRect("ButtonNavy", 64, 17f, 2.5f, v => Color.Lerp(Navy700, Shade(Navy700, 1.35f), v), Navy900);
            NavyEdge = RoundedRect("ButtonNavyEdge", 64, 17f, 2.5f, v => Navy900, Navy900);
            Pill = RoundedRect("Pill", 64, 31.5f, 0f, v => Color.white, Color.white);

            var star = StarOutline(0.48f, 0.21f);
            StarFilled = Shape("StarFilled", 128, (x, y) => InPolygon(new Vector2(x, y), star),
                (x, y) => Color.Lerp(Gold600, Shade(Gold400, 1.05f), y), Gold900, 0.045f);
            StarSlot = Shape("StarSlot", 128, (x, y) => InPolygon(new Vector2(x, y), star),
                (x, y) => Alpha(Navy900, 0.9f), Alpha(Gold400, 0.45f), 0.03f);
            IconBall = Shape("IconBall", 64, InBall, BallColour, Navy900, 0.05f);
            IconTrophy = Shape("IconTrophy", 64, InTrophy,
                (x, y) => Color.Lerp(Gold600, Gold400, y), Gold900, 0.05f);

            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- shapes

        static Vector2[] StarOutline(float outer, float inner)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? outer : inner;
                points[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.47f + Mathf.Sin(a) * r);
            }
            return points;
        }

        static bool InBall(float x, float y) => Sq(x - 0.5f) + Sq(y - 0.5f) < Sq(0.44f);

        /// <summary>White ball with a black pentagon in the middle and patches round the edge.</summary>
        static Color BallColour(float x, float y)
        {
            var p = new Vector2(x - 0.5f, y - 0.5f);
            if (Pentagon(p, 0.14f, 90f)) return Navy900;
            for (int i = 0; i < 5; i++)
            {
                float a = (90f + 36f + i * 72f) * Mathf.Deg2Rad;
                var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.42f;
                if (Pentagon(p - c, 0.12f, 90f + 36f + i * 72f + 180f)) return Navy900;
            }
            return new Color(0.97f, 0.97f, 0.95f);
        }

        static bool Pentagon(Vector2 p, float radius, float rotation)
        {
            var corners = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = (rotation + i * 72f) * Mathf.Deg2Rad;
                corners[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return InPolygon(p, corners);
        }

        /// <summary>A cup with two handles on a stem and base - the score.</summary>
        static bool InTrophy(float x, float y)
        {
            float dx = x - 0.5f;
            bool cup = (y >= 0.6f && y <= 0.9f && Mathf.Abs(dx) <= 0.27f) ||
                       (y < 0.6f && Sq(dx / 0.27f) + Sq((y - 0.6f) / 0.24f) <= 1f);
            float hx = Mathf.Abs(dx) - 0.27f;
            float ring = Mathf.Sqrt(Sq(hx) + Sq(y - 0.72f));
            bool handle = hx > 0f && ring > 0.05f && ring < 0.11f;
            bool stem = Mathf.Abs(dx) < 0.055f && y > 0.24f && y < 0.4f;
            bool foot = Mathf.Abs(dx) < 0.21f && y > 0.1f && y <= 0.24f;
            return cup || handle || stem || foot;
        }

        // ---------------------------------------------------------------- painters

        /// <summary>
        /// A rounded rectangle with an outline, as a 9-slice sprite. The fill is a
        /// function of height (0 bottom, 1 top), so buttons can carry a gradient.
        /// </summary>
        static Sprite RoundedRect(string name, int size, float radius, float outline,
            System.Func<float, Color> fill, Color outlineColour)
        {
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f - half, y + 0.5f - half, half, radius);
                    float coverage = Mathf.Clamp01(0.5f - d);
                    var c = fill((y + 0.5f) / size);
                    if (outline > 0f) c = Color.Lerp(c, outlineColour, Mathf.Clamp01(d + outline + 0.5f));
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }
            int border = Mathf.CeilToInt(radius + outline) + 1;
            border = Mathf.Min(border, size / 2 - 1);
            return Save(name, size, size, pixels, new Vector4(border, border, border, border));
        }

        static float RoundedRectDistance(float px, float py, float half, float radius)
        {
            float qx = Mathf.Abs(px) - (half - radius);
            float qy = Mathf.Abs(py) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>A soft black rounded rectangle for the drop shadow under panels.</summary>
        static Sprite SoftShadow(string name, int size, float radius, float feather, float alpha)
        {
            var pixels = new Color[size * size];
            float half = size / 2f;
            float inset = half - feather;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f - half, y + 0.5f - half, inset, Mathf.Min(radius, inset));
                    float a = 1f - Mathf.SmoothStep(-feather, feather, d);
                    pixels[y * size + x] = new Color(0f, 0f, 0f, a * alpha);
                }
            }
            int border = size / 2 - 2;
            return Save(name, size, size, pixels, new Vector4(border, border, border, border));
        }

        /// <summary>
        /// An icon from an inside test, supersampled for smooth edges, with an
        /// outline drawn by growing the shape a little in every direction.
        /// </summary>
        static Sprite Shape(string name, int size, System.Func<float, float, bool> inside,
            System.Func<float, float, Color> fill, Color outlineColour, float outline)
        {
            var pixels = new Color[size * size];
            int samples = Supersample * Supersample;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    var sum = Color.clear;
                    for (int s = 0; s < samples; s++)
                    {
                        float x = (px + (s % Supersample + 0.5f) / Supersample) / size;
                        float y = (py + (s / Supersample + 0.5f) / Supersample) / size;
                        Color c;
                        if (inside(x, y)) c = fill(x, y);
                        else if (Near(inside, x, y, outline)) c = outlineColour;
                        else continue;
                        // Premultiply while averaging so edges do not darken.
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                    sum /= samples;
                    pixels[py * size + px] = sum.a > 0f
                        ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a)
                        : Color.clear;
                }
            }
            return Save(name, size, size, pixels, Vector4.zero);
        }

        static bool Near(System.Func<float, float, bool> inside, float x, float y, float distance)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                if (inside(x + Mathf.Cos(a) * distance, y + Mathf.Sin(a) * distance)) return true;
            }
            return false;
        }

        public static bool InPolygon(Vector2 p, Vector2[] polygon)
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

        static Sprite Save(string name, int width, int height, Color[] pixels, Vector4 border)
        {
            string path = $"{Folder}/{name}.png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static float Sq(float v) => v * v;
        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
