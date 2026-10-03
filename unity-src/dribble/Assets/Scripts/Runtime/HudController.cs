using UnityEngine;
using UnityEngine.UI;

namespace Dribble
{
    /// <summary>
    /// Owns every piece of on-screen text. The game pushes state in; this class
    /// decides how it reads, and re-lays the start / end overlay when the screen
    /// turns between portrait and landscape.
    ///
    /// The canvas scales in Expand mode against a 1280x720 box in landscape and a
    /// 720x1280 one in portrait, so everything laid out inside that box is on
    /// screen at any window shape, and the text stays big enough to read on a
    /// small phone.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public static readonly Vector2 LandscapeReference = new Vector2(1280f, 720f);
        public static readonly Vector2 PortraitReference = new Vector2(720f, 1280f);

        [SerializeField] CanvasScaler scaler;
        [SerializeField] RectTransform statsBar;
        [SerializeField] Text scoreText;
        [SerializeField] Text distanceText;
        [SerializeField] Text starsText;
        [SerializeField] Text bestText;
        [SerializeField] Text toastText;
        [SerializeField] Text hintText;
        [SerializeField] GameObject overlay;
        [SerializeField] RectTransform titleRect;
        [SerializeField] Text titleText;
        [SerializeField] RectTransform bodyRect;
        [SerializeField] Text bodyText;
        [SerializeField] RectTransform captionRect;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;
        [SerializeField] Button[] difficultyButtons;
        [SerializeField] Image[] difficultyBackgrounds;
        [SerializeField] Text[] difficultyLabels;
        [SerializeField] Text[] difficultyHints;

        static readonly Color PickedFill = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color RestingFill = new Color(0.10f, 0.20f, 0.28f);
        static readonly Color PickedInk = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color RestingInk = new Color(0.86f, 0.88f, 0.86f);

        float toastUntil;
        float hintUntil;
        bool? portraitLayout;

        public Button StartButton => startButton;
        public Button[] DifficultyButtons => difficultyButtons;
        public bool OverlayVisible => overlay != null && overlay.activeSelf;

        public void Bind(CanvasScaler canvasScaler, Text score, Text distance, Text stars, Text best,
            Text toast, Text hint)
        {
            scaler = canvasScaler;
            scoreText = score;
            distanceText = distance;
            starsText = stars;
            bestText = best;
            toastText = toast;
            hintText = hint;
        }

        public void BindStatsBar(RectTransform bar)
        {
            statsBar = bar;
        }

        public void BindOverlay(GameObject panel, Text title, Text body, RectTransform caption,
            Button button, Text buttonLabel)
        {
            overlay = panel;
            titleText = title;
            titleRect = title != null ? title.rectTransform : null;
            bodyText = body;
            bodyRect = body != null ? body.rectTransform : null;
            captionRect = caption;
            startButton = button;
            startButtonLabel = buttonLabel;
        }

        public void BindDifficulty(Button[] buttons, Image[] backgrounds, Text[] labels, Text[] hints)
        {
            difficultyButtons = buttons;
            difficultyBackgrounds = backgrounds;
            difficultyLabels = labels;
            difficultyHints = hints;
        }

        void Update()
        {
            bool portrait = Screen.height > Screen.width;
            if (portraitLayout != portrait) Layout(portrait);

            if (toastText != null && toastText.enabled)
            {
                float left = toastUntil - Time.unscaledTime;
                if (left <= 0f) toastText.enabled = false;
                else SetAlpha(toastText, Mathf.Clamp01(left / 0.35f));
            }

            if (hintText != null && hintText.enabled)
            {
                float left = hintUntil - Time.unscaledTime;
                if (left <= 0f) hintText.enabled = false;
                else SetAlpha(hintText, Mathf.Clamp01(left / 0.6f));
            }
        }

        /// <summary>
        /// Landscape puts the four levels in a row; portrait stacks them two by two
        /// so each button stays a good size for a small thumb.
        /// </summary>
        public void Layout(bool portrait)
        {
            portraitLayout = portrait;
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.referenceResolution = portrait ? PortraitReference : LandscapeReference;
            }

            LayoutStatsBar(portrait);

            if (portrait)
            {
                Place(titleRect, 0f, 430f, 680f, 110f);
                Place(bodyRect, 0f, 300f, 640f, 150f);
                Place(captionRect, 0f, 150f, 640f, 40f);
            }
            else
            {
                Place(titleRect, 0f, 262f, 1100f, 100f);
                Place(bodyRect, 0f, 158f, 1100f, 96f);
                Place(captionRect, 0f, 72f, 640f, 40f);
            }
            if (bodyText != null) bodyText.fontSize = portrait ? 32 : 34;

