using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PenaltyDuel
{
    /// <summary>One row of the shootout scoreboard: who, how they have kicked, how many in.</summary>
    [System.Serializable]
    public class ScoreRow
    {
        public Image background;
        public Image stripe;
        public RawImage portrait;
        public Text name;
        public Text squadName;
        public Text goals;
        public Image[] marks;
        public Image[] glyphs;
    }

    /// <summary>
    /// Owns every piece of on-screen text. The match manager pushes state in; this
    /// class decides how it reads, and lays the HUD out for the shape of the screen:
    /// side by side along the top of a wide screen, stacked on a tall or narrow one.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        // The widest canvas (in reference units) that still fits the scoreboard and
        // the prompt side by side.
        const float SideBySideWidth = 1880f;
        const float Margin = 30f;
        const float Top = 24f;
        const float Gap = 14f;
        const float ScoreboardHeight = 200f;
        // The page's round back/sound buttons float above the right end of the
        // score bar (see the WebGL template's placeCorner): about 100 units tall.
        const float PageButtonsTop = 270f;

        [SerializeField] RectTransform canvasRect;
        [SerializeField] CameraFramer framer;

        [Header("Scoreboard")]
        [SerializeField] RectTransform scoreboard;
        [SerializeField] ScoreRow[] rows;
        [SerializeField] Sprite ring;
        [SerializeField] Sprite disc;
        [SerializeField] Sprite tick;
        [SerializeField] Sprite cross;

        [Header("Prompt")]
        [SerializeField] RectTransform promptCard;
        [SerializeField] Text promptTitle;
        [SerializeField] Text promptBody;
        [SerializeField] Text roundText;
        [SerializeField] Text hintText;

        [Header("Menu and end screen")]
        [SerializeField] GameObject overlay;
        [SerializeField] Text overlayTitle;
        [SerializeField] Text overlayBody;
        [SerializeField] RawImage overlayPortrait;
        [SerializeField] Button primaryButton;
        [SerializeField] Text primaryLabel;
        [SerializeField] Button secondaryButton;
        [SerializeField] Text secondaryLabel;

        [Header("Pass the phone")]
        [SerializeField] GameObject passPanel;
        [SerializeField] Text passTitle;
        [SerializeField] Text passBody;
        [SerializeField] Button passButton;
        [SerializeField] Image passButtonImage;
        [SerializeField] Text passButtonLabel;

        static readonly Color Chalk = new Color(0.95f, 0.95f, 0.92f);
        static readonly Color Pending = new Color(0.95f, 0.95f, 0.92f, 0.35f);
        static readonly Color Scored = new Color(0.18f, 0.68f, 0.32f);
        static readonly Color Missed = new Color(0.84f, 0.24f, 0.21f);
        static readonly Color RowIdle = new Color(1f, 1f, 1f, 0.04f);
        static readonly Color RowActive = new Color(1f, 1f, 1f, 0.16f);

        public Button PrimaryButton => primaryButton;
        public Button SecondaryButton => secondaryButton;
        public Button PassButton => passButton;
        public bool PassShowing => passPanel != null && passPanel.activeSelf;
        public bool OverlayShowing => overlay != null && overlay.activeSelf;
        public int RowCount => rows?.Length ?? 0;

        float laidOutWidth;
        float laidOutHeight;

        void Update()
        {
            if (canvasRect == null) return;
            var size = canvasRect.rect.size;
            if (Mathf.Approximately(size.x, laidOutWidth) && Mathf.Approximately(size.y, laidOutHeight)) return;
            Layout(size);
        }

        /// <summary>Lay the HUD out for a canvas of this size (in reference units).</summary>
        void Layout(Vector2 size)
        {
            laidOutWidth = size.x;
            laidOutHeight = size.y;
            bool wide = size.x >= SideBySideWidth;

            float topReserved;
            if (wide)
            {
                Place(scoreboard, new Vector2(0f, 1f), new Vector2(Margin, -Top), new Vector2(900f, ScoreboardHeight));
                Place(promptCard, new Vector2(1f, 1f), new Vector2(-Margin, -Top), new Vector2(900f, ScoreboardHeight));
                topReserved = Top + ScoreboardHeight;
            }
            else
            {
                float width = Mathf.Min(940f, size.x - 2f * Margin);
                Place(scoreboard, new Vector2(0.5f, 1f), new Vector2(0f, -Top), new Vector2(width, ScoreboardHeight));
                Place(promptCard, new Vector2(0.5f, 1f), new Vector2(0f, -Top - ScoreboardHeight - Gap),
                    new Vector2(width, 180f));
                topReserved = Top + ScoreboardHeight + Gap + 180f;
            }

            // Overlay: stacked buttons on a tall screen, side by side on a short one.
            bool tall = size.y > size.x;
            PlaceCentre(overlayTitle.rectTransform, new Vector2(0f, tall ? 470f : 330f), new Vector2(Mathf.Min(1400f, size.x - 60f), 130f));
            PlaceCentre(overlayBody.rectTransform, new Vector2(0f, tall ? 360f : 225f), new Vector2(Mathf.Min(1100f, size.x - 80f), 150f));
            PlaceCentre(overlayPortrait.rectTransform, new Vector2(0f, tall ? 90f : 30f), Vector2.one * (tall ? 300f : 210f));
            var buttonSize = new Vector2(600f, 130f);
            if (tall)
            {
                PlaceCentre(primaryButton.transform as RectTransform, new Vector2(0f, -200f), buttonSize);
                PlaceCentre(secondaryButton.transform as RectTransform, new Vector2(0f, -370f), buttonSize);
            }
            else
            {
                PlaceCentre(primaryButton.transform as RectTransform, new Vector2(-330f, -240f), buttonSize);
                PlaceCentre(secondaryButton.transform as RectTransform, new Vector2(330f, -240f), buttonSize);
            }

            // Tell the camera how much of the screen is left for the pitch.
            // The page's buttons sit over the right end of the pitch either way.
            float bottomReserved = PageButtonsTop;
            framer?.SetBand(bottomReserved / size.y, 1f - (topReserved + 16f) / size.y);
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 sizeDelta)
        {
            if (rect == null) return;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = position;
        }

        static void PlaceCentre(RectTransform rect, Vector2 position, Vector2 sizeDelta) =>
            Place(rect, new Vector2(0.5f, 0.5f), position, sizeDelta);

        /// <summary>
        /// Which big button is at this screen point: 0 primary, 1 secondary, 2 the
        /// pass-the-phone button, -1 none. Only buttons on a showing screen count.
        /// </summary>
        public int ButtonAt(Vector2 screenPoint)
        {
            if (PassShowing && Contains(passButton, screenPoint)) return 2;
            if (!OverlayShowing || PassShowing) return -1;
            if (Contains(primaryButton, screenPoint)) return 0;
            if (Contains(secondaryButton, screenPoint)) return 1;
            return -1;
        }

        static bool Contains(Button button, Vector2 screenPoint) =>
            button != null && button.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, screenPoint, null);

        // ---------------------------------------------------------------- scoreboard

        /// <summary>Name, colour and squad portrait for one side of the shootout.</summary>
        public void SetPlayer(int index, string name, Color colour, SquadMember member)
        {
            if (rows == null || index >= rows.Length) return;
            var row = rows[index];
            Set(row.name, name);
            Set(row.squadName, $"{member.Name} {member.Shirt}".Trim());
            if (row.stripe != null) row.stripe.color = colour;

            if (row.portrait != null)
            {
                var portrait = Roster.LoadPortrait(member);
                row.portrait.texture = portrait;
                row.portrait.enabled = portrait != null;
            }
        }

        /// <summary>Light up the row of the player who is shooting.</summary>
        public void SetActive(int index)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].background != null) rows[i].background.color = i == index ? RowActive : RowIdle;
        }

        /// <summary>
        /// Show a player's kicks from <paramref name="firstRound"/> on, like a TV
        /// shootout graphic: a green tick for a goal, a red cross for a miss, an
        /// empty ring for a kick still to come.
        /// </summary>
        public void SetKicks(int index, IReadOnlyList<bool> kicks, int firstRound, int goals)
        {
            if (rows == null || index >= rows.Length) return;
            var row = rows[index];
            Set(row.goals, goals.ToString());

            for (int slot = 0; slot < row.marks.Length; slot++)
            {
                int round = firstRound + slot;
                bool taken = round < kicks.Count;
                var mark = row.marks[slot];
                var glyph = row.glyphs[slot];

                if (mark != null)
                {
                    mark.sprite = taken ? disc : ring;
                    mark.color = !taken ? Pending : kicks[round] ? Scored : Missed;
                }

                if (glyph != null)
                {
                    glyph.enabled = taken;
                    if (taken) glyph.sprite = kicks[round] ? tick : cross;
                }
            }
        }

        public int MarkSlots => rows != null && rows.Length > 0 ? rows[0].marks.Length : 0;

        // ---------------------------------------------------------------- prompt

        public void SetPrompt(string title, Color colour, string body)
        {
            Set(promptTitle, title);
            if (promptTitle != null) promptTitle.color = colour;
            Set(promptBody, body);
        }

        public void SetRound(string text) => Set(roundText, text);
        public void SetHint(string text) => Set(hintText, text);

        // ---------------------------------------------------------------- overlays

        public void ShowOverlay(string title, string body, Texture portrait,
            string primary, string secondary)
        {
            if (overlay != null) overlay.SetActive(true);
            Set(overlayTitle, title);
            Set(overlayBody, body);
            Set(primaryLabel, primary);
            Set(secondaryLabel, secondary);

            if (overlayPortrait != null)
            {
                overlayPortrait.texture = portrait;
                overlayPortrait.gameObject.SetActive(portrait != null);
            }
        }

        public void HideOverlay()
        {
            if (overlay != null) overlay.SetActive(false);
        }

        /// <summary>
        /// The pass-the-phone screen. Fully opaque, so the keeper cannot see where
        /// the shooter aimed while the phone changes hands.
        /// </summary>
        public void ShowPass(string title, string body, string button, Color colour)
        {
            if (passPanel != null) passPanel.SetActive(true);
            Set(passTitle, title);
            Set(passBody, body);
            Set(passButtonLabel, button);
            if (passButtonImage != null) passButtonImage.color = colour;
        }

        public void HidePass()
        {
            if (passPanel != null) passPanel.SetActive(false);
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }

        // ---------------------------------------------------------------- wiring

        public void BindLayout(RectTransform canvas, CameraFramer cameraFramer)
        {
            canvasRect = canvas;
            framer = cameraFramer;
        }

        public void BindScoreboard(RectTransform board, ScoreRow[] scoreRows,
            Sprite ringSprite, Sprite discSprite, Sprite tickSprite, Sprite crossSprite)
        {
            scoreboard = board;
            rows = scoreRows;
            ring = ringSprite;
            disc = discSprite;
            tick = tickSprite;
            cross = crossSprite;
        }

        public void BindPrompt(RectTransform card, Text title, Text body, Text round, Text hint)
        {
            promptCard = card;
            promptTitle = title;
            promptBody = body;
            roundText = round;
            hintText = hint;
        }

        public void BindOverlay(GameObject panel, Text title, Text body, RawImage portrait,
            Button primary, Text primaryText, Button secondary, Text secondaryText)
        {
            overlay = panel;
            overlayTitle = title;
            overlayBody = body;
            overlayPortrait = portrait;
            primaryButton = primary;
            primaryLabel = primaryText;
            secondaryButton = secondary;
            secondaryLabel = secondaryText;
        }

        public void BindPass(GameObject panel, Text title, Text body, Button button, Image buttonImage, Text label)
        {
            passPanel = panel;
            passTitle = title;
            passBody = body;
            passButton = button;
            passButtonImage = buttonImage;
            passButtonLabel = label;
        }
    }
}
