using System.Collections;
using MaccabiShared;
using UnityEngine;
using UnityEngine.UI;

namespace MathStrikers
{
    /// <summary>
    /// Owns every piece of on-screen text. The match manager pushes state in; this
    /// class decides how it reads. Hebrew goes through Rtl, because the legacy Text
    /// lays glyphs out left to right; the sums themselves stay left to right.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        // Characters per line for the overlay paragraph: it is pre-wrapped here,
        // since wrapping after the RTL reversal would put the last line on top.
        const int BodyLineChars = 38;
        // Earned stars fill one by one once the card has popped in, each with a sparkle.
        const float FirstStarDelay = 0.9f;
        const float StarStagger = 0.25f;

        Coroutine starFill;

        [SerializeField] Text problemText;
        [SerializeField] Text shotText;
        [SerializeField] Text scoreText;
        [SerializeField] Text streakText;
        [SerializeField] Text scorelineText;
        [SerializeField] Text bannerTop;
        [SerializeField] Text bannerText;
        [SerializeField] Text feedbackText;
        [SerializeField] Text timerText;
        [SerializeField] Image timerFill;
        [SerializeField] GameObject startPanel;
        [SerializeField] Text startTitle;
        [SerializeField] Text startBody;
        [SerializeField] Button startButton;
        [SerializeField] Text startButtonLabel;
        [SerializeField] GameObject starRow;
        [SerializeField] Image[] stars;
        [SerializeField] ResultPop resultPop;
        [SerializeField] Button[] difficultyButtons;
        [SerializeField] Image[] difficultyFaces;
        [SerializeField] Image[] difficultyEdges;
        [SerializeField] Text[] difficultyLabels;
        [SerializeField] Text[] difficultyHints;
        [SerializeField] Sprite pickedFace;
        [SerializeField] Sprite pickedEdge;
        [SerializeField] Sprite restingFace;
        [SerializeField] Sprite restingEdge;

        public Button StartButton => startButton;
        public Button[] DifficultyButtons => difficultyButtons;

        public void Bind(Text problem, Text shot, Text feedback, Text timer, Image fill)
        {
            problemText = problem;
            shotText = shot;
            feedbackText = feedback;
            timerText = timer;
            timerFill = fill;
        }

        public void BindScoreBar(Text score, Text streak, Text scoreline, Text matchLine, Text opponentLine)
        {
            scoreText = score;
            streakText = streak;
            scorelineText = scoreline;
            bannerTop = matchLine;
            bannerText = opponentLine;
        }

        public void BindOverlay(GameObject panel, Text title, Text body, Button button, Text buttonLabel,
            GameObject starContainer, Image[] starImages, ResultPop result)
        {
            startPanel = panel;
            startTitle = title;
            startBody = body;
            startButton = button;
            startButtonLabel = buttonLabel;
            starRow = starContainer;
            stars = starImages;
            resultPop = result;
        }

        public void BindDifficulty(Button[] buttons, Image[] faces, Image[] edges, Text[] labels, Text[] hints,
            Sprite goldFace, Sprite goldEdge, Sprite navyFace, Sprite navyEdge)
        {
            difficultyButtons = buttons;
            difficultyFaces = faces;
            difficultyEdges = edges;
            difficultyLabels = labels;
            difficultyHints = hints;
            pickedFace = goldFace;
            pickedEdge = goldEdge;
            restingFace = navyFace;
            restingEdge = navyEdge;
        }

        /// <summary>Light up the chosen tier so the current setting is never ambiguous.</summary>
        public void HighlightDifficulty(int index)
        {
            if (difficultyFaces == null) return;

            for (int i = 0; i < difficultyFaces.Length; i++)
            {
                bool picked = i == index;
                if (difficultyFaces[i] != null) difficultyFaces[i].sprite = picked ? pickedFace : restingFace;
                if (difficultyEdges != null && i < difficultyEdges.Length && difficultyEdges[i] != null)
                    difficultyEdges[i].sprite = picked ? pickedEdge : restingEdge;

                var ink = picked ? Palette.Navy900 : Palette.Cream;
                if (difficultyLabels != null && i < difficultyLabels.Length && difficultyLabels[i] != null)
                    difficultyLabels[i].color = ink;
                if (difficultyHints != null && i < difficultyHints.Length && difficultyHints[i] != null)
                    difficultyHints[i].color = Palette.WithAlpha(ink, 0.75f);
            }
        }

