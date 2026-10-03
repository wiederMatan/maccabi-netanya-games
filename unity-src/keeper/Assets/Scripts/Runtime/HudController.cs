using UnityEngine;
using UnityEngine.UI;

namespace Keeper
{
    /// <summary>
    /// Owns every piece of on-screen text. The game pushes state in; this class
    /// decides how it reads, and re-flows the layout when the screen turns between
    /// portrait and landscape.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] RectTransform canvasRect;
        [SerializeField] RectTransform messageCard;
        [SerializeField] Text messageText;
        [SerializeField] Image[] pips;
        [SerializeField] Text savesText;
        [SerializeField] Text streakText;
        [SerializeField] Text bestText;
        [SerializeField] Text levelText;
        [SerializeField] Text callout;
        [SerializeField] GameObject overlay;
        [SerializeField] RectTransform overlayContent;
        [SerializeField] Text overlayTitle;
        [SerializeField] Text overlayBody;
        [SerializeField] RectTransform levelCaption;
        [SerializeField] Button startButton;
        [SerializeField] Text startLabel;
        [SerializeField] Button[] levelButtons;
        [SerializeField] Image[] levelBackgrounds;
        [SerializeField] Text[] levelLabels;
        [SerializeField] Text[] levelHints;

        public Button StartButton => startButton;
        public Button[] LevelButtons => levelButtons;
        public bool OverlayVisible => overlay != null && overlay.activeSelf;
        public int PipCount => pips?.Length ?? 0;

        static readonly Color PipPending = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color PipSaved = new Color(0.25f, 0.85f, 0.4f);
        static readonly Color PipConceded = new Color(0.9f, 0.27f, 0.22f);
        static readonly Color PipCurrent = new Color(0.98f, 0.82f, 0.09f);

        static readonly Color PickedFill = new Color(0.98f, 0.82f, 0.09f);
        static readonly Color RestingFill = new Color(0.10f, 0.20f, 0.30f);
        static readonly Color PickedInk = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color RestingInk = new Color(0.90f, 0.92f, 0.90f);

        Vector2 lastSize;
        float calloutUntil;
        float calloutStart;

        public void Bind(RectTransform canvas, RectTransform card, Text message, Image[] shotPips,
            Text saves, Text streak, Text best, Text level, Text bigCallout)
        {
            canvasRect = canvas;
            messageCard = card;
            messageText = message;
            pips = shotPips;
            savesText = saves;
            streakText = streak;
            bestText = best;
            levelText = level;
            callout = bigCallout;
        }

        public void BindOverlay(GameObject panel, RectTransform content, Text title, Text body,
            RectTransform caption, Button button, Text buttonLabel,
            Button[] tiers, Image[] tierBackgrounds, Text[] tierLabels, Text[] tierHints)
        {
            overlay = panel;
            overlayContent = content;
            overlayTitle = title;
            overlayBody = body;
            levelCaption = caption;
            startButton = button;
            startLabel = buttonLabel;
            levelButtons = tiers;
            levelBackgrounds = tierBackgrounds;
            levelLabels = tierLabels;
            levelHints = tierHints;
        }

        void Update()
        {
            if (canvasRect != null && canvasRect.rect.size != lastSize)
            {
                lastSize = canvasRect.rect.size;
                Layout(lastSize);
            }

            if (callout != null && callout.enabled)
            {
                float age = Time.time - calloutStart;
                // Pop in big, settle, then fade out.
                float pop = age < 0.18f ? Mathf.Lerp(0.4f, 1.12f, age / 0.18f) : Mathf.Lerp(1.12f, 1f, (age - 0.18f) / 0.2f);
                callout.rectTransform.localScale = Vector3.one * pop;
                float fade = Mathf.Clamp01((calloutUntil - Time.time) / 0.3f);
                var c = callout.color;
                c.a = fade;
                callout.color = c;
                if (Time.time >= calloutUntil) callout.enabled = false;
            }
        }

