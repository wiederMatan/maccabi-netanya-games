using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// The game loop. A round is five free kicks from around the edge of the box.
    /// For each one the player drags from the ball toward the goal: the direction
    /// aims, the length is the power, and a curve in the swipe bends the ball. The
    /// shot is judged the instant it is struck and everything else plays it out.
    /// </summary>
    public class FreeKickManager : MonoBehaviour
    {
        public const int KicksPerRound = 5;
        public const int GoalPoints = 10;
        public const int RingPoints = 20;

        const float RunUpSeconds = 0.55f;
        // Where the boot meets the ball in Anim_Kick, as a fraction of the clip:
        // the right foot is moving fastest, at ground level, at 0.75s of 1.5s.
        const float KickContact = 0.5f;
        // Fallback if the animator never reports the kick (no striker model).
        const float KickContactTimeout = 1.2f;
        static readonly int KickState = Animator.StringToHash("Kick");
        static readonly int KickTrigger = Animator.StringToHash("Kick");
        static readonly int RunTrigger = Animator.StringToHash("Run");

        // Swipe tuning, as fractions of the on-screen distance from the ball to the goal.
        const float MinSwipe = 0.035f;          // of the screen height - shorter is a tap
        const float PowerFloor = 0.12f;
        const float PowerSpan = 0.75f;
        const float CurveDeadZone = 0.04f;
        const float CurveGain = 3.2f;

        enum Phase { Menu, Aiming, Shooting, Done }

        [SerializeField] BallController ball;
        [SerializeField] GoalkeeperController keeper;
        [SerializeField] WallController wall;
        [SerializeField] TargetRing[] rings;
        [SerializeField] AimGuide guide;
        [SerializeField] HudController hud;
        [SerializeField] Transform striker;
        [SerializeField] CameraFramer framer;
        [SerializeField] MatchAudio audio_;
        [SerializeField] Level level = Level.Easy;

        Phase phase = Phase.Menu;
        LevelSettings settings;
        int kickIndex;
        int goals;
        int score;
        bool roundStarted;
        SquadMember taker;

        Vector3 spot;
        Vector3 takerSpot;
        Defence defence;
        float wind;
        Animator strikerAnimator;

        // Pointer state for the swipe.
        bool dragging;
        readonly List<Vector2> swipe = new List<Vector2>();
        float lastTouchTime = -10f;

        // Keyboard aiming, for desktop players who would rather not drag.
        bool keyAiming;
        float keyAim;
        float keyPower = 0.5f;
        float keyCurve;

        string BestKey => $"FreeKick.Best.{level}";

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<MatchAudio>();
            if (striker != null) strikerAnimator = striker.GetComponentInChildren<Animator>();
            if (ball != null)
            {
                ball.Arrived += OnBallArrived;
                ball.Resolved += OnBallResolved;
            }

            settings = LevelSettings.For(level);
            if (hud != null)
            {
                hud.StartButton?.onClick.AddListener(OnStartPressed);
                var buttons = hud.LevelButtons;
                if (buttons != null)
                {
                    for (int i = 0; i < buttons.Length && i < LevelSettings.All.Length; i++)
                    {
                        int index = i;
                        buttons[i]?.onClick.AddListener(() => SelectLevel(index));
                    }
                }
                hud.HighlightLevel((int)level);
                hud.ResetKicks();
                hud.SetGoals(0, 0);
                hud.SetScore(0);
                hud.SetBest(PlayerPrefs.GetInt(BestKey, 0));
                hud.SetLevel(settings.Name);
                hud.SetMessage("Free kick!");
                hud.SetHint("");
                hud.ShowOverlay("FREE KICK",
                    "Drag from the ball toward the goal and let go. A longer drag kicks harder. " +
                    "Curve your swipe to bend the ball round the wall, and hit the yellow rings for bonus points!",
                    "Play");
            }

            taker = Roster.Random();
            SetUpKick();
        }

        // ---------------------------------------------------------------- flow

        void SelectLevel(int index)
        {
            if (index < 0 || index >= LevelSettings.All.Length) return;
            level = (Level)index;
            settings = LevelSettings.For(level);
            hud?.HighlightLevel(index);
            hud?.SetLevel(settings.Name);
            hud?.SetBest(PlayerPrefs.GetInt(BestKey, 0));
            // Show the new wall straight away, behind the menu.
            if (phase != Phase.Shooting) SetUpKick();
        }

        void OnStartPressed()
        {
            roundStarted = true;
            kickIndex = 0;
            goals = 0;
            score = 0;
            taker = Roster.Random();

            hud?.HideOverlay();
            hud?.ResetKicks();
            hud?.SetGoals(0, 0);
            hud?.SetScore(0);
            audio_?.PlayWhistle();
            SetUpKick();
            BeginAiming();
        }

        /// <summary>Place the ball, the wall, the keeper, the taker and the camera for the next kick.</summary>
        void SetUpKick()
        {
            float distance = Random.Range(settings.MinDistance, settings.MaxDistance);
            float side = Random.Range(-settings.MaxSide, settings.MaxSide);
            spot = new Vector3(side, 0f, Pitch.GoalLineZ - distance);

            // The wall covers the near post and the keeper takes the far side, like
            // a real one. From straight in front, either side will do.
            float near = Mathf.Abs(side) > 0.6f ? Mathf.Sign(side) : (Random.value < 0.5f ? -1f : 1f);
            Vector3 wallAim = new Vector3(near * 1.6f, 0f, Pitch.GoalLineZ);
            Vector3 wallSpot = spot + (wallAim - spot).normalized * Pitch.WallDistance;
            float wallTop = Pitch.PlayerHeight * settings.WallScale * 0.98f + settings.WallJump * 0.9f;

            defence = new Defence(wallSpot.x, wallSpot.z, settings.WallCount, wallTop,
                -near * 0.6f, settings.KeeperSkill, settings.RingRadius);

            wind = settings.MaxWind > 0f ? Random.Range(-settings.MaxWind, settings.MaxWind) : 0f;

            ball?.Place(spot);
            wall?.LineUp(defence, settings.WallScale, spot);
            keeper?.TakePosition(defence.KeeperX, spot);
            if (rings != null) foreach (var ring in rings) ring?.Configure(settings.RingRadius);
            guide?.Hide();

            // The taker waits behind and to the left of the ball, a right-footer's run-up.
            Quaternion toGoal = Quaternion.LookRotation(new Vector3(-spot.x, 0f, Pitch.GoalLineZ - spot.z));
            takerSpot = spot + toGoal * new Vector3(-1.5f, 0f, -2.4f);
            if (striker != null)
            {
                striker.position = takerSpot;
                striker.rotation = Quaternion.LookRotation(spot - takerSpot);
            }

            framer?.FrameKick(spot, takerSpot);
        }

        void BeginAiming()
        {
            phase = Phase.Aiming;
            dragging = false;
            keyAiming = false;
            hud?.SetMessage($"Kick {kickIndex + 1} of {KicksPerRound}: {taker.Name} {taker.Shirt}".TrimEnd());
            hud?.SetHint(AimHint());
            StartCoroutine(ReportAim());
        }

        /// <summary>
        /// Logs where the ball and goal are on screen once the camera has settled. The
        /// browser console shows Debug.Log, so automated tests can aim real swipes.
        /// </summary>
        IEnumerator ReportAim()
        {
            yield return null;
            var cam = Camera.main;
            if (cam == null || phase != Phase.Aiming) yield break;
            Vector2 b = cam.WorldToScreenPoint(BallCentre);
            Vector2 g = cam.WorldToScreenPoint(new Vector3(0f, 0f, Pitch.GoalLineZ));
            Vector2 l = cam.WorldToScreenPoint(new Vector3(-Pitch.GoalWidth / 2f, Pitch.GoalHeight, Pitch.GoalLineZ));
            Vector2 r = cam.WorldToScreenPoint(new Vector3(Pitch.GoalWidth / 2f, Pitch.GoalHeight, Pitch.GoalLineZ));
            Debug.Log($"[FreeKick] aim kick={kickIndex + 1} screen={Screen.width}x{Screen.height} " +
                      $"ball={b.x:0},{b.y:0} goal={g.x:0},{g.y:0} topLeft={l.x:0},{l.y:0} topRight={r.x:0},{r.y:0} " +
                      $"wall={defence.WallCentreX:0.0} keeper={defence.KeeperX:0.0} wind={wind:0.0}");
        }

        string AimHint()
        {
            if (Mathf.Abs(wind) < 0.15f) return "Drag from the ball to the goal, then let go. Curve it to bend!";
            string strength = Mathf.Abs(wind) > 0.9f ? "strong" : "light";
            return wind > 0f ? $"Wind  >>  {strength}, blowing right" : $"Wind  <<  {strength}, blowing left";
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (phase != Phase.Aiming) return;

            if (UpdateKeyboard()) return;

            if (!ReadPointer(out bool down, out bool held, out bool up, out Vector2 position))
            {
                // The pointer vanished mid-drag (left the window): treat it as letting go.
                if (dragging) Release();
                return;
            }

            if (down)
            {
                dragging = true;
                keyAiming = false;
                swipe.Clear();
                swipe.Add(position);
                guide?.Hide();
                return;
            }

            if (!dragging) return;

            if (held)
            {
                if ((position - swipe[swipe.Count - 1]).sqrMagnitude > 4f) swipe.Add(position);
                if (TryReadSwipe(out var path)) guide?.Show(path, settings.GuideLength);
                else guide?.Hide();
            }
            else if (up)
            {
                if ((position - swipe[swipe.Count - 1]).sqrMagnitude > 1f) swipe.Add(position);
                Release();
            }
        }

        void Release()
        {
            dragging = false;
            if (TryReadSwipe(out var path)) Shoot(path);
            else
            {
                guide?.Hide();
                hud?.SetHint("Drag a bit further - from the ball toward the goal!");
            }
        }

        /// <summary>Touch first; the mouse only when no finger has been down recently.</summary>
        bool ReadPointer(out bool down, out bool held, out bool up, out Vector2 position)
        {
            down = held = up = false;
            position = default;

            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                lastTouchTime = Time.unscaledTime;
                position = touch.position;
                down = touch.phase == TouchPhase.Began;
                up = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                held = !down && !up;
                return true;
            }

            if (Time.unscaledTime - lastTouchTime < 0.5f) return false;

            position = Input.mousePosition;
            down = Input.GetMouseButtonDown(0);
            up = Input.GetMouseButtonUp(0);
            held = !down && !up && Input.GetMouseButton(0);
            return down || up || held;
        }

        /// <summary>
        /// Turn the swipe so far into a shot. The swipe's direction, laid from the ball
        /// on screen, picks the point on the goal line; its length against the ball-to-
        /// goal distance on screen is the power; how far it bows off a straight line is the bend.
        /// </summary>
        bool TryReadSwipe(out ShotPath path)
        {
            path = default;
            var cam = Camera.main;
            if (cam == null || swipe.Count < 2) return false;

            Vector2 start = swipe[0];
            Vector2 chord = swipe[swipe.Count - 1] - start;
            float length = chord.magnitude;
            if (length < MinSwipe * Screen.height || chord.y < 0.2f * length) return false;

            Vector2 ballOnScreen = cam.WorldToScreenPoint(spot);
            Vector2 goalOnScreen = cam.WorldToScreenPoint(new Vector3(0f, 0f, Pitch.GoalLineZ));
            float toGoal = Mathf.Max(40f, (goalOnScreen - ballOnScreen).magnitude);

            Vector2 direction = chord / length;
            if (!AimAlong(cam, ballOnScreen + direction * toGoal, out float aimX)) return false;

            float power = Mathf.Clamp01((length / toGoal - PowerFloor) / PowerSpan);

            // Signed bow: positive when the swipe bulges to the right of its chord.
            Vector2 right = new Vector2(direction.y, -direction.x);
            float bow = 0f;
            foreach (var point in swipe)
            {
                float d = Vector2.Dot(point - start, right);
                if (Mathf.Abs(d) > Mathf.Abs(bow)) bow = d;
            }
            float bowRatio = bow / length;
            float curve = Mathf.Sign(bowRatio) * Mathf.Clamp01((Mathf.Abs(bowRatio) - CurveDeadZone) * CurveGain);

            path = ShotPath.FromSwipe(BallCentre, aimX, power, curve, settings, wind);
            return true;
        }

        /// <summary>Where a screen point lands on the goal line, sideways.</summary>
        static bool AimAlong(Camera cam, Vector2 screenPoint, out float x)
        {
            x = 0f;
            var ray = cam.ScreenPointToRay(screenPoint);
            if (ray.direction.z <= 0.01f) return false;
            float t = (Pitch.GoalLineZ - ray.origin.z) / ray.direction.z;
            x = Mathf.Clamp(ray.origin.x + ray.direction.x * t, -Pitch.GoalWidth * 1.5f, Pitch.GoalWidth * 1.5f);
            return true;
        }

        Vector3 BallCentre => new Vector3(spot.x, Pitch.BallRadius, spot.z);

        /// <summary>Arrows aim and set power, A/D bend it, Space shoots.</summary>
        bool UpdateKeyboard()
        {
            float steer = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float lift = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            float bend = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            bool fire = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);

            if (!keyAiming && steer == 0f && lift == 0f && bend == 0f && !fire) return false;
            if (!keyAiming)
            {
                keyAiming = true;
                keyAim = 0f;
                keyPower = 0.5f;
                keyCurve = 0f;
            }

            keyAim = Mathf.Clamp(keyAim + steer * 3f * Time.deltaTime, -Pitch.GoalWidth, Pitch.GoalWidth);
            keyPower = Mathf.Clamp01(keyPower + lift * 0.5f * Time.deltaTime);
            keyCurve = Mathf.Clamp(keyCurve + bend * 1.2f * Time.deltaTime, -1f, 1f);

            var path = ShotPath.FromSwipe(BallCentre, keyAim, keyPower, keyCurve, settings, wind);
            if (fire)
            {
                keyAiming = false;
                Shoot(path);
            }
            else guide?.Show(path, settings.GuideLength);
            return true;
        }

        // ---------------------------------------------------------------- the kick

        void Shoot(ShotPath path)
        {
            phase = Phase.Shooting;
            guide?.Hide();
            hud?.SetHint("");
            var outcome = ShotJudge.Judge(path, defence, Random.value);
            Debug.Log($"[FreeKick] shot target={path.Target.x:0.00},{path.Target.y:0.00} bend={path.Bend:0.00} " +
                      $"time={path.FlightTime:0.00} -> {outcome.Result}");
            StartCoroutine(RunUpAndStrike(path, outcome));
        }

        /// <summary>
        /// The taker runs in and strikes. The ball only leaves the spot once the boot
        /// has reached it in the kick animation, so the kick and the shot are one action.
        /// </summary>
        IEnumerator RunUpAndStrike(ShotPath path, Outcome outcome)
        {
            if (striker != null)
            {
                Vector3 aim = path.Target - spot;
                aim.y = 0f;
                Quaternion facing = Quaternion.LookRotation(aim);
                Vector3 plant = spot + facing * new Vector3(-0.42f, 0f, -0.62f);
                Quaternion startRotation = striker.rotation;

                strikerAnimator?.SetTrigger(RunTrigger);
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / RunUpSeconds;
                    float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                    striker.position = Vector3.Lerp(takerSpot, plant, eased);
                    striker.rotation = Quaternion.Slerp(startRotation, facing, eased);
                    yield return null;
                }

                striker.position = plant;
                striker.rotation = facing;
                strikerAnimator?.SetTrigger(KickTrigger);
                yield return WaitForKickContact();
            }

            audio_?.PlayKick();
            ball?.Strike(path, outcome);
            wall?.Jump(settings.WallJump);
            StartCoroutine(KeeperReacts(path, outcome));
        }

        IEnumerator WaitForKickContact()
        {
            float waited = 0f;
            while (waited < KickContactTimeout)
            {
                if (strikerAnimator != null && !strikerAnimator.IsInTransition(0))
                {
                    var state = strikerAnimator.GetCurrentAnimatorStateInfo(0);
                    if (state.shortNameHash == KickState && state.normalizedTime >= KickContact) yield break;
                }
                else if (strikerAnimator != null)
                {
                    // Already blending into the kick: track the clip we are heading to.
                    var next = strikerAnimator.GetNextAnimatorStateInfo(0);
                    if (next.shortNameHash == KickState && next.normalizedTime >= KickContact) yield break;
                }
                waited += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// The keeper dives so he is at full stretch as the ball arrives. On a save he
        /// gets all the way there; when beaten he falls short, or guesses wrong.
        /// </summary>
        IEnumerator KeeperReacts(ShotPath path, Outcome outcome)
        {
            if (keeper == null || outcome.Result == ShotResult.Blocked) yield break;

            Vector3 crossing = path.Crossing;
            if (Mathf.Abs(crossing.x) > Pitch.GoalWidth / 2f + 1.2f || crossing.y > Pitch.GoalHeight + 1.2f) yield break;

            float arrive = path.AtDepth(Pitch.KeeperPlaneZ) * path.FlightTime;
            float wait = arrive - GoalkeeperController.DiveReach;
            if (wait > 0f) yield return new WaitForSeconds(wait);

            if (outcome.Result == ShotResult.Saved)
            {
                keeper.Dive(crossing, 1f);
                yield break;
            }

            float gap = crossing.x - defence.KeeperX;
            if (Mathf.Abs(gap) < 1.1f && outcome.IsGoal)
            {
                // Straight at him and it still went in: he must have gone early the wrong way.
                keeper.Dive(new Vector3(defence.KeeperX - Mathf.Sign(gap == 0f ? 1f : gap) * 2.2f, 0.8f, 0f), 1f);
                yield break;
            }
            keeper.Dive(crossing, Random.Range(0.45f, 0.7f));
        }

        void OnBallArrived(Outcome outcome)
        {
            int gained = 0;
            switch (outcome.Result)
            {
                case ShotResult.Goal:
                    goals++;
                    gained = GoalPoints;
                    if (outcome.Ring >= 0)
                    {
                        gained += RingPoints;
                        if (rings != null && outcome.Ring < rings.Length) rings[outcome.Ring]?.Pulse();
                        hud?.Flash("TOP CORNER!", HudController.MarkGoal);
                        hud?.SetMessage($"Through the ring! +{gained}");
                    }
                    else
                    {
                        hud?.Flash("GOAL!", HudController.MarkGoal);
                        hud?.SetMessage($"{taker.Name} scores! +{gained}");
                    }
                    audio_?.PlayGoal();
                    break;
                case ShotResult.Saved:
                    hud?.Flash("SAVED!", Color.white);
                    hud?.SetMessage("The keeper got a glove to it!");
                    audio_?.PlayBlock();
                    audio_?.PlayMiss();
                    break;
                case ShotResult.Blocked:
                    wall?.Flinch(outcome.WallIndex);
                    hud?.Flash("BLOCKED!", Color.white);
                    hud?.SetMessage("Hit the wall! Go higher, or bend it round.");
                    audio_?.PlayBlock();
                    audio_?.PlayMiss();
                    break;
                case ShotResult.Woodwork:
                    hud?.Flash("POST!", Color.white);
                    hud?.SetMessage("Off the woodwork - so close!");
                    audio_?.PlayPost();
                    audio_?.PlayMiss();
                    break;
                case ShotResult.Over:
                    hud?.Flash("OVER!", Color.white);
                    hud?.SetMessage("Over the bar! A shorter drag keeps it lower.");
                    audio_?.PlayMiss();
                    break;
                default:
                    hud?.Flash("WIDE!", Color.white);
                    hud?.SetMessage("Just wide! Aim more at the goal.");
                    audio_?.PlayMiss();
                    break;
            }

            Debug.Log($"[FreeKick] result kick={kickIndex + 1} {outcome.Result} ring={outcome.Ring} +{gained}");
            score += gained;
            hud?.MarkKick(kickIndex, outcome.IsGoal ? HudController.MarkGoal : HudController.MarkMiss);
            hud?.SetGoals(goals, kickIndex + 1);
            hud?.SetScore(score);
        }

        void OnBallResolved()
        {
            if (!roundStarted) return;
            kickIndex++;
            if (kickIndex >= KicksPerRound) EndRound();
            else
            {
                SetUpKick();
                BeginAiming();
            }
        }

        void EndRound()
        {
            phase = Phase.Done;
            roundStarted = false;

            int best = PlayerPrefs.GetInt(BestKey, 0);
            bool newBest = score > best;
            if (newBest)
            {
                best = score;
                PlayerPrefs.SetInt(BestKey, best);
                PlayerPrefs.Save();
            }
            hud?.SetBest(best);
            audio_?.PlayFullTime(goals >= 3);

            string title = goals == KicksPerRound ? "PERFECT!" :
                goals >= 3 ? $"{goals} GOALS!" :
                goals == 1 ? "1 GOAL" : $"{goals} GOALS";
            string praise = goals >= 4 ? "Brilliant free kicks!" :
                goals >= 2 ? "Nice shooting!" : "Keep practising - try a curve!";
            hud?.SetMessage("Round over");
            hud?.SetHint("");
            hud?.ShowOverlay(title,
                $"{praise} You scored {goals} of {KicksPerRound} for {score} points." +
                (newBest && score > 0 ? $" That's a new best on {settings.Name}!" : $" Best on {settings.Name}: {best}."),
                "Play again");

            taker = Roster.Random();
            SetUpKick();
        }

        public void Bind(BallController ballController, GoalkeeperController goalkeeper, WallController wallController,
            TargetRing[] targetRings, AimGuide aimGuide, HudController hudController, Transform strikerTransform,
            CameraFramer cameraFramer, MatchAudio matchAudio)
        {
            ball = ballController;
            keeper = goalkeeper;
            wall = wallController;
            rings = targetRings;
            guide = aimGuide;
            hud = hudController;
            striker = strikerTransform;
            framer = cameraFramer;
            audio_ = matchAudio;
        }
    }
}
