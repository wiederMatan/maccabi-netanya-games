using System.Collections;
using MaccabiShared;
using UnityEngine;
using UnityEngine.UI;

namespace MathStrikers
{
    /// <summary>
    /// The big word in the middle of the pitch after a shot (גול! / החמצה):
    /// pops in with overshoot, holds about a second, then fades.
    /// </summary>
    public class ResultPop : MonoBehaviour
    {
        const float PopSeconds = 0.3f;
        const float HoldSeconds = 1f;
        const float FadeSeconds = 0.3f;

        [SerializeField] Text word;
        Coroutine routine;

        public void Bind(Text text) => word = text;

        void Awake()
        {
            if (word != null) word.enabled = false;
        }

        public void Show(string hebrew, Color color)
        {
            if (word == null) return;
            if (routine != null) StopCoroutine(routine);
            word.text = Rtl.Fix(hebrew);
            routine = StartCoroutine(Play(color));
        }

        public void Hide()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (word != null) word.enabled = false;
        }

        IEnumerator Play(Color color)
        {
            word.enabled = true;
            word.color = color;
            var rect = word.rectTransform;

            for (float t = 0f; t < PopSeconds; t += Time.unscaledDeltaTime)
            {
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, Ease.OutBack(t / PopSeconds, 2.4f));
                yield return null;
            }
            rect.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(HoldSeconds);

            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                word.color = Palette.WithAlpha(color, 1f - t / FadeSeconds);
                yield return null;
            }
            word.enabled = false;
            routine = null;
        }
    }
}
