using System.Collections;
using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// The keeper takes up his spot for each kick and dives when the ball is struck.
    /// Whether the dive saves it is decided by <see cref="ShotJudge"/>, so the dive is
    /// pure theatre that has to look right: a save arrives on the ball, a miss falls short.
    /// </summary>
    public class GoalkeeperController : MonoBehaviour
    {
        // The dive clip throws him to his own left, which is the camera's right.
        // Mirroring the clip sends him the other way.
        static readonly int DiveTrigger = Animator.StringToHash("Dive");
        static readonly int MirrorParam = Animator.StringToHash("Mirror");

        // Seconds from the dive starting until he is stretched out flat.
        public const float DiveReach = 0.75f;

        Vector3 home;
        Coroutine routine;
        Animator animator;

        void Awake()
        {
            home = transform.position;
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Stand on the line at <paramref name="x"/>, facing the ball.</summary>
        public void TakePosition(float x, Vector3 ball)
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            home = new Vector3(x, 0f, Pitch.GoalLineZ - 0.35f);
            transform.position = home;
            Vector3 look = ball - home;
            look.y = 0f;
            transform.rotation = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.back);

            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }

        /// <summary>
        /// Dive toward <paramref name="point"/> on the goal line, reaching full stretch
        /// <see cref="DiveReach"/> seconds from now. <paramref name="reach"/> is how much
        /// of the way he gets: 1 for a save, less when he is beaten.
        /// </summary>
        public void Dive(Vector3 point, float reach)
        {
            if (routine != null) StopCoroutine(routine);

            float dx = point.x - home.x;
            if (animator != null)
            {
                animator.SetBool(MirrorParam, dx < 0f);
                if (Mathf.Abs(dx) > 0.5f) animator.SetTrigger(DiveTrigger);
            }

            // Hands up for a high ball: lift him, since the clip dives along the grass.
            float lift = Mathf.Clamp(point.y - 0.9f, 0f, 1.3f) * reach;
            var target = new Vector3(home.x + dx * reach, home.y + lift, home.z);
            routine = StartCoroutine(DiveRoutine(target));
        }

        IEnumerator DiveRoutine(Vector3 target)
        {
            // The clip handles the body; this carries him across to where he is going.
            Vector3 start = transform.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / DiveReach;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                var p = Vector3.Lerp(start, target, eased);
                // The lift peaks at full stretch and he falls back to the grass after.
                p.y = Mathf.Lerp(start.y, target.y, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f));
                transform.position = p;
                yield return null;
            }

            for (float fall = 0f; fall < 0.35f; fall += Time.deltaTime)
            {
                var p = transform.position;
                p.y = Mathf.Lerp(target.y, home.y, fall / 0.35f);
                transform.position = p;
                yield return null;
            }

            var settled = transform.position;
            settled.y = home.y;
            transform.position = settled;
            routine = null;
        }
    }
}
