using MaccabiShared;
using UnityEngine;
using UnityEngine.UI;

namespace Dribble
{
    /// <summary>
    /// Owns every piece of on-screen text. The game pushes state in; this class
    /// decides how it reads, animates the countdown, call-outs and end card, and
    /// re-lays the card when the screen turns between portrait and landscape.
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
        const int ToastSize = 84;
        const float DimAlpha = 0.72f;
        const float StarRevealGap = 0.28f;
        const int PortraitBodyChars = 30;
        const int LandscapeBodyChars = 60;

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
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;


        float toastShown;
        float toastUntil;
        float hintUntil;
        float overlayShown = -10f;
        int starsEarned;
        int starsRevealed;
        string bodyLogical = "";
        bool? portraitLayout;

        int shownScore = -1;
        int shownDistance = -1;
        int shownStars = -1;

        public Button StartButton => startButton;
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

            if (starFills == null) return;
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

        /// <summary>Lay the end card out for the current shape.</summary>
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
                Place(card, 0f, 20f, 660f, 780f);
                Place(titleText, 0f, 280f, 640f, 130f);
                Place(starRow, 0f, 145f, 420f, 120f);
                Place(bodyText, 0f, 5f, 620f, 110f);
                Place(startButton, 0f, -235f, 480f, 136f);
            }
            else
            {
                Place(card, 0f, -12f, 1020f, 600f);
                Place(titleText, 0f, 205f, 900f, 110f);
                Place(starRow, 0f, 105f, 360f, 96f);
                Place(bodyText, 0f, 10f, 980f, 50f);
                Place(startButton, 0f, -170f, 460f, 124f);
            }

            if (titleText != null) titleText.fontSize = portrait ? 96 : 88;
            if (startButtonLabel != null) startButtonLabel.fontSize = portrait ? 56 : 52;
            if (bodyText != null)
            {
                bodyText.fontSize = portrait ? 34 : 32;
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
        }

        static void Place(Component target, float x, float y, float width, float height)
        {
            if (target == null) return;
            var rect = (RectTransform)target.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
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
            toastText.fontSize = ToastSize;
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

        /// <summary>The countdown before a run: one big number at a time, popping in.</summary>
        public void Countdown(string logical)
        {
            Toast(logical, 0.72f);
            if (toastText != null) toastText.fontSize = 180;
        }

        /// <summary>The end card, with the stars the run earned and a badge for a new best.</summary>
        public void ShowEnd(string title, string body, string buttonLabel, int stars, bool newBest)
        {
            starsEarned = Mathf.Clamp(stars, 0, 3);
            starsRevealed = 0;
            bodyLogical = body ?? "";

            if (overlay != null) overlay.SetActive(true);
            // The card says everything the counters do, and the title needs the room.
            if (counters != null) counters.gameObject.SetActive(false);
            if (badge != null) badge.SetActive(newBest);
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
