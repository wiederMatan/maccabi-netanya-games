using UnityEngine;
using UnityEngine.UI;

namespace Juggling
{
    /// <summary>
    /// Owns every piece of on-screen text. The juggle manager pushes state in; this
    /// class decides how it reads.
    ///
    /// The canvas is laid out against a 720 x 720 square and scaled by the screen's
    /// short side, so a phone held either way up gets the same, readable HUD -
    /// rather than the tiny one a 1920 x 1080 reference gives a portrait phone.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public const float ReferenceSize = 720f;
        // The top bar plus its margin and a little air below it, in canvas units.
        // CameraFramer keeps the ball's flight below this.
        public const float TopReserve = 138f;

        [SerializeField] CanvasScaler scaler;
        [SerializeField] GameObject scoreBar;
        [SerializeField] Text scoreText;
        [SerializeField] Text comboText;
        [SerializeField] Text bestText;
        [SerializeField] Text toastText;
        [SerializeField] Text bannerText;
        [SerializeField] Text hintText;
        [SerializeField] GameObject overlay;
        [SerializeField] Text overlayTitle;
        [SerializeField] Text overlayBody;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;
        [SerializeField] Button[] difficultyButtons;
        [SerializeField] Image[] difficultyBackgrounds;
        [SerializeField] Text[] difficultyLabels;

        static readonly Color Gold = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color PickedFill = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color RestingFill = new Color(0.10f, 0.20f, 0.28f);
        static readonly Color PickedInk = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color RestingInk = new Color(0.86f, 0.88f, 0.86f);

        public Button StartButton => startButton;
        public Button[] DifficultyButtons => difficultyButtons;
        public bool OverlayVisible => overlay != null && overlay.activeSelf;

        float toastTimer;
        float bannerTimer;
        const float ToastSeconds = 0.8f;
        const float BannerSeconds = 1.8f;
        bool lastPortrait;
        bool scaled;

        public void Bind(CanvasScaler canvasScaler, Text score, Text combo, Text best, Text toast,
            Text banner, Text hint, GameObject panel, Text title, Text body, Button button, Text buttonLabel)
        {
            scaler = canvasScaler;
            scoreText = score;
            comboText = combo;
            bestText = best;
            toastText = toast;
            bannerText = banner;
            hintText = hint;
            overlay = panel;
            overlayTitle = title;
            overlayBody = body;
            startButton = button;
            startButtonLabel = buttonLabel;
        }

        /// <summary>
        /// The score bar hides while the overlay is up: in a landscape window the
        /// overlay's title sits where the bar is, and the overlay states the score itself.
        /// </summary>
        public void BindScoreBar(GameObject bar) => scoreBar = bar;

        public void BindDifficulty(Button[] buttons, Image[] backgrounds, Text[] labels)
        {
            difficultyButtons = buttons;
            difficultyBackgrounds = backgrounds;
            difficultyLabels = labels;
        }

        void Update()
        {
            bool portrait = Screen.height > Screen.width;
            if (scaler != null && (!scaled || portrait != lastPortrait))
            {
                // Match the short side: width when upright, height when sideways.
                scaler.matchWidthOrHeight = portrait ? 0f : 1f;
                lastPortrait = portrait;
                scaled = true;
            }

            Fade(toastText, ref toastTimer, ToastSeconds, 1.25f);
            Fade(bannerText, ref bannerTimer, BannerSeconds, 1.15f);
        }

        /// <summary>Pop in, hold, fade - shared by the small toast and the big banner.</summary>
        static void Fade(Text text, ref float timer, float seconds, float popScale)
        {
            if (text == null || timer <= 0f) return;

            timer -= Time.unscaledDeltaTime;
            float age = seconds - timer;
            float alpha = Mathf.Clamp01(timer / (seconds * 0.4f));
            var colour = text.color;
            colour.a = alpha;
            text.color = colour;

            float pop = age < 0.12f ? Mathf.Lerp(popScale, 1f, age / 0.12f) : 1f;
            text.rectTransform.localScale = Vector3.one * pop;
            if (timer <= 0f) text.text = "";
        }

        /// <summary>Light up the chosen tier so the current setting is never ambiguous.</summary>
        public void HighlightDifficulty(int index)
        {
            if (difficultyBackgrounds == null) return;

            for (int i = 0; i < difficultyBackgrounds.Length; i++)
            {
                bool picked = i == index;
                if (difficultyBackgrounds[i] != null)
                    difficultyBackgrounds[i].color = picked ? PickedFill : RestingFill;
                if (difficultyLabels != null && i < difficultyLabels.Length && difficultyLabels[i] != null)
                    difficultyLabels[i].color = picked ? PickedInk : RestingInk;
            }
        }

        public void SetScore(int value) => Set(scoreText, value.ToString());
        public void SetBest(int value) => Set(bestText, value.ToString());
        public void SetHint(string text) => Set(hintText, text);

        public void SetCombo(int combo)
        {
            if (comboText == null) return;
            comboText.text = combo >= 2 ? $"x{combo}" : "-";
            comboText.color = combo >= ScoreRules.DoublePointsCombo ? Gold : RestingInk;
        }

        /// <summary>A quick word of praise that pops up and fades away.</summary>
        public void Toast(string text)
        {
            if (toastText == null) return;
            toastText.text = text;
            toastTimer = ToastSeconds;
        }

        /// <summary>The big milestone shout across the middle of the screen.</summary>
        public void Banner(string text)
        {
            if (bannerText == null) return;
            bannerText.text = text;
            bannerTimer = BannerSeconds;
        }

        public void ShowOverlay(string title, string body, string buttonLabel)
        {
            if (overlay != null) overlay.SetActive(true);
            if (scoreBar != null) scoreBar.SetActive(false);
            Set(overlayTitle, title);
            Set(overlayBody, body);
            Set(startButtonLabel, buttonLabel);
        }

        public void SetOverlayBody(string body) => Set(overlayBody, body);

        public void HideOverlay()
        {
            if (overlay != null) overlay.SetActive(false);
            if (scoreBar != null) scoreBar.SetActive(true);
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
