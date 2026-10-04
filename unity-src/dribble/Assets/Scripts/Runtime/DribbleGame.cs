using System.Collections;
using MaccabiShared;
using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// The game loop. A run starts on the overlay, speeds up as it goes, collects
    /// stars and ends when a defender wins the ball. Cones are forgiving: knocking
    /// one over only costs some speed. Everything else in the scene reacts to this
    /// class.
    ///
    /// Progress is shared with the website through PortalBridge: the game is
    /// marked played when a run starts, and each finished run adds 1-3 stars to
    /// the portal's total (Tiers.StarScores) and reports the score as a best.
    /// </summary>
    public class DribbleGame : MonoBehaviour
    {
        public const string Slug = "dribble";
        public const int StarPoints = 10;
        const int MilestoneMetres = 100;
        // A blocker is hit when it reaches the ball out in front, or the runner's
        // feet, and is close enough across the pitch to touch.
        const float HitBehind = -0.35f;
        const float HitAhead = 1.0f;
        const float HitAcross = 0.72f;
        // Stars are a little more generous than blockers.
        const float StarAcross = 0.95f;
        const float StarBehind = -0.6f;
        const float StarAhead = 1.3f;
        // A knocked cone costs this share of the speed, but never drops below the start speed.
        const float ConeSlowdown = 0.25f;
        const float EndOverlayDelay = 1.7f;
        const string BestKeyPrefix = "dribble.best.";

        [SerializeField] Course course;
        [SerializeField] Runner runner;
        [SerializeField] HudController hud;
        [SerializeField] DribbleAudio audio_;
        [SerializeField] CameraFramer framer;
        [SerializeField] Difficulty difficulty = Difficulty.Starter;

        readonly LaneInput input = new LaneInput();

        TierSettings tier;
        bool running;
        float runTime;
        float speed;
        // Speed lost to cones, recovered gradually.
        float speedPenalty;
        float distance;
        int stars;
        int starRun;
        float lastStarTime;
        int nextMilestone;
        bool steered;
        int bestAtStart;
        bool bestCalled;
        int laneChanges;
        int conesHit;

        public bool Running => running;
        public float Speed => speed;
        public float Distance => distance;
        public int Stars => stars;
        public int Score => Mathf.FloorToInt(distance) + stars * StarPoints;
        public Difficulty CurrentDifficulty => difficulty;

        public void Bind(Course pitch, Runner player, HudController hudController, DribbleAudio audio, CameraFramer cameraFramer)
        {
            course = pitch;
            runner = player;
            hud = hudController;
            audio_ = audio;
            framer = cameraFramer;
        }

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<DribbleAudio>();
            if (framer != null && runner != null) framer.Target = runner.Body;
            if (runner != null) runner.Touched += () => audio_?.PlayTouch();
            if (hud != null) hud.StarRevealed += i => audio_?.PlayStar(i);

            if (hud != null)
            {
                hud.StartButton?.onClick.AddListener(StartRun);

                var tierButtons = hud.DifficultyButtons;
                if (tierButtons != null)
                {
                    for (int i = 0; i < tierButtons.Length && i < Tiers.All.Length; i++)
                    {
                        int index = i;
                        tierButtons[i]?.onClick.AddListener(() => SelectDifficulty(index));
                    }
                }

                hud.HighlightDifficulty(System.Array.IndexOf(Tiers.All, difficulty));
                hud.SetScore(0);
                hud.SetDistance(0);
                hud.SetStars(0);
                hud.ShowMenu(
                    "כדרור!",
                    "רוץ עם הכדור לאורך המגרש! החלק או הקש ימינה ושמאלה " +
                    "כדי לעקוף את המגינים, ואסוף כוכבים.",
                    "בעיטת פתיחה!");
            }

            tier = Tiers.For(difficulty);
            course?.Begin(tier, tier.StartSpeed);
        }

        void Update()
        {
            if (!running)
            {
                // Space or Enter starts a run from the overlay too.
                if (hud != null && hud.OverlayVisible &&
                    (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
                    StartRun();
                return;
            }

            float dt = Time.deltaTime;
            runTime += dt;
            speedPenalty = Mathf.MoveTowards(speedPenalty, 0f, 0.6f * dt);
            speed = Mathf.Max(tier.StartSpeed * 0.8f, tier.SpeedAt(runTime) - speedPenalty);

            int move = input.Poll();
            if (move != 0 && runner.Shift(move))
            {
                laneChanges++;
                audio_?.PlayLaneChange();
                if (!steered)
                {
                    steered = true;
                    hud?.HideHint();
                }
            }

            float step = speed * dt;
            distance += step;
            course.Advance(step, speed);
            runner.Tick(speed, dt);

            CheckContacts();
            if (!running) return;

            if (distance >= nextMilestone)
            {
                hud?.Toast($"{nextMilestone} מ'!");
                audio_?.PlayMilestone();
                nextMilestone += MilestoneMetres;
            }

            // Passing the old best mid-run deserves a shout.
            if (!bestCalled && bestAtStart > 0 && Score > bestAtStart)
            {
                bestCalled = true;
                hud?.Toast("שיא חדש!");
                audio_?.PlayMilestone();
            }

            hud?.SetDistance(Mathf.FloorToInt(distance));
            hud?.SetScore(Score);
        }

        void CheckContacts()
        {
            var items = course.Active;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Spent || !item.gameObject.activeSelf) continue;

                Vector3 p = item.transform.localPosition;
                float across = Mathf.Abs(p.x - runner.X);

                if (item.Kind == ItemKind.Star)
                {
                    if (p.z < StarBehind || p.z > StarAhead || across > StarAcross) continue;
                    CollectStar(item);
                    continue;
                }

                if (p.z < HitBehind || p.z > HitAhead || across > HitAcross) continue;

                if (item.Kind == ItemKind.Cone) HitCone(item);
                else
                {
                    Tackled(item);
                    return;
                }
            }
        }

        void CollectStar(PitchItem star)
        {
            star.Collect();
            stars++;
            // Stars picked up in quick succession ring higher and higher.
            starRun = Time.time - lastStarTime < 0.8f ? starRun + 1 : 0;
            lastStarTime = Time.time;
            audio_?.PlayStar(starRun);
            hud?.SetStars(stars);
        }

        void HitCone(PitchItem cone)
        {
            cone.KnockAway(cone.transform.localPosition.x >= runner.X ? 1f : -1f);
            speedPenalty = Mathf.Max(speedPenalty, speed * ConeSlowdown);
            conesHit++;
            audio_?.PlayCone();
            hud?.Toast("זהירות, קונוס!", 1f);
        }

        void Tackled(PitchItem defender)
        {
            defender.MarkSpent();
            running = false;
            runner.Fall();

            int score = Score;
            hud?.SetDistance(Mathf.FloorToInt(distance));
            hud?.SetScore(score);
            Debug.Log($"[Dribble] Run over on {difficulty}: {Mathf.FloorToInt(distance)} m, {stars} stars, " +
                      $"score {score}, {laneChanges} lane changes, {conesHit} cones, top speed {speed:0.0} m/s");
            int best = Best(difficulty);
            bool newBest = score > best;
            if (newBest)
            {
                PlayerPrefs.SetInt(BestKeyPrefix + difficulty, score);
                PlayerPrefs.Save();
            }

            // Recorded straight away, so leaving during the replay loses nothing.
            int earned = Tiers.StarsFor(difficulty, score);
            PortalBridge.AddStars(earned);
            PortalBridge.ReportBest(Slug, score);

            audio_?.PlayTackle(newBest);
            hud?.Toast("אוי!", 1.2f);
            StartCoroutine(ShowEnd(score, earned, newBest));
        }

        IEnumerator ShowEnd(int score, int earned, bool newBest)
        {
            yield return new WaitForSeconds(EndOverlayDelay);

            int metres = Mathf.FloorToInt(distance);
            string title = earned == 3 ? "מדהים!" : earned == 2 ? "כל הכבוד!" : "יפה מאוד!";
            string collected = stars == 0 ? "ולא אספת כוכבים" : stars == 1 ? "ואספת כוכב אחד" : $"ואספת {stars} כוכבים";
            hud?.ShowEnd(title,
                $"כדררת {metres}\u00A0מ' {collected}.\nנקודות: {score}, שיא: {Best(difficulty)}",
                "שחק שוב", earned, newBest);
        }

        /// <summary>Overlay level picker. Takes effect from the next run.</summary>
        void SelectDifficulty(int index)
        {
            if (index < 0 || index >= Tiers.All.Length) return;
            difficulty = Tiers.All[index];
            hud?.HighlightDifficulty(index);
        }

        public void StartRun()
        {
            if (running) return;
            StopAllCoroutines();

            tier = Tiers.For(difficulty);
            runTime = 0f;
            speed = tier.StartSpeed;
            speedPenalty = 0f;
            distance = 0f;
            stars = 0;
            starRun = 0;
            nextMilestone = MilestoneMetres;
            steered = false;
            laneChanges = 0;
            conesHit = 0;

            course.Begin(tier, speed);
            runner.ResetToStart();
            runner.StartRunning();
            input.Reset();
            running = true;

            hud?.HideOverlay();
            hud?.SetScore(0);
            hud?.SetDistance(0);
            hud?.SetStars(0);
            hud?.ShowHint("החלק או הקש ימינה ושמאלה", 6f);
            bestAtStart = Best(difficulty);
            bestCalled = false;
            PortalBridge.MarkPlayed(Slug);
            audio_?.PlayKickOff();
        }

        static int Best(Difficulty level) => PlayerPrefs.GetInt(BestKeyPrefix + level, 0);
    }
}
