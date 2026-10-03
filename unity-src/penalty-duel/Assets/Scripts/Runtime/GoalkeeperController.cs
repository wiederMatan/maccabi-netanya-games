using System.Collections;
using UnityEngine;

namespace PenaltyDuel
{
    /// <summary>
    /// The keeper throws himself at whichever spot his player picked. Whether that
    /// stops the shot is decided by <see cref="Shootout.IsSave"/>, so the dive only
    /// has to look right: sideways for the corners (the one dive clip, mirrored for
    /// the other side), and a jump or a set stance for the middle.
    /// </summary>
    public class GoalkeeperController : MonoBehaviour
    {
        [SerializeField] float diveDuration = 0.5f;
        [SerializeField] float recoverDuration = 0.4f;
        [SerializeField] float diveHeight = 0.45f;
        [SerializeField] float jumpHeight = 0.5f;
        // How far toward the spot's x the body travels; the arms reach the rest.
        [SerializeField] float diveReach = 0.7f;

        // Anim_Dive throws the keeper toward world -x when he faces down the pitch
        // (-z); a dive toward +x plays it mirrored.
        const bool ClipDivesTowardNegativeX = true;

        static readonly int DiveTrigger = Animator.StringToHash("Dive");
        static readonly int MirrorParameter = Animator.StringToHash("Mirror");

        Vector3 home;
        Quaternion homeRotation;
        Coroutine routine;
        Animator animator;

        void Awake()
        {
            home = transform.position;
            homeRotation = transform.rotation;
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Dive (or jump) toward a spot on the goal line.</summary>
        public void Dive(Vector3 spotPoint, bool high)
        {
            if (routine != null) StopCoroutine(routine);

            bool middle = Mathf.Abs(spotPoint.x) < 0.5f;
            if (middle)
            {
                // Straight at him: a jump for a high one, stand tall for a low one.
                routine = StartCoroutine(JumpRoutine(high ? jumpHeight : 0.08f));
                return;
            }

            if (animator != null)
            {
                bool towardNegative = spotPoint.x < 0f;
                animator.SetBool(MirrorParameter, towardNegative != ClipDivesTowardNegativeX);
                animator.SetTrigger(DiveTrigger);
            }

            Vector3 target = new Vector3(spotPoint.x * diveReach, home.y + (high ? diveHeight : 0f), home.z);
            routine = StartCoroutine(DiveRoutine(target));
        }

        public void ResetStance()
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(RecoverRoutine());
        }

        IEnumerator DiveRoutine(Vector3 target)
        {
            // The dive clip handles the body; this only carries him across (and up,
            // for a high one) so the animation and the travel do not fight.
            Vector3 start = transform.position;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / diveDuration;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                Vector3 p = Vector3.Lerp(start, target, eased);
                // Rise into the dive, then drop back to the grass.
                p.y = home.y + (target.y - home.y) * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.85f);
                transform.position = p;
                yield return null;
            }

            // Let gravity have him once the dive is spent.
            Vector3 landed = transform.position;
            for (float fall = 0f; fall < 1f; fall += Time.deltaTime / 0.2f)
            {
                transform.position = Vector3.Lerp(landed, new Vector3(landed.x, home.y, landed.z), fall);
                yield return null;
            }
            transform.position = new Vector3(landed.x, home.y, landed.z);
            routine = null;
        }

        IEnumerator JumpRoutine(float height)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / (diveDuration * 1.3f);
                transform.position = home + Vector3.up * (height * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
                yield return null;
            }
            transform.position = home;
            routine = null;
        }

        IEnumerator RecoverRoutine()
        {
            Vector3 start = transform.position;
            Quaternion startRotation = transform.rotation;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / recoverDuration;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                transform.position = Vector3.Lerp(start, home, eased);
                transform.rotation = Quaternion.Slerp(startRotation, homeRotation, eased);
                yield return null;
            }

            transform.position = home;
            transform.rotation = homeRotation;
            routine = null;
        }
    }
}
