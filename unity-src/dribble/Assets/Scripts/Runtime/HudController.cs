using MaccabiShared;
using UnityEngine;
using UnityEngine.UI;

namespace Dribble
{
    /// <summary>
    /// Owns every piece of on-screen text. The game pushes state in; this class
    /// decides how it reads, animates the overlay and call-outs, and re-lays the
    /// start / end card when the screen turns between portrait and landscape.
    ///
    /// All text is Hebrew. Unity's Text cannot lay out right-to-left, so every
    /// string goes through Rtl.Fix (one line) or Rtl.Wrap (a paragraph, wrapped
    /// here for the current width) and the Text components never wrap themselves.
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

        // Shared palette (scratchpad design SPEC: "Maccabi Arcade").
        public static readonly Color Navy900 = Hex(0x0A1A36);
        public static readonly Color Cream = Hex(0xFFF6DD);
        public static readonly Color Gold400 = Hex(0xFFD23F);

        const float PopSeconds = 0.25f;
        const float DimAlpha = 0.72f;
        const float StarRevealGap = 0.28f;
        const int PortraitBodyChars = 30;
        const int LandscapeBodyChars = 64;

        [SerializeField] CanvasScaler scaler;
        [SerializeField] RectTransform counters;
        [SerializeField] Text scoreText;
        [SerializeField] Text distanceText;
        [SerializeField] Text starsText;
        [SerializeField] Text toastText;
        [SerializeField] Text hintText;

        [SerializeField] GameObject overlay;
        [SerializeField] Image dim;
        [SerializeField] RectTransform card;
        [SerializeField] Text titleText;
        [SerializeField] GameObject badge;
        [SerializeField] RectTransform starRow;
        [SerializeField] Image[] starFills;
        [SerializeField] Text bodyText;
        [SerializeField] Text captionText;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;

        [SerializeField] Button[] difficultyButtons;
        [SerializeField] Image[] difficultyFaces;
        [SerializeField] Image[] difficultyEdges;
        [SerializeField] Text[] difficultyLabels;
        [SerializeField] Text[] difficultyHints;
        [SerializeField] Sprite pickedFace;
        [SerializeField] Sprite pickedEdge;
        [SerializeField] Sprite restingFace;
        [SerializeField] Sprite restingEdge;

        float toastShown;
        float toastUntil;
        float hintUntil;
        float overlayShown = -10f;
        int starsEarned;
        int starsRevealed;
        bool endMode;
        string bodyLogical = "";
        bool? portraitLayout;

        int shownScore = -1;
        int shownDistance = -1;
        int shownStars = -1;

        public Button StartButton => startButton;
        public Button[] DifficultyButtons => difficultyButtons;
        public bool OverlayVisible => overlay != null && overlay.activeSelf;

        /// <summary>Raised as each earned star lands on the end card, for its chime.</summary>
        public event System.Action<int> StarRevealed;

        void Update()
        {
            bool portrait = Screen.height > Screen.width;
            if (portraitLayout != portrait) Layout(portrait);

            float now = Time.unscaledTime;
            AnimateOverlay(now);

            if (toastText != null && toastText.enabled)
            {
                float left = toastUntil - now;
                if (left <= 0f) toastText.enabled = false;
                else
                {
                    SetAlpha(toastText, Mathf.Clamp01(left / 0.3f));
                    float s = Pop((now - toastShown) / PopSeconds, 0.4f);
                    toastText.rectTransform.localScale = new Vector3(s, s, 1f);
                }
            }

            if (hintText != null && hintText.enabled)
            {
                float left = hintUntil - now;
                if (left <= 0f) hintText.enabled = false;
                else SetAlpha(hintText, Mathf.Clamp01(left / 0.6f));
            }
        }

