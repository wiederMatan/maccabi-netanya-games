using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// The Maccabi Netanya player and the ball at his feet. He runs on the spot at
    /// z = 0 (the pitch does the moving), slides between the three lanes, and keeps
    /// the ball just ahead of him with a touch every couple of strides. When a
    /// defender wins the ball he goes down and the ball rolls loose.
    /// </summary>
    public class Runner : MonoBehaviour
    {
        const float LaneChangeSeconds = 0.17f;
        const float MaxLean = 12f;
        const float BallRadius = 0.13f;
        // Ball touches per second at the reference speed; quicker running, quicker touches.
        const float TouchesPerSecond = 1.7f;
        const float ReferenceSpeed = 6f;

        // States are cross-faded by name rather than through triggers: a run
        // starts in the same frame it is reset, and a stale "Idle" trigger left
        // from the reset used to pull him straight back out of the run.
        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int RunState = Animator.StringToHash("Run");
        static readonly int FallState = Animator.StringToHash("Fall");
        // The ball is dribbled just ahead of his right foot, out to the side far
        // enough that the chase camera sees it past his legs.
        const float BallNear = 0.55f;
        const float BallReach = 0.45f;
        const float BallSide = 0.3f;

        [SerializeField] Transform body;
        [SerializeField] Rigidbody ball;

        Animator animator;
        int lane = 1;
        float x;
        float xVelocity;
        float touchPhase;
        float ballX;
        float ballXVelocity;
        bool running;
        bool down;

        /// <summary>Raised on each touch of the ball, for the sound.</summary>
        public event System.Action Touched;

        public int Lane => lane;
        /// <summary>Where the runner actually is across the pitch, mid lane change included.</summary>
        public float X => x;
        public Transform Ball => ball != null ? ball.transform : null;
        public Transform Body => body;

        public void Bind(Transform runnerBody, Rigidbody ballBody)
        {
            body = runnerBody;
            ball = ballBody;
        }

        void Awake()
        {
            if (body != null) animator = body.GetComponentInChildren<Animator>();
            ResetToStart();
        }

        /// <summary>Back to the middle lane, standing with the ball at his feet.</summary>
        public void ResetToStart()
        {
            lane = 1;
            x = Course.LaneX(lane);
            xVelocity = 0f;
            ballX = x + BallSide;
            ballXVelocity = 0f;
            touchPhase = 0f;
            running = false;
            down = false;

            if (body != null)
            {
                body.localPosition = new Vector3(x, 0f, 0f);
                body.localRotation = Quaternion.identity;
            }

            if (ball != null)
            {
                if (!ball.isKinematic)
                {
                    ball.linearVelocity = Vector3.zero;
                    ball.angularVelocity = Vector3.zero;
                }
                ball.isKinematic = true;
                ball.transform.localPosition = new Vector3(x + BallSide, BallRadius, BallNear);
            }

            if (animator != null)
            {
                animator.speed = 1f;
                animator.Play(IdleState, 0, 0f);
            }
        }

        public void StartRunning()
        {
            running = true;
            animator?.CrossFadeInFixedTime(RunState, 0.15f);
        }

        /// <summary>Move one lane left (-1) or right (+1). Returns false at the edge.</summary>
        public bool Shift(int direction)
        {
            if (!running || down) return false;
            int next = Mathf.Clamp(lane + direction, 0, CourseGenerator.LaneCount - 1);
            if (next == lane) return false;
            lane = next;
            return true;
        }

        /// <summary>
        /// Tackled. He tumbles and the ball squirts away off the defender's boot,
        /// out to the side the runner was not heading.
        /// </summary>
        public void Fall()
        {
            if (down) return;
            down = true;
            running = false;

            if (animator != null)
            {
                animator.speed = 1f;
                animator.CrossFadeInFixedTime(FallState, 0.06f);
            }

            if (ball != null)
            {
                float side = x > 0.1f ? -1f : x < -0.1f ? 1f : (Random.value < 0.5f ? -1f : 1f);
                ball.isKinematic = false;
                ball.linearVelocity = new Vector3(side * 2.6f, 2.2f, -1.2f);
                ball.angularVelocity = new Vector3(-8f, 0f, side * 6f);
            }
        }

        /// <summary>Advance the runner and the dribble by one frame at the given running speed.</summary>
        public void Tick(float speed, float dt)
        {
            if (down) return;

            float targetX = Course.LaneX(lane);
            float before = x;
            x = Mathf.SmoothDamp(x, targetX, ref xVelocity, LaneChangeSeconds * 0.45f, 30f, dt);

            if (body != null)
            {
                float lean = Mathf.Clamp(-xVelocity * 2.2f, -MaxLean, MaxLean);
                float turn = Mathf.Clamp(xVelocity * 3.5f, -18f, 18f);
                body.localPosition = new Vector3(x, 0f, 0f);
                body.localRotation = Quaternion.Euler(0f, turn, lean);
            }

            if (animator != null && running)
                animator.speed = Mathf.Clamp(speed / ReferenceSpeed, 0.85f, 1.5f);

            if (ball == null || !running) return;

            // A touch pushes the ball out ahead; then he runs back onto it.
            float touchRate = TouchesPerSecond * Mathf.Clamp(speed / ReferenceSpeed, 0.8f, 1.6f);
            touchPhase += touchRate * dt;
            if (touchPhase >= 1f)
            {
                touchPhase -= 1f;
                Touched?.Invoke();
            }

            float reach = Mathf.Sin(Mathf.PI * Mathf.Pow(touchPhase, 0.55f));
            float ballZ = BallNear + BallReach * reach;
            ballX = Mathf.SmoothDamp(ballX, x + BallSide, ref ballXVelocity, 0.07f, 40f, dt);

            var t = ball.transform;
            t.localPosition = new Vector3(ballX, BallRadius, ballZ);
            // Roll at the speed the grass passes under it, plus a little sideways roll.
            float sideways = (x - before) / Mathf.Max(dt, 0.0001f);
            t.Rotate(new Vector3(speed, 0f, -sideways) * (dt / BallRadius * Mathf.Rad2Deg), Space.World);
        }
    }
}
