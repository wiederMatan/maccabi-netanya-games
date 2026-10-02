using System.Collections;
using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// The game loop. A career is a run of matches; a match is five shots; a shot is
    /// one math problem answered against a thirty second clock, then struck at the
    /// goal. Everything else in the scene reacts to this class.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public const float AnswerSeconds = 30f;
        public const int ShotsPerMatch = 5;
        const float RunUpSeconds = 0.55f;
        // Where the boot meets the ball in Anim_Kick, as a fraction of the clip:
        // the right foot is moving fastest, at ground level, at 0.75s of 1.5s.
        const float KickContact = 0.5f;
        // Fallback if the animator never reports the kick (no striker model).
        const float KickContactTimeout = 1.2f;
        static readonly int KickState = Animator.StringToHash("Kick");

        public static MatchManager Instance { get; private set; }

        static readonly string[] Opponents =
        {
            "Ironclad FC", "Vantage United", "Red Harbor",
            "Solstice City", "Granite Rovers", "Meridian AC"
        };

        [SerializeField] BallController ball;
        [SerializeField] GoalkeeperController keeper;
        [SerializeField] TargetZone[] zones;
        [SerializeField] HudController hud;
        [SerializeField] Transform striker;
        [SerializeField] MatchAudio audio_;
        [SerializeField] Difficulty difficulty = Difficulty.Pro;

        // Easy / Medium / Hard, in the order the overlay buttons appear.
        static readonly Difficulty[] Tiers =
        {
            Difficulty.Starter, Difficulty.Rookie, Difficulty.Pro, Difficulty.Legend
        };

        static readonly string[] TierBlurbs =
        {
            "Starter — adding and taking away, up to 20.",
            "Easy — addition and subtraction.",
            "Medium — adds multiplication.",
            "Hard — multiplication and division."
        };

        // Boards sit on the grass now, so they need enough opacity to hold their
        // own against the pitch behind them.
        static readonly Color ZoneIdle = new Color(0.07f, 0.17f, 0.27f, 0.92f);
        static readonly Color ZoneHover = new Color(0.91f, 0.71f, 0.30f, 0.96f);
        static readonly Color ZoneRight = new Color(0.16f, 0.60f, 0.32f, 0.96f);
        static readonly Color ZoneWrong = new Color(0.78f, 0.22f, 0.19f, 0.96f);

        MathProblem problem;
        float timeLeft;
        bool awaitingAnswer;
        bool shotInProgress;

        int score;
        int streak;
        int shotIndex;
        int matchIndex;
        int matchesWon;
        int playerGoals;
        int opponentGoals;
        bool careerStarted;
        SquadMember striker_;
        Animator strikerAnimator;
        Vector3 strikerHome;
        Quaternion strikerHomeRotation;

        // On a portrait phone the striker waits just behind the ball, like a
        // penalty taker, so CameraFramer can come in close on him and the goal.
        // Keep in step with CameraFramer.Subject.
        static readonly Vector3 PortraitStrikerMark = new Vector3(-1.3f, 0f, -1.2f);
        static bool IsPortrait => Screen.height > Screen.width;

        Vector3 StrikerMark => IsPortrait ? PortraitStrikerMark : strikerHome;

        // Facing the middle of the goal (goal line at z = 12, as in SceneBuilder).
        Quaternion StrikerMarkRotation => IsPortrait
            ? Quaternion.LookRotation(new Vector3(0f, 0f, 12f) - PortraitStrikerMark)
            : strikerHomeRotation;
        static readonly int KickTrigger = Animator.StringToHash("Kick");
        static readonly int RunTrigger = Animator.StringToHash("Run");

        string CurrentOpponent => Opponents[matchIndex % Opponents.Length];

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<MatchAudio>();

            if (striker != null)
            {
                strikerAnimator = striker.GetComponentInChildren<Animator>();
                strikerHome = striker.position;
                strikerHomeRotation = striker.rotation;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                zones[i].Configure(i, ZoneIdle, ZoneHover);
                zones[i].SetInteractable(false);
            }

            if (ball != null) ball.Resolved += OnStrikeResolved;

            if (hud != null)
            {
                hud.StartButton?.onClick.AddListener(OnStartPressed);

                var tierButtons = hud.DifficultyButtons;
                if (tierButtons != null)
                {
                    for (int i = 0; i < tierButtons.Length && i < Tiers.Length; i++)
                    {
                        int index = i;
                        tierButtons[i]?.onClick.AddListener(() => SelectDifficulty(index));
                    }
                }
                hud.HighlightDifficulty(System.Array.IndexOf(Tiers, difficulty));
                PickStriker();
                hud.SetScore(0);
                hud.SetStreak(0);
                hud.SetScoreline(0, 0);
                hud.SetBanner("Career — warm up");
                hud.SetProblem("");
                hud.SetFeedback("");
                hud.SetTimer(AnswerSeconds, AnswerSeconds);
                hud.ShowOverlay(
                    "MATH STRIKERS",
                    "Every shot brings a math problem and thirty seconds on the clock. " +
                    "Solve it, then strike the panel holding the right answer — click it, " +
                    "or press 1, 2 or 3. Outscore your opponent across five shots to take the match.",
                    "Kick Off");
            }
        }

        void Update()
        {
            if (!awaitingAnswer) return;

            timeLeft -= Time.deltaTime;
            hud?.SetTimer(timeLeft, AnswerSeconds);

            if (timeLeft <= 0f)
            {
                TimeUp();
                return;
            }

            for (int i = 0; i < zones.Length && i < 3; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    SubmitAnswer(zones[i]);
                    return;
                }
            }
        }

        /// <summary>Overlay difficulty picker. Takes effect from the next shot.</summary>
        void SelectDifficulty(int index)
        {
            if (index < 0 || index >= Tiers.Length) return;

            difficulty = Tiers[index];
            hud?.HighlightDifficulty(index);
            hud?.SetBanner(TierBlurbs[index]);
        }

        void OnStartPressed()
        {
            if (!careerStarted)
            {
                careerStarted = true;
                score = 0;
                streak = 0;
                matchIndex = 0;
                matchesWon = 0;
                hud?.SetScore(score);
                hud?.SetStreak(streak);
            }
            else
            {
                matchIndex++;
            }

            StartMatch();
        }

        /// <summary>A different squad member takes the shots each match.</summary>
        void PickStriker()
        {
            striker_ = Roster.Random();
            hud?.SetStriker(striker_);
        }

        void StartMatch()
        {
            PickStriker();
            shotIndex = 0;
            playerGoals = 0;
            opponentGoals = 0;

            hud?.HideOverlay();
            hud?.SetScoreline(0, 0);
            hud?.SetBanner($"Match {matchIndex + 1} — vs {CurrentOpponent}");
            audio_?.PlayWhistle();
            NextShot();
        }

        void NextShot()
        {
            problem = ProblemGenerator.Create(difficulty);

            for (int i = 0; i < zones.Length; i++)
            {
                zones[i].SetValue(problem.Options[i]);
                zones[i].ResetVisual();
                zones[i].SetInteractable(true);
            }

            ball?.Park();
            keeper?.ResetStance();
            ResetStriker();

            timeLeft = AnswerSeconds;
            awaitingAnswer = true;
            shotInProgress = false;

            hud?.SetProblem($"{problem.Text} = ?");
            hud?.SetFeedback("");
            hud?.SetTimer(timeLeft, AnswerSeconds);
        }

        /// <summary>Called by a target zone when the player commits to it.</summary>
        public void SubmitAnswer(TargetZone zone)
        {
            if (!awaitingAnswer || shotInProgress || zone == null) return;

            awaitingAnswer = false;
            shotInProgress = true;

            foreach (var z in zones) z.SetInteractable(false);

            bool correct = zone.Value == problem.Answer;
            zone.Flash(correct ? ZoneRight : ZoneWrong);
            if (!correct)
            {
                foreach (var z in zones)
                    if (z.Value == problem.Answer) z.Flash(ZoneRight);
            }

            // The keeper guesses a lane. A correct answer beats him unless he both
            // reads the lane and wins the coin flip - so good maths is rewarded but
            // never a guarantee, which is what keeps a match tense.
            int keeperLane = Random.value < 0.35f
                ? zone.LaneIndex
                : (zone.LaneIndex + Random.Range(1, zones.Length)) % zones.Length;

            bool saved = correct && keeperLane == zone.LaneIndex && Random.value < 0.25f;

            Vector3 target;

            if (correct && !saved)
            {
                int bonus = Mathf.RoundToInt(timeLeft);
                score += 10 + streak * 2 + bonus;
                streak++;
                playerGoals++;
                hud?.SetFeedback(streak >= 3
                    ? $"GOAL! {striker_.Name} again — streak ×{streak}, +{10 + (streak - 1) * 2 + bonus} points"
                    : $"GOAL! {striker_.Name} scores — +{10 + bonus} points");
                target = zone.AimPoint;
            }
            else if (saved)
            {
                streak = 0;
                hud?.SetFeedback($"{CurrentOpponent}'s keeper reads it — saved!");
                target = zone.AimPoint;
            }
            else
            {
                streak = 0;
                opponentGoals++;
                hud?.SetFeedback($"Wrong — it was {problem.Answer}. The shot sails over.");
                // A wrong answer drags the strike high and wide of the frame.
                target = zone.AimPoint + new Vector3(
                    Mathf.Sign(zone.AimPoint.x == 0f ? 1f : zone.AimPoint.x) * 1.4f, 2.4f, 0f);
            }

            hud?.SetScore(score);
            hud?.SetStreak(streak);
            hud?.SetScoreline(playerGoals, opponentGoals);
            StartCoroutine(RunUpAndStrike(target, keeperLane, correct && !saved));
        }

        void TimeUp()
        {
            awaitingAnswer = false;
            shotInProgress = true;

            foreach (var z in zones)
            {
                z.SetInteractable(false);
                if (z.Value == problem.Answer) z.Flash(ZoneRight);
            }

            streak = 0;
            opponentGoals++;
            hud?.SetStreak(streak);
            hud?.SetScoreline(playerGoals, opponentGoals);
            hud?.SetTimer(0f, AnswerSeconds);
            hud?.SetFeedback($"Out of time — it was {problem.Answer}. {CurrentOpponent} break away.");
            audio_?.PlayMiss();

            StartCoroutine(AdvanceAfter(1.6f));
        }

        /// <summary>
        /// The striker waits clear of the answer boards while the question is up,
        /// then runs in and strikes. The ball only leaves the spot once the boot has
        /// had time to reach it, so the kick and the shot read as one action.
        /// </summary>
        IEnumerator RunUpAndStrike(Vector3 target, int keeperLane, bool onTarget)
        {
            if (striker == null)
            {
                ball?.Strike(target);
                keeper?.Dive(zones[keeperLane].AimPoint);
                yield break;
            }

            Vector3 spot = ball != null ? ball.transform.position : Vector3.zero;
            Vector3 plant = spot + new Vector3(-0.42f, 0f, -0.62f);
            // Face where the shot is going, not just downfield.
            Vector3 lookAt = new Vector3(target.x, 0f, target.z) - new Vector3(plant.x, 0f, plant.z);
            Quaternion facing = lookAt.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookAt)
                : StrikerMarkRotation;

            strikerAnimator?.SetTrigger(RunTrigger);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / RunUpSeconds;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                striker.position = Vector3.Lerp(StrikerMark, plant, eased);
                striker.rotation = Quaternion.Slerp(StrikerMarkRotation, facing, eased);
                yield return null;
            }

            striker.position = plant;
            striker.rotation = facing;
            strikerAnimator?.SetTrigger(KickTrigger);

            // Release the ball when the boot actually reaches it in the animation,
            // not after a fixed delay, so the kick and the shot are one action.
            yield return WaitForKickContact();

            audio_?.PlayKick();
            ball?.Strike(target);
            keeper?.Dive(zones[keeperLane].AimPoint);

            // Let the ball travel before calling the result, so the cue lands with
            // the ball rather than with the boot.
            yield return new WaitForSeconds(0.55f);
            if (onTarget) audio_?.PlayGoal(); else audio_?.PlayMiss();
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

        /// <summary>Put the striker back on his mark for the next question.</summary>
        void ResetStriker()
        {
            // Deliberately not StopAllCoroutines: the shot-advance coroutine is the
            // one that called this, and killing it would stall the match.
            if (striker == null) return;
            striker.position = StrikerMark;
            striker.rotation = StrikerMarkRotation;
        }

        void OnStrikeResolved()
        {
            StartCoroutine(AdvanceAfter(0.6f));
        }

        IEnumerator AdvanceAfter(float delay)
        {
            yield return new WaitForSeconds(delay);

            shotIndex++;
            if (shotIndex >= ShotsPerMatch) EndMatch();
            else NextShot();
        }

        void EndMatch()
        {
            awaitingAnswer = false;
            shotInProgress = false;

            foreach (var z in zones)
            {
                z.SetInteractable(false);
                z.ResetVisual();
            }

            ball?.Park();
            keeper?.ResetStance();

            bool won = playerGoals > opponentGoals;
            bool drew = playerGoals == opponentGoals;
            audio_?.PlayFullTime(won || drew);
            if (won) matchesWon++;

            string verdict = won ? "Victory" : drew ? "Draw" : "Defeat";
            string nextOpponent = Opponents[(matchIndex + 1) % Opponents.Length];

            hud?.SetProblem("");
            hud?.SetFeedback("");
            hud?.SetBanner($"Full time — {verdict}");
            hud?.ShowOverlay(
                $"{verdict.ToUpperInvariant()}  {playerGoals}–{opponentGoals}",
                $"You {playerGoals} – {opponentGoals} {CurrentOpponent}. " +
                $"Career: {matchesWon} win{(matchesWon == 1 ? "" : "s")} from {matchIndex + 1} " +
                $"match{(matchIndex == 0 ? "" : "es")}, {score} points banked. " +
                $"Up next: {nextOpponent}.",
                "Next Match");
        }

        public void Bind(BallController ballController, GoalkeeperController goalkeeper,
            TargetZone[] targetZones, HudController hudController, Transform strikerTransform)
        {
            ball = ballController;
            keeper = goalkeeper;
            zones = targetZones;
            hud = hudController;
            striker = strikerTransform;
        }
    }
}