            if (difficultyButtons != null)
            {
                for (int i = 0; i < difficultyButtons.Length; i++)
                {
                    if (difficultyButtons[i] == null) continue;
                    var rect = (RectTransform)difficultyButtons[i].transform;
                    if (portrait)
                        Place(rect, (i % 2 == 0 ? -1f : 1f) * 160f, i < 2 ? 55f : -75f, 300f, 112f);
                    else
                        Place(rect, (i - 1.5f) * 270f, -35f, 250f, 116f);
                }
            }

            if (startButton != null)
            {
                var rect = (RectTransform)startButton.transform;
                if (portrait) Place(rect, 0f, -260f, 420f, 112f);
                else Place(rect, 0f, -200f, 400f, 100f);
            }
        }

        /// <summary>
        /// Landscape is short, so the stats bar slims down there to leave the far
        /// end of the pitch - where the defenders come from - in view.
        /// </summary>
        void LayoutStatsBar(bool portrait)
        {
            if (statsBar == null) return;
            float height = portrait ? 104f : 84f;
            statsBar.offsetMin = new Vector2(16f, -16f - height);
            statsBar.offsetMax = new Vector2(-16f, -16f);

            foreach (var text in statsBar.GetComponentsInChildren<Text>(true))
                text.fontSize = text.name == "Value" ? (portrait ? 46 : 40) : (portrait ? 22 : 19);
            foreach (var image in statsBar.GetComponentsInChildren<Image>(true))
                if (image.transform != statsBar)
                    image.rectTransform.sizeDelta = Vector2.one * (portrait ? 44f : 36f);
        }

        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Light up the chosen level so the current setting is never ambiguous.</summary>
        public void HighlightDifficulty(int index)
        {
            if (difficultyBackgrounds == null) return;

            for (int i = 0; i < difficultyBackgrounds.Length; i++)
            {
                bool picked = i == index;
                if (difficultyBackgrounds[i] != null)
                    difficultyBackgrounds[i].color = picked ? PickedFill : RestingFill;
                Color ink = picked ? PickedInk : RestingInk;
                if (difficultyLabels != null && i < difficultyLabels.Length && difficultyLabels[i] != null)
                    difficultyLabels[i].color = ink;
                if (difficultyHints != null && i < difficultyHints.Length && difficultyHints[i] != null)
                    difficultyHints[i].color = new Color(ink.r, ink.g, ink.b, 0.75f);
            }
        }

        public void SetScore(int value) => Set(scoreText, value.ToString());
        public void SetDistance(int metres) => Set(distanceText, $"{metres} m");
        public void SetStars(int value) => Set(starsText, value.ToString());
        public void SetBest(int value) => Set(bestText, value.ToString());

        /// <summary>A big centre-screen call-out that fades after a moment.</summary>
        public void Toast(string text, float seconds = 1.3f)
        {
            if (toastText == null) return;
            toastText.text = text;
            toastText.enabled = true;
            SetAlpha(toastText, 1f);
            toastUntil = Time.unscaledTime + seconds;
        }

        public void ShowHint(string text, float seconds)
        {
            if (hintText == null) return;
            hintText.text = text;
            hintText.enabled = true;
            SetAlpha(hintText, 1f);
            hintUntil = Time.unscaledTime + seconds;
        }

        public void HideHint()
        {
            if (hintText != null && hintText.enabled)
                hintUntil = Mathf.Min(hintUntil, Time.unscaledTime + 0.6f);
        }

        public void ShowOverlay(string title, string body, string buttonLabel)
        {
            if (overlay != null) overlay.SetActive(true);
            // The overlay says everything the bar does, and the title needs the room.
            if (statsBar != null) statsBar.gameObject.SetActive(false);
            Set(titleText, title);
            Set(bodyText, body);
            Set(startButtonLabel, buttonLabel);
            if (toastText != null) toastText.enabled = false;
            if (hintText != null) hintText.enabled = false;
        }

        public void HideOverlay()
        {
            if (overlay != null) overlay.SetActive(false);
            if (statsBar != null) statsBar.gameObject.SetActive(true);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            var c = graphic.color;
            c.a = alpha;
            graphic.color = c;
            var outline = graphic.GetComponent<Shadow>();
            if (outline != null)
            {
                var e = outline.effectColor;
                e.a = alpha * 0.85f;
                outline.effectColor = e;
            }
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
