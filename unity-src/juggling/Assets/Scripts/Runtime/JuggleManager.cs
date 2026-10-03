using System.Runtime.InteropServices;
using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// The game loop. A round starts with the ball dropping from the top; every tap
    /// on the ball kicks it back up, and where the tap lands across the ball decides
    /// which way it drifts. The round ends when the ball reaches the grass.
    /// Everything else in the scene reacts to this class.
    /// </summary>
    public class JuggleManager : MonoBehaviour
    {
        enum State { Menu, Ready, Playing, Dropped }

        // The ball hangs at the top for this long before it starts to fall, so
        // there is time to find it after pressing Kick Off.
        const float ReadySeconds = 0.9f;
        const float DroppedSeconds = 1.6f;
        // Two touches closer together than this are one touch (a double-fired
        // tap, or a mouse event the browser made up from a touch).
        const float KickCooldown = 0.18f;
        // A tap is never smaller than this, as a fraction of the screen's short
        // side, however small the ball is drawn.
        const float MinHitFraction = 0.075f;
        // Space only kicks a ball on its way down into the bottom of the screen,
        // the way a foot would - otherwise holding it down would juggle forever.
        const float KeyReach = 2.3f;
        // Random wobble on every kick, as a fraction of the drift, so the ball
        // never settles into one spot.
        const float Jitter = 0.2f;

        [SerializeField] JuggleBall ball;
        [SerializeField] HudController hud;
        [SerializeField] JuggleAudio audio_;
        [SerializeField] CameraFramer framer;
        [SerializeField] PlayerCheer cheer;
        [SerializeField] int tierIndex;

        State state = State.Menu;
        int score;
        int touches;
        int combo;
        int[] best;
        float lastKick = -10f;
        float lastTouchInput = -10f;
        float lastKeyHint = -10f;
        float stateTimer;
        float inputBlockedUntil;
        bool started;
        Camera cam;
        int lastWidth;
        int lastHeight;

        Tier Tier => Tiers.All[tierIndex];

        public void Bind(JuggleBall juggleBall, HudController hudController, JuggleAudio juggleAudio,
            CameraFramer cameraFramer, PlayerCheer playerCheer)
        {
            ball = juggleBall;
            hud = hudController;
            audio_ = juggleAudio;
            framer = cameraFramer;
            cheer = playerCheer;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Publishes the ball's on-screen position to the page as window.__juggling,
        // so the automated browser checks can find and tap it.
        [DllImport("__Internal")]
        static extern void JugglingReport(float x, float y, float hitRadius, int state, int score, int combo);
#else
        static void JugglingReport(float x, float y, float hitRadius, int state, int score, int combo) { }
#endif

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<JuggleAudio>();
            if (framer != null) cam = framer.GetComponent<Camera>();
            if (cam == null) cam = Camera.main;

            best = new int[Tiers.All.Length];
            for (int i = 0; i < best.Length; i++)
                best[i] = PlayerPrefs.GetInt(BestKey(i), 0);
            tierIndex = Mathf.Clamp(PlayerPrefs.GetInt("juggling.tier", tierIndex), 0, Tiers.All.Length - 1);

            if (hud != null)
            {
                hud.StartButton?.onClick.AddListener(OnStartPressed);

                var tierButtons = hud.DifficultyButtons;
                if (tierButtons != null)
                {
                    for (int i = 0; i < tierButtons.Length && i < Tiers.All.Length; i++)
                    {
                        int index = i;
                        tierButtons[i]?.onClick.AddListener(() => SelectDifficulty(index));
                    }
                }

                hud.SetScore(0);
                hud.SetCombo(0);
                hud.SetHint("");
                hud.ShowOverlay("JUGGLING", "", "Kick Off");
            }

            SelectDifficulty(tierIndex);
            RestBall();
        }

        static string BestKey(int index) => "juggling.best." + Tiers.All[index].Name;

        string Intro =>
            "Tap the ball to kick it up - don't let it touch the grass!\n" +
            "Tap its left side to send it right, its right side to send it left.";

        /// <summary>Overlay difficulty picker. Takes effect from the next round.</summary>
        void SelectDifficulty(int index)
        {
            if (index < 0 || index >= Tiers.All.Length) return;

            tierIndex = index;
            PlayerPrefs.SetInt("juggling.tier", index);
            hud?.HighlightDifficulty(index);
            hud?.SetBest(best[index]);

            if (state == State.Menu)
            {
                if (!started) hud?.SetOverlayBody(Intro);
                ball.SetRadius(Tier.BallRadius);
                RestBall();
            }
        }

        void OnStartPressed()
        {
            started = true;
            hud?.HideOverlay();

            score = 0;
            touches = 0;
            combo = 0;
            hud?.SetScore(0);
            hud?.SetCombo(0);
            hud?.SetBest(best[tierIndex]);
            hud?.SetHint(Input.touchSupported ? "Tap the ball!" : "Click the ball - or press Space!");

            ball.SetRadius(Tier.BallRadius);
            ball.Position = new Vector2(0f, Tier.ApexMax - 0.2f);
            ball.Velocity = Vector2.zero;
            ball.Spin(0f);
            ball.Sync();

            state = State.Ready;
            stateTimer = ReadySeconds;
            // The tap that pressed the button must not also count as a kick.
            inputBlockedUntil = Time.unscaledTime + 0.25f;
            audio_?.PlayWhistle();
        }

        void Update()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                framer?.Apply();
                if (framer != null) cheer?.Place(framer.PlayHalfWidth, framer.VisibleHalfWidth);
            }

            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);

            switch (state)
            {
                case State.Ready:
                    HandleInput();
                    stateTimer -= dt;
                    if (state == State.Ready && stateTimer <= 0f) state = State.Playing;
                    break;

                case State.Playing:
                    HandleInput();
                    if (state == State.Playing) Fly(dt);
                    break;

                case State.Dropped:
                    Settle(dt);
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EndRound();
                    break;
            }

            ball.Sync();
            Report();
        }

        void Fly(float dt)
        {
            float halfWidth = framer != null ? framer.PlayHalfWidth : 3f;
            bool landed = BallPhysics.Step(ref ball.Position, ref ball.Velocity, dt,
                Tier.GravityAt(touches), ball.Radius, halfWidth);
            if (landed) Drop();
        }

        // ---------------------------------------------------------------- input

        void HandleInput()
        {
            if (Time.unscaledTime < inputBlockedUntil) return;

            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began) continue;
                lastTouchInput = Time.unscaledTime;
                TryKickAt(touch.position);
            }

            // Unity also reports a touch as a mouse click; only take the mouse when
            // no finger has been down lately.
            if (Input.GetMouseButtonDown(0) && Input.touchCount == 0 &&
                Time.unscaledTime - lastTouchInput > 0.6f)
                TryKickAt(Input.mousePosition);

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.W))
                KeyKick();
        }

        void TryKickAt(Vector2 screen)
        {
            if (cam == null) return;

            ball.ScreenCircle(cam, out var centre, out float radius);
            float hitRadius = Mathf.Max(radius * Tier.HitScale,
                MinHitFraction * Mathf.Min(Screen.width, Screen.height));

            if (Vector2.Distance(screen, centre) > hitRadius) return;

            // Tapping left of centre (screen x below the ball's) pushes the ball right.
            Kick((centre.x - screen.x) / hitRadius);
        }

        void KeyKick()
        {
            bool inReach = state == State.Ready ||
                           (ball.Position.y - ball.Radius < KeyReach && ball.Velocity.y <= 0f);
            if (!inReach)
            {
                if (Time.unscaledTime - lastKeyHint > 1f) hud?.Toast("Wait for it...");
                lastKeyHint = Time.unscaledTime;
                return;
            }

            // A key has no "where on the ball", so steer gently back toward the middle.
            float halfWidth = framer != null ? framer.PlayHalfWidth : 3f;
            float toward = -ball.Position.x / Mathf.Max(0.5f, halfWidth);
            Kick(Mathf.Clamp(toward * 0.6f + Random.Range(-0.3f, 0.3f), -1f, 1f));
        }

        void Kick(float offset)
        {
            if (Time.unscaledTime - lastKick < KickCooldown) return;
            lastKick = Time.unscaledTime;

            if (state == State.Ready)
            {
                state = State.Playing;
            }

            bool clean = ScoreRules.IsClean(offset);
            combo = clean ? combo + 1 : 0;

            int before = score;
            score += ScoreRules.PointsFor(combo);
            touches++;

            var tier = Tier;
            float apex = Random.Range(tier.ApexMin, tier.ApexMax);
            float wobble = Random.Range(-Jitter, Jitter);
            ball.Velocity = BallPhysics.Kick(ball.Position, offset + wobble, apex,
                CameraFramer.Ceiling - ball.Radius, tier.GravityAt(touches), tier.Drift);
            ball.Spin(-ball.Velocity.x * 140f);

            audio_?.PlayTouch(combo, clean);
            hud?.SetScore(score);
            hud?.SetCombo(combo);
            hud?.SetHint("");

            if (combo == ScoreRules.DoublePointsCombo) hud?.Toast("Double points!");
            else if (clean) hud?.Toast(combo >= 3 ? "Perfect!" : "Nice!");

            int milestone = ScoreRules.MilestoneCrossed(before, score);
            if (milestone > 0)
            {
                hud?.Banner($"{milestone} POINTS!");
                audio_?.PlayCheer();
                cheer?.Celebrate(milestone >= 25);
            }
        }

        // ---------------------------------------------------------------- round end

        void Drop()
        {
            state = State.Dropped;
            stateTimer = DroppedSeconds;
            combo = 0;
            hud?.SetCombo(0);
            hud?.SetHint("");
            audio_?.PlayDrop();
            Bounce();
        }

        /// <summary>A dropped ball bounces a couple of times on the grass and rolls to a stop.</summary>
        void Settle(float dt)
        {
            float halfWidth = framer != null ? framer.PlayHalfWidth : 3f;
            ball.Velocity.x *= 1f - 1.2f * dt;
            if (BallPhysics.Step(ref ball.Position, ref ball.Velocity, dt, 9.8f, ball.Radius, halfWidth))
                Bounce();
        }

        void Bounce()
        {
            ball.Position.y = ball.Radius;
            ball.Velocity.y = Mathf.Abs(ball.Velocity.y) * 0.4f;
            if (ball.Velocity.y < 0.4f) ball.Velocity.y = 0f;
            ball.Spin(-ball.Velocity.x * 140f);
        }

        void EndRound()
        {
            state = State.Menu;

            bool newBest = score > best[tierIndex];
            if (newBest)
            {
                best[tierIndex] = score;
                PlayerPrefs.SetInt(BestKey(tierIndex), score);
                PlayerPrefs.Save();
            }

            audio_?.PlayRoundOver(newBest && score > 0);
            hud?.SetBest(best[tierIndex]);

            string title = newBest && score > 0 ? "NEW BEST!" : score >= 10 ? "GREAT JUGGLING!" : "BALL DOWN!";
            string touchWord = touches == 1 ? "touch" : "touches";
            hud?.ShowOverlay(title,
                $"You scored {score} points with {touches} {touchWord}.\n" +
                $"Best on {Tier.Name}: {best[tierIndex]}. Pick a level and go again!",
                "Play again");
        }

        void RestBall()
        {
            if (ball == null) return;
            ball.Position = new Vector2(0f, ball.Radius);
            ball.Velocity = Vector2.zero;
            ball.Sync();
        }

        /// <summary>Tell the page where the ball is, in CSS pixels from the canvas's top-left.</summary>
        void Report()
        {
            if (cam == null) return;
            ball.ScreenCircle(cam, out var centre, out float radius);
            float hitRadius = Mathf.Max(radius * Tier.HitScale,
                MinHitFraction * Mathf.Min(Screen.width, Screen.height));
            JugglingReport(centre.x, centre.y, hitRadius, (int)state, score, combo);
        }
    }
}
