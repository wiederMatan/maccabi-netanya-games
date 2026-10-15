using UnityEngine;

namespace Headers
{
    /// <summary>
    /// One player on the pitch: stands, jogs to a spot, and jumps for the ball.
    /// The rig only has idle, run, kick and dive clips, so the jump and the header
    /// are posed by hand on top of the idle clip each frame: the body is lifted
    /// along the jump, the arms swing up and out for balance, the back arches as
    /// he rises and snaps forward when the header is struck.
    /// </summary>
    public class Footballer : MonoBehaviour
    {
        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int RunState = Animator.StringToHash("Run");
        static readonly int KickState = Animator.StringToHash("Kick");

        const float ArmLift = 72f;
        const float ElbowBend = 35f;
        const float Arch = -16f;
        const float Nod = 30f;
        const float NodSeconds = 0.32f;

        Animator animator;
        Transform spine, chest, neck, head, leftArm, rightArm, leftForearm, rightForearm;

        Vector3 runFrom, runTo;
        float runStarted, runSeconds;
        bool running;
        Quaternion facing = Quaternion.identity;

        float jumpStarted = -10f;
        float jumpHeight;
        float jumpSeconds = Field.JumpSeconds;
        float nodAt = -10f;
        bool celebrating;
        float celebrateStarted;

        public bool InAir => Time.time - jumpStarted < jumpSeconds;

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        }

        /// <summary>Stand still at <paramref name="spot"/>, facing <paramref name="lookAt"/>.</summary>
        public void Stand(Vector3 spot, Vector3 lookAt)
        {
            running = false;
            celebrating = false;
            jumpStarted = -10f;
            nodAt = -10f;
            transform.position = new Vector3(spot.x, 0f, spot.z);
            Face(lookAt);
            transform.rotation = facing;
            Play(IdleState, 0f);
        }

        /// <summary>Jog to <paramref name="spot"/> over <paramref name="seconds"/>, then stand facing <paramref name="lookAt"/>.</summary>
        public void RunTo(Vector3 spot, float seconds, Vector3 lookAt)
        {
            runFrom = transform.position;
            runTo = new Vector3(spot.x, 0f, spot.z);
            runStarted = Time.time;
            runSeconds = Mathf.Max(0.05f, seconds);
            running = (runTo - runFrom).sqrMagnitude > 0.01f;
            Face(lookAt);
            if (running) Play(RunState, 0.12f);
        }

        /// <summary>The corner taker's strike.</summary>
        public void Kick() => Play(KickState, 0.08f);

        /// <summary>Take off now; the top of the jump comes half a jump later.</summary>
        public void Jump(float height = Field.JumpHeight)
        {
            if (InAir) return;
            running = false;
            jumpStarted = Time.time;
            jumpHeight = height;
            jumpSeconds = Field.JumpSeconds;
            Play(IdleState, 0.1f);
        }

        /// <summary>The header itself: the head and shoulders snap forward through the ball.</summary>
        public void Header() => nodAt = Time.time;

        /// <summary>A goal: a big leap with both arms up, landing back where he was.</summary>
        public void Celebrate()
        {
            celebrating = true;
            celebrateStarted = Time.time + 0.25f;
        }

        void Face(Vector3 lookAt)
        {
            var dir = lookAt - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(dir);
        }

        void Play(int state, float blend)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!animator.HasState(0, state)) return;
            if (blend <= 0f) animator.Play(state, 0, 0f);
            else animator.CrossFadeInFixedTime(state, blend);
        }

        void Update()
        {
            var p = transform.position;
            if (running)
            {
                float u = Mathf.Clamp01((Time.time - runStarted) / runSeconds);
                var flat = Vector3.Lerp(runFrom, runTo, u);
                p = new Vector3(flat.x, p.y, flat.z);
                var dir = runTo - runFrom;
                dir.y = 0f;
                // Turn toward where he is running, then square up to the goal as he arrives.
                var heading = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : facing;
                transform.rotation = Quaternion.Slerp(heading, facing, Mathf.SmoothStep(0.5f, 1f, u));
                if (u >= 1f)
                {
                    running = false;
                    Play(IdleState, 0.15f);
                }
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }

            float since = Time.time - jumpStarted;
            float lift = 0f;
            if (since >= 0f && since < jumpSeconds)
            {
                float u = since / jumpSeconds;
                lift = 4f * jumpHeight * u * (1f - u);
            }
            if (celebrating)
            {
                float c = (Time.time - celebrateStarted) / 0.7f;
                if (c >= 1f) celebrating = false;
                else if (c > 0f) lift = Mathf.Max(lift, 4f * 0.45f * c * (1f - c));
            }
            p.y = lift;
            transform.position = p;
        }

        void LateUpdate()
        {
            if (spine == null) return;

            float since = Time.time - jumpStarted;
            float jump = since >= 0f && since < jumpSeconds ? Mathf.Sin(Mathf.PI * since / jumpSeconds) : 0f;
            float party = 0f;
            if (celebrating)
            {
                float c = Time.time - celebrateStarted;
                party = Mathf.Clamp01(c / 0.15f) * Mathf.Clamp01((1.6f - c) / 0.3f);
            }

            float arms = Mathf.Max(jump, party);
            if (arms <= 0.001f && Time.time - nodAt > NodSeconds) return;

            var right = transform.right;
            var forward = transform.forward;

            // Arch back on the way up; once the header is struck, snap forward and ease back.
            float nod = 0f;
            float sinceNod = Time.time - nodAt;
            if (sinceNod >= 0f && sinceNod < NodSeconds)
            {
                float k = sinceNod / NodSeconds;
                nod = Nod * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.6f)) * (1f - k * 0.4f);
            }
            float bend = Arch * jump * (sinceNod >= 0f && sinceNod < NodSeconds ? 0f : 1f) + nod;
            Rotate(spine, right, bend * 0.35f);
            Rotate(chest, right, bend * 0.35f);
            Rotate(neck, right, bend * 0.3f);

            // Arms out and up: the left arm swings out to his left, the right to his
            // right. A positive turn about his forward axis carries "down" to his right.
            float lift = (celebrating ? 150f : ArmLift) * arms;
            Rotate(leftArm, forward, -lift);
            Rotate(rightArm, forward, lift);
            Rotate(leftForearm, forward, -ElbowBend * arms);
            Rotate(rightForearm, forward, ElbowBend * arms);
        }

        static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }
}