        /// <summary>
        /// The dim fades in and the card pops from 85% with a little overshoot;
        /// then the earned stars drop into their slots one after another.
        /// </summary>
        void AnimateOverlay(float now)
        {
            if (!OverlayVisible) return;
            float t = (now - overlayShown) / PopSeconds;

            if (dim != null)
            {
                var c = dim.color;
                c.a = DimAlpha * Mathf.Clamp01(t);
                dim.color = c;
            }
            if (card != null)
            {
                float s = Pop(t, 0.85f);
                card.localScale = new Vector3(s, s, 1f);
            }

            if (!endMode || starFills == null) return;
            for (int i = 0; i < starFills.Length; i++)
            {
                if (starFills[i] == null) continue;
                bool earned = i < starsEarned;
                starFills[i].enabled = earned;
                if (!earned) continue;

                float start = 1f + i * StarRevealGap / PopSeconds;
                float local = t - start;
                float s = local <= 0f ? 0f : Pop(local, 0f);
                starFills[i].rectTransform.localScale = new Vector3(s, s, 1f);
                if (local > 0f && i >= starsRevealed)
                {
                    starsRevealed = i + 1;
                    StarRevealed?.Invoke(i);
                }
            }
        }

        /// <summary>Ease-out-back from <paramref name="from"/> to 1: overshoots a touch, then settles.</summary>
        static float Pop(float t, float from)
        {
            t = Mathf.Clamp01(t);
            const float back = 1.7f;
            float u = t - 1f;
            float eased = 1f + (back + 1f) * u * u * u + back * u * u;
            return Mathf.LerpUnclamped(from, 1f, eased);
        }

        /// <summary>
        /// Lay the card out for the current shape. Landscape puts the four levels
        /// in a row; portrait stacks them two by two so each stays thumb sized.
        /// Levels run right to left, the way Hebrew reads.
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

            if (counters != null)
            {
                float width = portrait ? 214f : 236f;
                float gap = portrait ? 232f : 262f;
                for (int i = 0; i < counters.childCount; i++)
                {
                    var pill = (RectTransform)counters.GetChild(i);
                    pill.anchoredPosition = new Vector2((1 - i) * gap, 0f);
                    pill.sizeDelta = new Vector2(width, portrait ? 76f : 66f);
                }
                counters.anchoredPosition = new Vector2(0f, portrait ? -22f : -12f);
            }

            if (portrait)
            {
                Place(card, 0f, 40f, 680f, 1010f);
                Place(titleText, 0f, endMode ? 400f : 390f, 660f, 130f);
                Place(starRow, 0f, 282f, 420f, 120f);
                Place(bodyText, 0f, endMode ? 160f : 238f, 640f, endMode ? 110f : 190f);
                Place(captionText, 0f, 62f, 640f, 46f);
                Place(startButton, 0f, -362f, 440f, 116f);
            }
            else
            {
                // Short enough that the new-best badge on its top edge stays on screen.
                Place(card, 0f, -12f, 1220f, 650f);
                Place(titleText, 0f, endMode ? 258f : 245f, 1100f, 110f);
                Place(starRow, 0f, 164f, 360f, 96f);
                Place(bodyText, 0f, endMode ? 86f : 140f, 1140f, endMode ? 50f : 96f);
                Place(captionText, 0f, endMode ? 36f : 52f, 640f, 40f);
                Place(startButton, 0f, -222f, 420f, 108f);
            }

            if (titleText != null) titleText.fontSize = portrait ? (endMode ? 92 : 108) : (endMode ? 84 : 100);
            if (bodyText != null)
            {
                bodyText.fontSize = portrait ? 34 : (endMode ? 32 : 34);
                // Landscape is short on height, so a two-line result runs on as one.
                string logical = portrait ? bodyLogical : bodyLogical.Replace("\n", "   ");
                bodyText.text = Rtl.Wrap(logical, portrait ? PortraitBodyChars : LandscapeBodyChars);
            }
            if (starRow != null)
            {
                float slot = portrait ? 120f : 96f;
                for (int i = 0; i < starRow.childCount; i++)
                {
                    var star = (RectTransform)starRow.GetChild(i);
                    star.sizeDelta = new Vector2(slot, slot);
                    // Fill order right to left, like the text.
                    star.anchoredPosition = new Vector2((1 - i) * slot * 1.18f, i == 1 ? slot * 0.12f : 0f);
                }
            }

