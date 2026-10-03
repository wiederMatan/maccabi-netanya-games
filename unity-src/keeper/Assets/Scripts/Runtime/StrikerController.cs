using System.Collections;
using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// The opposition's penalty taker. He waits on his mark behind the ball, then
    /// runs in, turning his body toward the spot he has picked - a tell sharp-eyed
    /// players can learn to read - and strikes.
    /// </summary>
    public class StrikerController : MonoBehaviour
    {
        // Where the boot meets the ball in Anim_Kick, as a fraction of the clip:
        // the right foot is moving fastest, at ground level, at 0.75s of 1.5s.
        const float KickContact = 0.5f;
        // Fallback if the animator never reports the kick (no striker model).
        const float KickContactTimeout = 1.2f;
        const float MarkDistance = 3.2f;

        static readonly int RunTrigger = Animator.StringToHash("Run");
        static readonly int KickTrigger = Animator.StringToHash("Kick");
        static readonly int KickState = Animator.StringToHash("Kick");

        Animator animator;
        Vector3 ball;

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Stand on the mark, straight behind the ball, facing the goal.</summary>
        public void ToMark(Vector3 ballSpot)
        {
            ball = ballSpot;
            transform.position = ballSpot + new Vector3(0.35f, 0f, MarkDistance);
            transform.rotation = Quaternion.LookRotation(Vector3.back);
            if (animator != null) animator.CrossFadeInFixedTime("Idle", 0.15f);
        }

        /// <summary>
        /// Run in over <paramref name="seconds"/> and swing at the ball; finishes the
        /// moment the boot reaches it, so the caller can release the shot then.
        /// </summary>
        public IEnumerator RunUpAndKick(Vector3 target, float seconds)
        {
            Vector3 start = transform.position;
            Quaternion startRotation = transform.rotation;

            // Face where the shot is going; a right-footer plants just left of the ball.
            Vector3 aim = new Vector3(target.x - ball.x, 0f, target.z - ball.z).normalized;
            Quaternion facing = Quaternion.LookRotation(aim);
            Vector3 plant = ball - aim * 0.62f - (facing * Vector3.right) * 0.42f;

            animator?.SetTrigger(RunTrigger);

            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                transform.position = Vector3.Lerp(start, plant, eased);
                // Square up to the target early, so the body shape is readable.
                transform.rotation = Quaternion.Slerp(startRotation, facing, Mathf.Clamp01(t * 1.6f));
                yield return null;
            }

            transform.position = plant;
            transform.rotation = facing;
            animator?.SetTrigger(KickTrigger);

            yield return WaitForKickContact();
        }

        IEnumerator WaitForKickContact()
        {
            float waited = 0f;
            while (waited < KickContactTimeout)
            {
                if (animator != null && !animator.IsInTransition(0))
                {
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    if (state.shortNameHash == KickState && state.normalizedTime >= KickContact) yield break;
                }
                else if (animator != null)
                {
                    // Already blending into the kick: track the clip we are heading to.
                    var next = animator.GetNextAnimatorStateInfo(0);
                    if (next.shortNameHash == KickState && next.normalizedTime >= KickContact) yield break;
                }
                waited += Time.deltaTime;
                yield return null;
            }
        }
    }
}
