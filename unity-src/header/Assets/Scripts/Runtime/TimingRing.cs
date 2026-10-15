using UnityEngine;
using UnityEngine.UI;

namespace Headers
{
    /// <summary>
    /// The jump cue. A fixed ring marks where the ball will meet the attacker's
    /// head, and a gold ring closes in on it from outside; it lands on the fixed
    /// ring at the perfect moment to jump. While the jump would still win the
    /// header the fixed ring glows green, so a child can read the timing without
    /// reading anything else.
    /// </summary>
    public class TimingRing : MonoBehaviour
    {
        const float StartScale = 3.2f;
        const float Size = 150f;

        [SerializeField] RectTransform root;
        [SerializeField] Image target;
        [SerializeField] Image closer;

        static readonly Color Idle = new Color(1f, 0.965f, 0.867f, 0.85f);
        static readonly Color Ready = new Color(0.36f, 0.95f, 0.45f, 1f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.25f, 1f);

        Camera cam;
        Canvas canvas;
        Vector3 world;
        float shownAt;
        float perfectAt;
        float tolerance;
        bool active;
        float fadeFrom = -1f;

        public void Bind(RectTransform ringRoot, Image targetRing, Image closingRing)
        {
            root = ringRoot;
            target = targetRing;
            closer = closingRing;
        }

        void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            Hide();
        }

        /// <summary>
        /// Start closing on <paramref name="point"/> now, landing at
        /// <paramref name="perfect"/> (Time.time), and glowing green for
        /// <paramref name="window"/> seconds either side of it.
        /// </summary>
        public void Show(Vector3 point, float perfect, float window)
        {
            world = point;
            shownAt = Time.time;
            perfectAt = perfect;
            tolerance = window;
            active = true;
            fadeFrom = -1f;
            if (root != null) root.gameObject.SetActive(true);
            Update();
        }

        /// <summary>The jump is in, or the ball has gone: fade the rings away.</summary>
        public void Release()
        {
            if (active && fadeFrom < 0f) fadeFrom = Time.time;
        }

        public void Hide()
        {
            active = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!active || root == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            // Follow the meeting point on screen, in the canvas's own units.
            Vector3 screen = cam.WorldToScreenPoint(world);
            var parent = (RectTransform)root.parent;
            if (screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen,
                    canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, out var local))
                root.anchoredPosition = local;

            float now = Time.time;
            float lead = Mathf.Max(0.05f, perfectAt - shownAt);
            float left = (perfectAt - now) / lead;
            float scale = left > 0f ? 1f + (StartScale - 1f) * left : Mathf.Max(0.55f, 1f + left * 0.9f);
            closer.rectTransform.sizeDelta = Vector2.one * Size * scale;
            target.rectTransform.sizeDelta = Vector2.one * Size;

            bool ready = Mathf.Abs(now - perfectAt) <= tolerance;
            float alpha = 1f;
            if (fadeFrom >= 0f)
            {
                alpha = 1f - (now - fadeFrom) / 0.25f;
                if (alpha <= 0f) { Hide(); return; }
            }
            else if (now > perfectAt + tolerance)
            {
                alpha = Mathf.Clamp01(1f - (now - perfectAt - tolerance) / 0.3f);
            }

            var t = ready ? Ready : Idle;
            t.a *= alpha;
            target.color = t;
            var c = Gold;
            c.a *= alpha * Mathf.Clamp01((now - shownAt) / 0.15f);
            closer.color = c;
            float pulse = ready ? 1f + 0.08f * Mathf.Sin(now * 30f) : 1f;
            target.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
        }
    }
}
