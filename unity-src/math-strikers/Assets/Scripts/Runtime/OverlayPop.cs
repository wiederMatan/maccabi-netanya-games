using System.Collections;
using UnityEngine;

namespace MathStrikers
{
    /// <summary>Easing shared by the HUD's pop animations.</summary>
    public static class Ease
    {
        /// <summary>Ease out with a slight overshoot past 1 before settling.</summary>
        public static float OutBack(float t, float overshoot = 1.7f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }
    }

    /// <summary>
    /// Pops an overlay in whenever it is shown: the dim layer fades up while the
    /// card grows from 85% with a little overshoot.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class OverlayPop : MonoBehaviour
    {
        const float Seconds = 0.25f;

        [SerializeField] RectTransform card;

        public void Bind(RectTransform target) => card = target;

        void OnEnable() => StartCoroutine(Pop());

        IEnumerator Pop()
        {
            var group = GetComponent<CanvasGroup>();
            for (float t = 0f; t < Seconds; t += Time.unscaledDeltaTime)
            {
                float k = t / Seconds;
                group.alpha = Mathf.Clamp01(k * 1.6f);
                if (card != null) card.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, Ease.OutBack(k));
                yield return null;
            }
            group.alpha = 1f;
            if (card != null) card.localScale = Vector3.one;
        }
    }
}
