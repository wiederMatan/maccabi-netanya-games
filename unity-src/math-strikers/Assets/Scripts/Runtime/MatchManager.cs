using System.Collections;
using MaccabiShared;
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

        /// <summary>The portal's slug for this game (localStorage keys, stars).</summary>
        public const string Slug = "math-strikers";

        public const string Title = "חלוצי החשבון";
        public const string Intro =
            "בכל בעיטה יש תרגיל חשבון. פותרים אותו ובוחרים את הלוח עם התשובה הנכונה, " +
            "והכדור טס לשער! יש 30 שניות לכל בעיטה. במשחק 5 בעיטות – מבקיעים יותר מהיריבה ומנצחים!";
        public const string KickOffLabel = "בעיטת פתיחה!";
        const string NextMatchLabel = "למשחק הבא!";

        static readonly string[] Opponents =
        {
            "הכרישים", "הנמרים", "הנשרים", "הזאבים", "הברקים", "הדובים"
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

        // The boards are lit 3D quads, so they are painted a shade brighter than
        // the flat HUD colours they are meant to match.
        static readonly Color ZoneIdle = Palette.Navy700;
        static readonly Color ZoneHover = Palette.Gold400;
        static readonly Color ZoneRight = Palette.Green400;
        static readonly Color ZoneWrong = Palette.Red400;

        MathProblem problem;
        float timeLeft;
        bool awaitingAnswer;
        bool shotInProgress;

        int score;
        int streak;
        int correctAnswers;
        int shotIndex;
        int matchIndex;
        int playerGoals;
        int opponentGoals;
        bool careerStarted;
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
                hud.SetScore(0);
                hud.SetStreak(0);
                hud.SetScoreline(0, 0);
                hud.SetBanner("משחק 1", $"נגד {CurrentOpponent}");
                hud.SetProblem("");
                hud.SetShot(0, ShotsPerMatch);
                hud.ClearFeedback();
                hud.SetTimer(AnswerSeconds, AnswerSeconds);
                hud.ShowOverlay(Title, Intro, KickOffLabel);
            }
            audio_?.PlayMenuMusic();
        }

        void Update()
        {
            if (!awaitingAnswer) return;

            timeLeft -= Time.deltaTime;
            hud?.SetTimer(timeLeft, AnswerSeconds);
            audio_?.SetUrgent(timeLeft < 10f);

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
        }

        /// <summary>Each match is a round of its own: its score is what the portal keeps as a best.</summary>
        void OnStartPressed()
        {
            if (careerStarted) matchIndex++;
            careerStarted = true;
            StartMatch();
        }

        void StartMatch()
        {
            shotIndex = 0;
            playerGoals = 0;
            opponentGoals = 0;
            correctAnswers = 0;
            score = 0;
            streak = 0;

            PortalBridge.MarkPlayed(Slug);

            hud?.HideOverlay();
            hud?.SetScore(score);
            hud?.SetStreak(streak);
            hud?.SetScoreline(0, 0);
            hud?.SetBanner($"משחק {matchIndex + 1}", $"נגד {CurrentOpponent}");
            audio_?.PlayWhistle();
            audio_?.PlayMatchMusic();
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
            hud?.SetShot(shotIndex + 1, ShotsPerMatch);
            hud?.ClearFeedback();
            hud?.SetTimer(timeLeft, AnswerSeconds);
        }

        /// <summary>Called by a target zone when the player commits to it.</summary>
        public void SubmitAnswer(TargetZone zone)
        {
            if (!awaitingAnswer || shotInProgress || zone == null) return;

            awaitingAnswer = false;
            audio_?.SetUrgent(false);
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
            string resultWord;
            Color resultColour;
            if (correct) correctAnswers++;

            if (correct && !saved)
            {
                int bonus = Mathf.RoundToInt(timeLeft);
                int points = 10 + streak * 2 + bonus;
                score += points;
                streak++;
                playerGoals++;
                hud?.SetFeedback(streak >= 3
                    ? $"תשובה נכונה! רצף של {streak}, קיבלת {points} נקודות"
                    : $"תשובה נכונה! קיבלת {points} נקודות", Palette.Green400);
                resultWord = "גול!";
                resultColour = Palette.Green400;
                target = zone.AimPoint;
            }
            else if (saved)
            {
                streak = 0;
                hud?.SetFeedback($"תשובה נכונה, אבל השוער של {CurrentOpponent} הציל!", Palette.Gold400);
                resultWord = "הצלה!";
                resultColour = Palette.Gold400;
                target = zone.AimPoint;
            }
            else
            {
                streak = 0;
                opponentGoals++;
                hud?.SetFeedback($"אופס! התשובה הנכונה היא {problem.Answer}", Palette.Red400);
                resultWord = "החמצה";
                resultColour = Palette.Red400;
                // A wrong answer drags the strike high and wide of the frame.
                target = zone.AimPoint + new Vector3(
                    Mathf.Sign(zone.AimPoint.x == 0f ? 1f : zone.AimPoint.x) * 1.4f, 2.4f, 0f);
            }

            hud?.SetScore(score);
            hud?.SetStreak(streak);
            hud?.SetScoreline(playerGoals, opponentGoals);
            StartCoroutine(RunUpAndStrike(target, keeperLane, correct && !saved, resultWord, resultColour));
        }

        void TimeUp()
        {
            awaitingAnswer = false;
            audio_?.SetUrgent(false);
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
            hud?.SetFeedback($"נגמר הזמן! התשובה הנכונה היא {problem.Answer}", Palette.Red400);
            hud?.ShowResult("נגמר הזמן!", Palette.Red400);
            audio_?.PlayMiss();

            StartCoroutine(AdvanceAfter(1.6f));
        }

        /// <summary>
        /// The striker waits clear of the answer boards while the question is up,
        /// then runs in and strikes. The ball only leaves the spot once the boot has
        /// had time to reach it, so the kick and the shot read as one action.
        /// </summary>
        IEnumerator RunUpAndStrike(Vector3 target, int keeperLane, bool onTarget, string resultWord, Color resultColour)
        {
            if (striker == null)
            {
                ball?.Strike(target);
                keeper?.Dive(zones[keeperLane].AimPoint);
                hud?.ShowResult(resultWord, resultColour);
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
            hud?.ShowResult(resultWord, resultColour);
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
            if (shotIndex >= ShotsPerMatch)
            {
                // Let the last result word have its moment before full time.
                yield return new WaitForSeconds(1.1f);
                EndMatch();
            }
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
            audio_?.SetUrgent(false);
            audio_?.PlayFullTime(won, drew);

            int stars = StarsFor(playerGoals, opponentGoals, correctAnswers);
            PortalBridge.AddStars(stars);
            PortalBridge.ReportBest(Slug, score);

            string verdict = won ? "ניצחון!" : drew ? "תיקו" : "הפסד";
            string nextOpponent = Opponents[(matchIndex + 1) % Opponents.Length];

            hud?.SetProblem("");
            hud?.SetShot(0, ShotsPerMatch);
            hud?.ClearFeedback();
            hud?.ShowOverlay(
                verdict,
                $"אנחנו {playerGoals}, {CurrentOpponent} {opponentGoals}\n" +
                $"צברת {score} נקודות\n" +
                $"המשחק הבא: נגד {nextOpponent}",
                NextMatchLabel,
                stars);
        }

        /// <summary>
        /// Stars for a finished match: 3 for a win with at least 4 right answers,
        /// 2 for any other win, 1 for a draw or for a loss with at least 2 right
        /// answers, otherwise 0.
        /// </summary>
        public static int StarsFor(int playerGoals, int opponentGoals, int correctAnswers)
        {
            if (playerGoals > opponentGoals) return correctAnswers >= 4 ? 3 : 2;
            if (playerGoals == opponentGoals) return 1;
            return correctAnswers >= 2 ? 1 : 0;
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
