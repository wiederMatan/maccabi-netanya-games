using System.Collections;
using UnityEngine;

namespace Headers
{
    /// <summary>
    /// The keeper takes up his spot for each corner and dives at the header.
    /// Whether he saves it is decided by <see cref="HeaderJudge"/>, so the dive only
    /// has to look right: a save arrives on the ball, a goal leaves him short.
    /// </summary>
    public class Keeper : MonoBehaviour
    {
        // The dive clip throws him to his own left, which is the camera's right.
        // Mirroring the clip sends him the other way.
        static readonly int DiveTrigger = Animator.StringToHash("Dive");
        static readonly int MirrorParam = Animator.StringToHash("Mirror");

        Vector3 home;
        Coroutine routine;
        Animator animator;

        public float X => home.x;

        void Awake()
        {
            home = transform.position;
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Stand on the line at <paramref name="x"/>, facing <paramref name="lookAt"/>.</summary>
        public void TakePosition(float x, Vector3 lookAt)
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            home = new Vector3(x, 0f, Field.KeeperLineZ);
            transform.position = home;
            Watch(lookAt, true);

            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }

        /// <summary>Turn to keep his eyes on the ball while the corner is in the air.</summary>
        public void Watch(Vector3 ball, bool snap = false)
        {
            if (routine != null) return;
            var look = ball - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;
            var target = Quaternion.LookRotation(look);
            transform.rotation = snap ? target : Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        /// <summary>
        /// Dive toward <paramref name="point"/> on his line, reaching it after
        /// <paramref name="seconds"/>. <paramref name="reach"/> is how much of the way
        /// he gets: 1 for a save, less when he is beaten.
        /// </summary>
        public void Dive(Vector3 point, float reach, float delay, float seconds)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(DiveRoutine(point, reach, delay, seconds));
        }

        IEnumerator DiveRoutine(Vector3 point, float reach, float delay, float seconds)
        {
            yield return new WaitForSeconds(delay);

            // Square up to the pitch before he goes.
            transform.rotation = Quaternion.LookRotation(Vector3.back);
            float dx = point.x - home.x;
            if (animator != null)
            {
                // Facing the camera, his left is the camera's right (+x).
                animator.SetBool(MirrorParam, dx < 0f);
                if (Mathf.Abs(dx) > 0.6f) animator.SetTrigger(DiveTrigger);
            }

            // A high ball lifts him off the ground; the clip dives along the grass.
            float lift = Mathf.Clamp(point.y - 1.0f, 0f, 1.2f) * reach;
            var target = new Vector3(home.x + dx * reach, home.y + lift, home.z);
            Vector3 start = transform.position;
            float duration = Mathf.Max(0.15f, seconds);
            for (float t = 0f; t < 1f;)
            {
                t += Time.deltaTime / duration;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                var p = Vector3.Lerp(start, target, eased);
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
        }
    }
}
