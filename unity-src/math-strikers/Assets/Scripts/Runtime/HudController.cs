using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MathStrikers
{
    /// <summary>
    /// Owns every piece of on-screen text. The match manager pushes state in; this
    /// class decides how it reads.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] Text problemText;
        [SerializeField] Text scoreText;
        [SerializeField] Text streakText;
        [SerializeField] Text scorelineText;
        [SerializeField] Text bannerText;
        [SerializeField] Text feedbackText;
        [SerializeField] Text timerText;
        [SerializeField] Image timerFill;
        [SerializeField] GameObject startPanel;
        [SerializeField] Text startTitle;
        [SerializeField] Text startBody;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;
        [SerializeField] GameObject goalCard;
        [SerializeField] CanvasGroup goalCardGroup;
        [SerializeField] RawImage goalCardImage;
        [SerializeField] Text goalCardHeadline;
        [SerializeField] Text goalCardName;
        [SerializeField] Text goalCardNumber;
        [SerializeField] RawImage portraitImage;
        [SerializeField] Text portraitName;
        [SerializeField] Text portraitNumber;
        [SerializeField] Button[] difficultyButtons;
        [SerializeField] Image[] difficultyBackgrounds;
        [SerializeField] Text[] difficultyLabels;

        static readonly Color Calm = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color Urgent = new Color(0.88f, 0.28f, 0.25f);

        public Button StartButton => startButton;
        public Button[] DifficultyButtons => difficultyButtons;

        static readonly Color PickedFill = new Color(0.91f, 0.71f, 0.30f);
        static readonly Color RestingFill = new Color(0.10f, 0.20f, 0.28f);
        static readonly Color PickedInk = new Color(0.05f, 0.11f, 0.17f);
        static readonly Color RestingInk = new Color(0.86f, 0.88f, 0.86f);

        public void Bind(Text problem, Text score, Text streak, Text scoreline, Text banner,
            Text feedback, Text timer, Image fill, GameObject panel, Text title, Text body,
            Button button, Text buttonLabel)
        {
            problemText = problem;
            scoreText = score;
            streakText = streak;
            scorelineText = scoreline;
            bannerText = banner;
            feedbackText = feedback;
            timerText = timer;
            timerFill = fill;
            startPanel = panel;
            startTitle = title;
            startBody = body;
            startButton = button;
            startButtonLabel = buttonLabel;
        }

        public void BindGoalCard(GameObject root, CanvasGroup group, RawImage image,
            Text headline, Text name, Text number)
        {
            goalCard = root;
            goalCardGroup = group;
            goalCardImage = image;
            goalCardHeadline = headline;
            goalCardName = name;
            goalCardNumber = number;
            if (goalCard != null) goalCard.SetActive(false);
        }

        /// <summary>
        /// The broadcast scorer graphic. A front-facing club portrait is wasted on a
        /// figure seen from behind, so this is where the real photo actually lands.
        /// </summary>
        public void ShowGoalCard(SquadMember member, string headline)
        {
            if (goalCard == null) return;

            Set(goalCardHeadline, headline);
            Set(goalCardName, member.Name);
            Set(goalCardNumber, member.Shirt);

            if (goalCardImage != null)
            {
                var portrait = Roster.LoadPortrait(member);
                goalCardImage.texture = portrait;
                goalCardImage.enabled = portrait != null;
            }

            goalCard.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(RevealGoalCard());
        }

        /// <summary>
        /// Jump straight to the revealed state. Edit-mode tooling cannot run the
        /// coroutine past its first yield, which would otherwise leave the card
        /// stuck at zero alpha in a headless capture.
        /// </summary>
        public void RevealGoalCardInstantly()
        {
            StopAllCoroutines();
            if (goalCardGroup != null) goalCardGroup.alpha = 1f;
            if (goalCard != null) goalCard.GetComponent<RectTransform>().localScale = Vector3.one;
        }

        public void HideGoalCard()
        {
            StopAllCoroutines();
            if (goalCard != null) goalCard.SetActive(false);
        }

        IEnumerator RevealGoalCard()
        {
            var card = goalCard.GetComponent<RectTransform>();
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime * 4.5f;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                if (goalCardGroup != null) goalCardGroup.alpha = eased;
                if (card != null)
                    card.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, eased);
                yield return null;
            }

            if (goalCardGroup != null) goalCardGroup.alpha = 1f;
            if (card != null) card.localScale = Vector3.one;
        }

        public void BindPortrait(RawImage image, Text name, Text number)
        {
            portraitImage = image;
            portraitName = name;
            portraitNumber = number;
        }

        /// <summary>Show which Maccabi Netanya player is taking the shots.</summary>
        public void SetStriker(SquadMember member)
        {
            Set(portraitName, member.Name);
            Set(portraitNumber, member.Shirt);

            if (portraitImage == null) return;

            var portrait = Roster.LoadPortrait(member);
            portraitImage.texture = portrait;
            portraitImage.enabled = portrait != null;
        }

        public void BindDifficulty(Button[] buttons, Image[] backgrounds, Text[] labels)
        {
            difficultyButtons = buttons;
            difficultyBackgrounds = backgrounds;
            difficultyLabels = labels;
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

        public void SetProblem(string text) => Set(problemText, text);
        public void SetScore(int value) => Set(scoreText, value.ToString());
        public void SetStreak(int value) => Set(streakText, value.ToString());
        public void SetBanner(string text) => Set(bannerText, text);
        public void SetFeedback(string text) => Set(feedbackText, text);

        public void SetScoreline(int player, int opponent) =>
            Set(scorelineText, $"{player} – {opponent}");

        public void SetTimer(float remaining, float limit)
        {
            float fraction = limit <= 0f ? 0f : Mathf.Clamp01(remaining / limit);
            if (timerFill != null)
            {
                // Driven by the anchor rather than Image.fillAmount: a filled Image
                // needs a source sprite to render, and this bar is a bare colour.
                var rect = timerFill.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(fraction, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                timerFill.color = remaining <= 8f ? Urgent : Calm;
            }

            if (timerText != null)
            {
                timerText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, remaining))}s";
                timerText.color = remaining <= 8f ? Urgent : Calm;
            }
        }

        public void ShowOverlay(string title, string body, string buttonLabel)
        {
            if (startPanel != null) startPanel.SetActive(true);
            Set(startTitle, title);
            Set(startBody, body);
            Set(startButtonLabel, buttonLabel);
        }

        public void HideOverlay()
        {
            if (startPanel != null) startPanel.SetActive(false);
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
