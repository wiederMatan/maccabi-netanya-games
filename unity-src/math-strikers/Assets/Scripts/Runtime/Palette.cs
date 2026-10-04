using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// The shared "Maccabi Arcade" colours, so the HUD, the answer boards and the
    /// website all read as one design.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Navy900 = Hex(0x0A1A36);
        public static readonly Color Navy800 = Hex(0x10264D);
        public static readonly Color Navy700 = Hex(0x1A3A73);
        public static readonly Color Gold400 = Hex(0xFFD23F);
        public static readonly Color Gold600 = Hex(0xF2A900);
        public static readonly Color Gold900 = Hex(0x8A5A00);
        public static readonly Color Blue400 = Hex(0x4D8DFF);
        public static readonly Color Blue700 = Hex(0x2556C8);
        public static readonly Color Green400 = Hex(0x3DDC84);
        public static readonly Color Red400 = Hex(0xFF5C5C);
        public static readonly Color Cream = Hex(0xFFF6DD);

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
