using UnityEngine;

namespace Dribble
{
    public enum ItemKind { Defender, Cone, Star }

    /// <summary>
    /// Anything that rides the pitch toward the runner: a defender, a cone or a
    /// star. Items are pooled - the course hands them out and takes them back - so
    /// nothing is created or destroyed during a run.
    /// </summary>
    public class PitchItem : MonoBehaviour
    {
        [SerializeField] ItemKind kind;

        const float StarSpin = 160f;
        const float PopSeconds = 0.28f;
        const float KnockSeconds = 0.7f;

        Vector3 restScale;
        Quaternion restRotation;
        float effectTime;
        Vector3 knockVelocity;
        Vector3 knockSpin;

        public ItemKind Kind => kind;
        public int Lane { get; private set; }
        /// <summary>Already hit or collected this pass, so it cannot count twice.</summary>
        public bool Spent { get; private set; }
        bool popping;
        bool knocked;

        public void Configure(ItemKind itemKind)
        {
            kind = itemKind;
        }

        bool hasRest;

        // Captured lazily: a pooled item may be placed before it has ever been
        // active, and so before its Awake would have run.
        void CaptureRest()
        {
            if (hasRest) return;
            hasRest = true;
            restScale = transform.localScale;
            restRotation = transform.localRotation;
        }

        public void Place(int lane, Vector3 position)
        {
            CaptureRest();
            Lane = lane;
            Spent = false;
            popping = false;
            knocked = false;
            transform.localScale = restScale;
            transform.localRotation = restRotation;
            transform.localPosition = position;
            gameObject.SetActive(true);
        }

        /// <summary>A star flies up and shrinks away.</summary>
        public void Collect()
        {
            Spent = true;
            popping = true;
            effectTime = 0f;
        }

        /// <summary>A cone goes flying off the pitch.</summary>
        public void KnockAway(float side)
        {
            Spent = true;
            knocked = true;
            effectTime = 0f;
            knockVelocity = new Vector3(side * 3.5f, 4.2f, 2.5f);
            knockSpin = new Vector3(Random.Range(380f, 620f), 0f, -side * 540f);
        }

        /// <summary>A defender who has made the tackle stops counting as a blocker.</summary>
        public void MarkSpent() => Spent = true;

        void Update()
        {
            if (kind == ItemKind.Star && !popping)
                transform.Rotate(0f, StarSpin * Time.deltaTime, 0f, Space.World);

            if (popping)
            {
                effectTime += Time.deltaTime;
                float t = Mathf.Clamp01(effectTime / PopSeconds);
                transform.localScale = restScale * Mathf.Lerp(1.5f, 0f, t * t);
                transform.localPosition += Vector3.up * (3.5f * Time.deltaTime);
                transform.Rotate(0f, StarSpin * 4f * Time.deltaTime, 0f, Space.World);
                if (t >= 1f) gameObject.SetActive(false);
            }

            if (knocked)
            {
                effectTime += Time.deltaTime;
                knockVelocity += Physics.gravity * Time.deltaTime;
                transform.localPosition += knockVelocity * Time.deltaTime;
                transform.Rotate(knockSpin * Time.deltaTime, Space.Self);
                if (effectTime >= KnockSeconds) gameObject.SetActive(false);
            }
        }
    }
}