        /// <summary>The sum is left to right on purpose ("7 + 3 = ?"), so no Rtl here.</summary>
        public void SetProblem(string text) => Set(problemText, text);

        public void SetShot(int number, int total) =>
            Set(shotText, number <= 0 ? "" : Rtl.Fix($"בעיטה {number} מתוך {total}"));

        public void SetScore(int value) => Set(scoreText, value.ToString());
        public void SetStreak(int value) => Set(streakText, value.ToString());

        /// <summary>Two short lines at the end of the score bar: which match, and who against.</summary>
        public void SetBanner(string top, string main)
        {
            Set(bannerTop, Rtl.Fix(top));
            Set(bannerText, Rtl.Fix(main));
        }

        public void SetFeedback(string hebrew, Color color)
        {
            if (feedbackText != null) feedbackText.color = color;
            Set(feedbackText, Rtl.Fix(hebrew));
        }

        public void ClearFeedback() => Set(feedbackText, "");

        /// <summary>
        /// Laid out as the eye reads it right to left: our goals first (gold), then
        /// the opponent's. Built in visual order, so it skips Rtl.
        /// </summary>
        public void SetScoreline(int player, int opponent) =>
            Set(scorelineText, $"{opponent} : <color=#FFD23F>{player}</color>");

        public void ShowResult(string hebrew, Color color) => resultPop?.Show(hebrew, color);
        public void HideResult() => resultPop?.Hide();

        public void SetTimer(float remaining, float limit)
        {
            float fraction = limit <= 0f ? 0f : Mathf.Clamp01(remaining / limit);
            var colour = remaining <= 8f ? Palette.Red400 : Palette.Gold400;
            if (timerFill != null)
            {
                // Driven by the anchor rather than Image.fillAmount, so the sliced
                // pill keeps its round ends. It drains toward the right, where a
                // Hebrew reader starts.
                var rect = timerFill.rectTransform;
                rect.anchorMin = new Vector2(1f - fraction, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                timerFill.enabled = fraction > 0.01f;
                timerFill.color = colour;
            }

            if (timerText != null)
            {
                timerText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
                timerText.color = colour;
            }
        }

        /// <summary>
        /// Show the start / full-time card. Title, body and button are logical
        /// Hebrew. stars &lt; 0 hides the star row (the start screen).
        /// </summary>
        public void ShowOverlay(string title, string body, string buttonLabel, int earnedStars = -1)
        {
            HideResult();
            if (startPanel != null) startPanel.SetActive(true);
            Set(startTitle, Rtl.Fix(title));
            Set(startBody, Rtl.Wrap(body, BodyLineChars));
            Set(startButtonLabel, Rtl.Fix(buttonLabel));

            if (starRow != null) starRow.SetActive(earnedStars >= 0);
            if (stars == null) return;
            for (int i = 0; i < stars.Length; i++) PaintStar(i, false);
            if (starFill != null) StopCoroutine(starFill);
            starFill = earnedStars > 0 ? StartCoroutine(FillStars(earnedStars)) : null;
        }

        IEnumerator FillStars(int earned)
        {
            yield return new WaitForSecondsRealtime(FirstStarDelay);
            for (int i = 0; i < earned && i < stars.Length; i++)
            {
                PaintStar(i, true);
                MatchAudio.Instance?.PlayStar();
                if (stars[i] != null) StartCoroutine(PopStar(stars[i].rectTransform));
                yield return new WaitForSecondsRealtime(StarStagger);
            }
            starFill = null;
        }

        static IEnumerator PopStar(RectTransform star)
        {
            const float seconds = 0.25f;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                star.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, Ease.OutBack(t / seconds, 2.4f));
                yield return null;
            }
            star.localScale = Vector3.one;
        }

        void PaintStar(int index, bool earned)
        {
            var star = stars[index];
            if (star == null) return;
            star.color = earned ? Palette.Gold400 : Palette.Navy700;
            star.rectTransform.localScale = Vector3.one;
            var outline = star.GetComponent<Outline>();
            if (outline != null) outline.effectColor = earned ? Palette.Gold900 : Palette.Navy900;
        }

        public void HideOverlay()
        {
            if (starFill != null) StopCoroutine(starFill);
            starFill = null;
            if (startPanel != null) startPanel.SetActive(false);
        }

        static void Set(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
