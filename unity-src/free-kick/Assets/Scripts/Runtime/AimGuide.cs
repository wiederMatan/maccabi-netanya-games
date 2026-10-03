using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// The dotted arc shown while the player drags: where the ball will fly if they
    /// let go now. Far dots are drawn bigger so the arc reads evenly despite the
    /// perspective. On the easy levels a ring marks where it will cross the goal line.
    /// </summary>
    public class AimGuide : MonoBehaviour
    {
        [SerializeField] Transform[] dots;
        [SerializeField] Transform marker;
        [SerializeField] float nearSize = 0.11f;
        [SerializeField] float farSize = 0.26f;

        public int DotCount => dots == null ? 0 : dots.Length;
        public bool Visible { get; private set; }

        public void Bind(Transform[] dotTransforms, Transform crossingMarker)
        {
            dots = dotTransforms;
            marker = crossingMarker;
        }

        void Awake() => Hide();

        /// <summary>Lay the dots along the first <paramref name="length"/> (0..1) of the flight.</summary>
        public void Show(in ShotPath path, float length)
        {
            Visible = true;
            length = Mathf.Clamp01(length);

            for (int i = 0; i < dots.Length; i++)
            {
                // Start one step out so the first dot is not hidden inside the ball.
                float s = (i + 1f) / dots.Length;
                bool on = s <= length + 0.001f;
                dots[i].gameObject.SetActive(on);
                if (!on) continue;
                dots[i].position = path.Evaluate(s, withWind: false);
                dots[i].localScale = Vector3.one * Mathf.Lerp(nearSize, farSize, s);
            }

            if (marker != null)
            {
                bool full = length >= 0.999f;
                marker.gameObject.SetActive(full);
                if (full) marker.position = path.Evaluate(1f, withWind: false) + Vector3.back * 0.05f;
            }
        }

        public void Hide()
        {
            Visible = false;
            if (dots != null)
                foreach (var dot in dots) if (dot != null) dot.gameObject.SetActive(false);
            if (marker != null) marker.gameObject.SetActive(false);
        }
    }
}