            if (difficultyButtons != null)
            {
                for (int i = 0; i < difficultyButtons.Length; i++)
                {
                    if (difficultyButtons[i] == null) continue;
                    if (portrait)
                        Place(difficultyButtons[i], (i % 2 == 0 ? 1f : -1f) * 160f, i < 2 ? -40f : -172f, 300f, 116f);
                    else
                        Place(difficultyButtons[i], (1.5f - i) * 272f, endMode ? -50f : -46f, 252f, 116f);
                }
            }
        }

        static void Place(Component target, float x, float y, float width, float height)
        {
            if (target == null) return;
            var rect = (RectTransform)target.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Light up the chosen level so the current setting is never ambiguous.</summary>
        public void HighlightDifficulty(int index)
        {
            if (difficultyFaces == null) return;

            for (int i = 0; i < difficultyFaces.Length; i++)
            {
                bool picked = i == index;
                if (difficultyFaces[i] != null) difficultyFaces[i].sprite = picked ? pickedFace : restingFace;
                if (difficultyEdges != null && i < difficultyEdges.Length && difficultyEdges[i] != null)
                    difficultyEdges[i].sprite = picked ? pickedEdge : restingEdge;
                Color ink = picked ? Navy900 : Cream;
                if (difficultyLabels != null && i < difficultyLabels.Length && difficultyLabels[i] != null)
                    difficultyLabels[i].color = ink;
                if (difficultyHints != null && i < difficultyHints.Length && difficultyHints[i] != null)
                    difficultyHints[i].color = new Color(ink.r, ink.g, ink.b, 0.8f);
            }
        }

        public void SetScore(int value)
        {
            if (value == shownScore) return;
            shownScore = value;
            Set(scoreText, value.ToString());
        }

        public void SetDistance(int metres)
        {
            if (metres == shownDistance) return;
            shownDistance = metres;
            Set(distanceText, Rtl.Fix($"{metres} מ'"));
        }

        public void SetStars(int value)
        {
            if (value == shownStars) return;
            shownStars = value;
            Set(starsText, value.ToString());
        }

        /// <summary>A big centre-screen call-out that pops in and fades after a moment.</summary>
        public void Toast(string logical, float seconds = 1.2f)
        {
            if (toastText == null) return;
            toastText.text = Rtl.Fix(logical);
            toastText.enabled = true;
            SetAlpha(toastText, 1f);
            toastShown = Time.unscaledTime;
            toastUntil = toastShown + seconds;
        }

        public void ShowHint(string logical, float seconds)
        {
            if (hintText == null) return;
            hintText.text = Rtl.Fix(logical);
            hintText.enabled = true;
            SetAlpha(hintText, 1f);
            hintUntil = Time.unscaledTime + seconds;
        }

        public void HideHint()
        {
            if (hintText != null && hintText.enabled)
                hintUntil = Mathf.Min(hintUntil, Time.unscaledTime + 0.6f);
        }

        /// <summary>The start card: title, blurb, levels and the kick-off button.</summary>
        public void ShowMenu(string title, string body, string buttonLabel)
        {
            Show(false, title, body, buttonLabel, 0, false);
        }

        /// <summary>The end card, with the stars the run earned and a badge for a new best.</summary>
        public void ShowEnd(string title, string body, string buttonLabel, int stars, bool newBest)
        {
            Show(true, title, body, buttonLabel, stars, newBest);
        }

        void Show(bool end, string title, string body, string buttonLabel, int stars, bool newBest)
        {
            endMode = end;
            starsEarned = Mathf.Clamp(stars, 0, 3);
            starsRevealed = 0;
            bodyLogical = body ?? "";

            if (overlay != null) overlay.SetActive(true);
            // The card says everything the counters do, and the title needs the room.
            if (counters != null) counters.gameObject.SetActive(false);
            if (starRow != null) starRow.gameObject.SetActive(end);
            if (badge != null) badge.SetActive(end && newBest);
            if (starFills != null)
                foreach (var fill in starFills) if (fill != null) fill.enabled = false;

            Set(titleText, Rtl.Fix(title));
            Set(startButtonLabel, Rtl.Fix(buttonLabel));
            if (toastText != null) toastText.enabled = false;
            if (hintText != null) hintText.enabled = false;

            overlayShown = Time.unscaledTime;
            Layout(Screen.height > Screen.width);
            AnimateOverlay(overlayShown);
        }

        public void HideOverlay()
        {
            if (overlay != null) overlay.SetActive(false);
            if (counters != null) counters.gameObject.SetActive(true);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            var c = graphic.color;
            c.a = alpha;
            graphic.color = c;
            foreach (var effect in graphic.GetComponents<Shadow>())
            {
                var e = effect.effectColor;
                e.a = alpha * 0.85f;
                effect.effectColor = e;
            }
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }

        public static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
    }
}
