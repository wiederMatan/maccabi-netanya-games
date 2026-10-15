using System.Collections.Generic;
using System.Text;

namespace MaccabiShared
{
    /// <summary>
    /// Unity's legacy Text lays glyphs out left to right and does not reorder
    /// right-to-left scripts, so Hebrew would read backwards. This turns logical
    /// Hebrew text into visual order: the line is reversed, and runs that must
    /// stay left to right (numbers, Latin, sums like "7 + 3") are put back.
    /// Paragraphs must be wrapped here too, since wrapping after the reversal
    /// would put the end of the sentence on the first line.
    /// </summary>
    public static class Rtl
    {
        static bool IsHebrew(char c) => (c >= '֐' && c <= '׿') || (c >= 'יִ' && c <= 'ﭏ');
        static bool IsLtrStrong(char c) => char.IsDigit(c) || (c < 128 && char.IsLetter(c));

        /// <summary>One line in visual order.</summary>
        public static string Fix(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            bool anyHebrew = false;
            foreach (char c in line) if (IsHebrew(c)) { anyHebrew = true; break; }
            if (!anyHebrew) return line;

            var chars = line.ToCharArray();
            System.Array.Reverse(chars);

            // Put left-to-right runs back in reading order. A run starts and ends on
            // a strong LTR character and may contain spaces and number punctuation.
            var result = new StringBuilder(chars.Length);
            int i = 0;
            while (i < chars.Length)
            {
                if (!IsLtrStrong(chars[i])) { result.Append(Mirror(chars[i])); i++; continue; }
                int end = i;
                for (int j = i; j < chars.Length; j++)
                {
                    char c = chars[j];
                    if (IsHebrew(c)) break;
                    if (IsLtrStrong(c)) end = j;
                }
                for (int j = end; j >= i; j--) result.Append(chars[j]);
                i = end + 1;
            }
            return result.ToString();
        }

        /// <summary>
        /// Word-wraps logical text to lines of at most maxChars, then fixes each
        /// line. The first logical line stays on top. Keeps explicit newlines.
        /// </summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = new List<string>();
            foreach (var paragraph in text.Split('\n'))
            {
                var current = new StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }
                lines.Add(current.ToString());
            }
            for (int i = 0; i < lines.Count; i++) lines[i] = Fix(lines[i]);
            return string.Join("\n", lines);
        }

        static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '<': return '>';
                case '>': return '<';
                default: return c;
            }
        }
    }
}
