using System.Collections;
using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// The keeper picks a lane the instant the ball is struck and throws himself at
    /// it. He only ever guesses - whether that guess saves the shot is decided by
    /// <see cref="MatchManager"/>, so the dive is pure theatre that has to look right.
    /// </summary>
    public class GoalkeeperController : MonoBehaviour
    {
        [SerializeField] float diveDuration = 0.55f;
        [SerializeField] float recoverDuration = 0.4f;
        [SerializeField] float diveHeight = 0.55f;

        static readonly int DiveTrigger = Animator.StringToHash("Dive");

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

        /// <summary>Dive toward a lane centre on the goal line.</summary>
        public void Dive(Vector3 lanePoint)
        {
            if (routine != null) StopCoroutine(routine);

            if (animator != null) animator.SetTrigger(DiveTrigger);

            Vector3 target = new Vector3(lanePoint.x, home.y, home.z);
            routine = StartCoroutine(DiveRoutine(target));
        }

        public void ResetStance()
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(RecoverRoutine());
        }

        IEnumerator DiveRoutine(Vector3 target)
        {
            // The dive clip handles the body; this only carries him sideways to the
            // lane he picked, so the animation and the travel do not fight.
            Vector3 start = transform.position;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / diveDuration;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                transform.position = Vector3.Lerp(start, target, eased);
                yield return null;
            }

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
