using System.Collections;
using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// The Maccabi Netanya player watching from beside the juggling column. He idles
    /// while the ball is up and jumps for joy, spinning, when a milestone falls.
    ///
    /// Where he stands depends on the window: in a wide one there is room beside the
    /// play area, so he stands at its edge; in a phone held upright there is not, so
    /// he steps back behind it, where the ball passes in front of him.
    /// </summary>
    public class PlayerCheer : MonoBehaviour
    {
        const float SideGap = 1.1f;
        const float SideDepth = 1.5f;
        static readonly Vector3 BehindMark = new Vector3(1.0f, 0f, 6f);

        Animator animator;
        Coroutine routine;
        Vector3 mark;
        Quaternion facing;
        static readonly int KickTrigger = Animator.StringToHash("Kick");

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            mark = transform.position;
            facing = transform.rotation;
        }

        /// <summary>Stand beside the play area if it fits on screen, otherwise behind it.</summary>
        public void Place(float playHalfWidth, float visibleHalfWidth)
        {
            float side = -(playHalfWidth + SideGap);
            bool fits = -side + 0.6f < visibleHalfWidth;
            mark = fits ? new Vector3(side, 0f, SideDepth) : BehindMark;

            // Face the camera, turned a little toward the middle of the column.
            float turn = fits ? 25f : -15f;
            facing = Quaternion.Euler(0f, 180f - turn, 0f);

            if (routine == null) transform.SetPositionAndRotation(mark, facing);
        }

        /// <summary>A little celebration: two hops and a spin, plus a kick of joy on a big one.</summary>
        public void Celebrate(bool big)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Jump(big));
        }

        IEnumerator Jump(bool big)
        {
            if (big && animator != null) animator.SetTrigger(KickTrigger);

            const float seconds = 1.3f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float phase = t / seconds;
                // Two hops, the second smaller.
                float hop = phase < 0.5f
                    ? Mathf.Sin(phase * 2f * Mathf.PI) * 0.45f
                    : Mathf.Sin((phase - 0.5f) * 2f * Mathf.PI) * 0.3f;
                float spin = Mathf.SmoothStep(0f, 360f, phase);
                transform.SetPositionAndRotation(mark + Vector3.up * Mathf.Max(0f, hop),
                    facing * Quaternion.Euler(0f, spin, 0f));
                yield return null;
            }

            transform.SetPositionAndRotation(mark, facing);
            routine = null;
        }
    }
}
