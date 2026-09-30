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

        Vector3 home;
        Quaternion homeRotation;
        Coroutine routine;

        void Awake()
        {
            home = transform.position;
            homeRotation = transform.rotation;
        }

        /// <summary>Dive toward a lane centre on the goal line.</summary>
        public void Dive(Vector3 lanePoint)
        {
            if (routine != null) StopCoroutine(routine);

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
            Vector3 start = transform.position;
            float lateral = target.x - start.x;
            float lean = Mathf.Clamp(lateral * 18f, -72f, 72f);
            Quaternion targetRotation = homeRotation * Quaternion.Euler(0f, 0f, -lean);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / diveDuration;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

                Vector3 position = Vector3.Lerp(start, target, eased);
                position.y = home.y + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * diveHeight
                                    * Mathf.Clamp01(Mathf.Abs(lateral));
                transform.position = position;
                transform.rotation = Quaternion.Slerp(homeRotation, targetRotation, eased);
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