        /// <summary>
        /// Re-flow for the current canvas size (in reference units). The message card
        /// and the overlay's level buttons are the parts that change: a portrait phone
        /// is barely 1080 units wide, so the four levels go two by two.
        /// </summary>
        void Layout(Vector2 size)
        {
            bool tall = size.y > size.x;

            if (messageCard != null)
                messageCard.sizeDelta = new Vector2(Mathf.Min(1100f, size.x - 48f), messageCard.sizeDelta.y);

            if (overlayContent == null) return;

            float width = Mathf.Min(1150f, size.x - 60f);
            if (overlayBody != null) overlayBody.rectTransform.sizeDelta = new Vector2(width, 170f);

            // Stack, top to bottom: title, body, caption, level buttons, start button.
            int rows = tall ? 2 : 1;
            float buttonW = tall ? Mathf.Min(440f, (width - 30f) / 2f) : Mathf.Min(250f, (width - 60f) / 4f);
            const float buttonH = 120f;
            float tiersHeight = rows * buttonH + (rows - 1) * 24f;

            float y = 0f;
            Place(overlayTitle?.rectTransform, ref y, 130f, 10f);
            Place(overlayBody?.rectTransform, ref y, 170f, 18f);
            Place(levelCaption, ref y, 50f, 12f);

            for (int i = 0; levelButtons != null && i < levelButtons.Length; i++)
            {
                if (levelButtons[i] == null) continue;
                int row = tall ? i / 2 : 0;
                int col = tall ? i % 2 : i;
                int cols = tall ? 2 : levelButtons.Length;
                float x = (col - (cols - 1) * 0.5f) * (buttonW + (tall ? 30f : 20f));
                var rect = (RectTransform)levelButtons[i].transform;
                rect.sizeDelta = new Vector2(buttonW, buttonH);
                rect.anchoredPosition = new Vector2(x, y - row * (buttonH + 24f) - buttonH / 2f);
                foreach (var t in levelButtons[i].GetComponentsInChildren<Text>())
                    t.rectTransform.sizeDelta = new Vector2(buttonW, t.rectTransform.sizeDelta.y);
            }
            y -= tiersHeight + 40f;

            if (startButton != null)
            {
                var rect = (RectTransform)startButton.transform;
                rect.anchoredPosition = new Vector2(0f, y - rect.sizeDelta.y / 2f);
                y -= rect.sizeDelta.y;
            }

            // Centre the whole stack vertically, nudged up off the corner buttons.
            float total = -y;
            overlayContent.anchoredPosition = new Vector2(0f, total / 2f + 40f);
            float fit = Mathf.Min(1f, (size.y - 330f) / total);
            overlayContent.localScale = Vector3.one * Mathf.Max(0.6f, fit);
        }

        static void Place(RectTransform rect, ref float y, float height, float gap)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(0f, y - height / 2f);
            y -= height + gap;
        }

        public void SetMessage(string text) => Set(messageText, text);
        public void SetSaves(int saves, int shots) => Set(savesText, $"{saves}/{shots}");
        public void SetStreak(int value) => Set(streakText, value.ToString());
        public void SetBest(int value) => Set(bestText, value.ToString());
        public void SetLevel(string name) => Set(levelText, name);

        /// <summary>Colour the shot pips: saved, conceded, the one being taken, and the rest.</summary>
        public void SetPips(bool?[] results, int current)
        {
            if (pips == null) return;
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null) continue;
                bool? r = i < results.Length ? results[i] : null;
                pips[i].color = r == true ? PipSaved : r == false ? PipConceded : i == current ? PipCurrent : PipPending;
            }
        }

        /// <summary>A big word across the middle of the pitch - SAVE! or GOAL! - that pops and fades.</summary>
        public void ShowCallout(string text, Color color, float seconds = 1.3f)
        {
            if (callout == null) return;
            callout.text = text;
            callout.color = color;
            callout.enabled = true;
            calloutStart = Time.time;
            calloutUntil = Time.time + seconds;
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
                    levelHints[i].color = (picked ? PickedInk : RestingInk) * new Color(1f, 1f, 1f, 0.75f);
            }
        }

        public void ShowOverlay(string title, string body, string buttonLabel)
        {
            if (overlay != null) overlay.SetActive(true);
            Set(overlayTitle, title);
            Set(overlayBody, body);
            Set(startLabel, buttonLabel);
            lastSize = Vector2.zero;
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
