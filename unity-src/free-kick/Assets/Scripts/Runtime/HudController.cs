using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FreeKick
{
    /// <summary>
    /// Owns every piece of on-screen text. The free kick manager pushes state in;
    /// this class decides how it reads.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        // Layout on the 1920x1080 reference canvas (CanvasScaler, match 0.5). The web
        // page's placeCorner() and the camera framing both rely on these numbers.
        public const float BarBottom = 30f;
        public const float BarHeight = 130f;
        public const float BarSide = 40f;
        public const float CardTop = 24f;
        public const float CardHeight = 150f;

        [SerializeField] Text messageText;
        [SerializeField] Text hintText;
        [SerializeField] Text goalsText;
        [SerializeField] Text scoreText;
        [SerializeField] Text bestText;
        [SerializeField] Text levelText;
        [SerializeField] Image[] kickMarks;
        [SerializeField] Text flashText;
        [SerializeField] GameObject overlay;
        [SerializeField] Text overlayTitle;
        [SerializeField] Text overlayBody;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;
        [SerializeField] Button[] levelButtons;
        [SerializeField] Image[] levelBackgrounds;
        [SerializeField] Text[] levelLabels;
        [SerializeField] Text[] levelHints;

        static readonly Color PickedFill = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color RestingFill = new Color(0.10f, 0.20f, 0.28f);
        static readonly Color PickedInk = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color RestingInk = new Color(0.86f, 0.88f, 0.86f);
        static readonly Color PickedHint = new Color(0.05f, 0.11f, 0.17f, 0.75f);
        static readonly Color RestingHint = new Color(0.86f, 0.88f, 0.86f, 0.6f);

        public static readonly Color MarkPending = new Color(0.20f, 0.27f, 0.33f);
        public static readonly Color MarkGoal = new Color(0.98f, 0.82f, 0.09f);
        public static readonly Color MarkMiss = new Color(0.78f, 0.25f, 0.21f);

        Coroutine flash;

        public Button StartButton => startButton;
        public Button[] LevelButtons => levelButtons;
        public Image[] KickMarks => kickMarks;
        public bool OverlayVisible => overlay != null && overlay.activeSelf;

        /// <summary>
        /// The slice of the screen, as fractions of its height from the bottom, that
        /// the HUD leaves clear for the pitch.
        /// </summary>
        public static (float bottom, float top) SceneBand(float width, float height)
        {
            float scale = Mathf.Sqrt(width / 1920f * (height / 1080f));
            float bottom = (BarBottom + BarHeight + 24f) * scale / height;
            float top = 1f - (CardTop + CardHeight + 18f) * scale / height;
            return (bottom, top);
        }

        public void Bind(Text message, Text hint, Text goals, Text score, Text best, Text level,
            Image[] marks, Text flashLabel)
        {
            messageText = message;
            hintText = hint;
            goalsText = goals;
            scoreText = score;
            bestText = best;
            levelText = level;
            kickMarks = marks;
            flashText = flashLabel;
        }

        public void BindOverlay(GameObject panel, Text title, Text body, Button button, Text buttonLabel,
            Button[] buttons, Image[] backgrounds, Text[] labels, Text[] hints)
        {
            overlay = panel;
            overlayTitle = title;
            overlayBody = body;
            startButton = button;
            startButtonLabel = buttonLabel;
            levelButtons = buttons;
            levelBackgrounds = backgrounds;
            levelLabels = labels;
            levelHints = hints;
        }

        /// <summary>Light up the chosen level so the current setting is never ambiguous.</summary>
        public void HighlightLevel(int index)
        {
            if (levelBackgrounds == null) return;

            for (int i = 0; i < levelBackgrounds.Length; i++)
            {
                bool picked = i == index;
                if (levelBackgrounds[i] != null) levelBackgrounds[i].color = picked ? PickedFill : RestingFill;
                if (levelLabels != null && i < levelLabels.Length && levelLabels[i] != null)
                    levelLabels[i].color = picked ? PickedInk : RestingInk;
                if (levelHints != null && i < levelHints.Length && levelHints[i] != null)
                    levelHints[i].color = picked ? PickedHint : RestingHint;
            }
        }

        public void SetMessage(string text) => Set(messageText, text);
        public void SetHint(string text) => Set(hintText, text);
        public void SetGoals(int goals, int kicks) => Set(goalsText, $"{goals}/{kicks}");
        public void SetScore(int value) => Set(scoreText, value.ToString());
        public void SetBest(int value) => Set(bestText, value.ToString());
        public void SetLevel(string name) => Set(levelText, name.ToUpperInvariant());

        /// <summary>Colour kick <paramref name="index"/>'s square: pending, scored or missed.</summary>
        public void MarkKick(int index, Color colour)
        {
            if (kickMarks == null || index < 0 || index >= kickMarks.Length || kickMarks[index] == null) return;
            kickMarks[index].color = colour;
        }

        public void ResetKicks()
        {
            if (kickMarks == null) return;
            for (int i = 0; i < kickMarks.Length; i++) MarkKick(i, MarkPending);
        }

        /// <summary>A big word across the middle of the pitch - GOAL!, SAVED! - that pops and fades.</summary>
        public void Flash(string word, Color colour)
        {
            if (flashText == null) return;
            if (flash != null) StopCoroutine(flash);
            flash = StartCoroutine(FlashRoutine(word, colour));
        }

        IEnumerator FlashRoutine(string word, Color colour)
        {
            flashText.text = word;
            flashText.gameObject.SetActive(true);
            var rect = flashText.rectTransform;
            const float duration = 1.5f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float pop = t < 0.18f ? Mathf.Lerp(0.4f, 1.12f, t / 0.18f) : Mathf.Lerp(1.12f, 1f, (t - 0.18f) / 0.3f);
                rect.localScale = Vector3.one * pop;
                float fade = t > duration - 0.35f ? (duration - t) / 0.35f : 1f;
                flashText.color = new Color(colour.r, colour.g, colour.b, fade);
                yield return null;
            }
            flashText.gameObject.SetActive(false);
            flash = null;
        }

        public void ShowOverlay(string title, string body, string buttonLabel)
        {
            if (overlay != null) overlay.SetActive(true);
            Set(overlayTitle, title);
            Set(overlayBody, body);
            Set(startButtonLabel, buttonLabel);
        }

        public void HideOverlay()
        {
            if (overlay != null) overlay.SetActive(false);
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
