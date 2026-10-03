using System.Collections;
using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// The player's keeper. A dive plays Anim_Dive (mirrored for the right-hand
    /// side) while this carries him across the goal toward the chosen spot - and up
    /// into the air for a top corner. The middle is a hop on the spot.
    /// Whether the dive saves the shot is decided by <see cref="KeeperGame"/>.
    /// </summary>
    public class GoalkeeperController : MonoBehaviour
    {
        [SerializeField] float travelSeconds = 0.42f;
        [SerializeField] float recoverSeconds = 0.4f;
        // The dive clip stretches the arms well past the body, so the body only has
        // to land short of the spot for the gloves to reach it.
        [SerializeField] float reachShort = 0.95f;
        [SerializeField] float highLift = 1.15f;
        [SerializeField] float hopHeight = 0.28f;
        [SerializeField] float setStep = 0.35f;

        static readonly int DiveLeft = Animator.StringToHash("Dive");
        static readonly int DiveRight = Animator.StringToHash("DiveRight");

        Vector3 home;
        Quaternion homeRotation;
        Coroutine routine;
        Coroutine hold;
        Animator animator;

        // The point in Anim_Dive where he is full length on the grass. The clip
        // goes on to get him up again, so it is paused here until the shot is over.
        const float DownTime = 0.45f;

        public bool Diving { get; private set; }

        void Awake()
        {
            home = transform.position;
            homeRotation = transform.rotation;
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Throw himself at one of the goal's spots.</summary>
        public void Dive(int spot)
        {
            if (routine != null) StopCoroutine(routine);
            Diving = true;

            int side = Goal.Side(spot);
            if (side == 0)
            {
                routine = StartCoroutine(Hop());
                return;
            }

            if (animator != null)
            {
                animator.speed = 1f;
                animator.SetTrigger(side < 0 ? DiveLeft : DiveRight);
                hold = StartCoroutine(HoldWhenDown(side < 0 ? DiveLeft : DiveRight));
            }

            Vector3 target = Goal.Spots[spot];
            float x = target.x - side * reachShort;
            routine = StartCoroutine(DiveRoutine(new Vector3(x, home.y, home.z), Goal.IsHigh(spot)));
        }

        /// <summary>
        /// The player has guessed before the kick: shuffle a step toward that side and
        /// wait, so the guess shows without the keeper leaving his line too soon.
        /// </summary>
        public void SetFor(int spot)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Shuffle(home + Vector3.right * (Goal.Side(spot) * setStep)));
        }

        IEnumerator Shuffle(Vector3 target)
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
            {
                transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            transform.position = target;
            routine = null;
        }

        /// <summary>Let him get back up off the grass after the shot has been decided.</summary>
        public void GetUp()
        {
            if (hold != null) StopCoroutine(hold);
            hold = null;
            if (transform.position.y > home.y + 0.01f)
            {
                if (routine != null) StopCoroutine(routine);
                routine = StartCoroutine(Fall());
                return;
            }
            if (animator != null) animator.speed = 1f;
        }

        IEnumerator Fall()
        {
            Vector3 start = transform.position;
            Vector3 ground = new Vector3(start.x, home.y, start.z);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                transform.position = Vector3.Lerp(start, ground, t * t);
                yield return null;
            }
            transform.position = ground;
            routine = null;
            if (animator != null) animator.speed = 1f;
        }

        public void ResetStance()
        {
            if (routine != null) StopCoroutine(routine);
            if (hold != null) StopCoroutine(hold);
            hold = null;
            Diving = false;
            if (animator != null)
            {
                animator.speed = 1f;
                animator.ResetTrigger(DiveLeft);
                animator.ResetTrigger(DiveRight);
                animator.CrossFadeInFixedTime("Idle", 0.25f);
            }
            routine = StartCoroutine(RecoverRoutine());
        }

        IEnumerator DiveRoutine(Vector3 target, bool high)
        {
            // The clip handles the body; this only carries him across to the spot,
            // and for a top corner lifts him into the air. He stays up at full stretch
            // until the shot is decided - GetUp lets him fall.
            Vector3 start = transform.position;
            if (high) target.y = home.y + highLift;

            for (float t = 0f; t < travelSeconds; t += Time.deltaTime)
            {
                float across = 1f - Mathf.Pow(1f - t / travelSeconds, 3f);
                transform.position = Vector3.Lerp(start, target, across);
                yield return null;
            }

            transform.position = target;
            routine = null;
        }

        IEnumerator HoldWhenDown(int state)
        {
            for (float waited = 0f; waited < 2f; waited += Time.deltaTime)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (!animator.IsInTransition(0) && info.shortNameHash == state && info.normalizedTime >= DownTime)
                {
                    animator.speed = 0f;
                    break;
                }
                yield return null;
            }
            hold = null;
        }

        IEnumerator Hop()
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, home, t / 0.2f) + Vector3.up * (hopHeight * Mathf.Sin(Mathf.PI * t / 0.5f));
                yield return null;
            }
            transform.position = home;
            routine = null;
        }

        IEnumerator RecoverRoutine()
        {
            Vector3 start = transform.position;
            Quaternion startRotation = transform.rotation;

            for (float t = 0f; t < 1f; t += Time.deltaTime / recoverSeconds)
            {
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
