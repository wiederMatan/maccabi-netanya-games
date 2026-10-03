using System.Collections;
using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// A bonus ring hung in a top corner of the goal. Its size comes from the level;
    /// it pulses when a shot goes through it.
    /// </summary>
    public class TargetRing : MonoBehaviour
    {
        [SerializeField] int side = -1;
        [SerializeField] Transform visual;

        Coroutine pulse;
        float radius = 0.6f;

        public int Side => side;
        public Transform Visual => visual;

        public void Bind(int ringSide, Transform ringVisual)
        {
            side = ringSide;
            visual = ringVisual;
        }

        public void Configure(float ringRadius)
        {
            radius = ringRadius;
            var centre = Pitch.RingCentre(side, radius);
            transform.position = new Vector3(centre.x, centre.y, Pitch.GoalLineZ + 0.02f);
            SetSize(1f);
        }

        void SetSize(float factor)
        {
            // The ring texture's outer edge sits at the quad's edge, so a quad twice
            // the radius across matches the area that scores.
            if (visual != null) visual.localScale = Vector3.one * radius * 2f * factor;
        }

        public void Pulse()
        {
            if (pulse != null) StopCoroutine(pulse);
            pulse = StartCoroutine(PulseRoutine());
        }

        IEnumerator PulseRoutine()
        {
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                SetSize(1f + 0.35f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1f - t / 1.2f));
                yield return null;
            }
            SetSize(1f);
            pulse = null;
        }
    }
}
