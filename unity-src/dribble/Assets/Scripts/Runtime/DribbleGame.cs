using System.Collections;
using MaccabiShared;
using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// The game loop. There are no levels: as soon as the game loads a short
    /// countdown runs over the pitch and the run starts by itself. It speeds up
    /// along one progression (Progression), collects stars and ends when a
    /// defender wins the ball; the end card's one button counts down the next run. Cones are forgiving: knocking
    /// one over only costs some speed. Everything else in the scene reacts to this
    /// class.
    ///
    /// Progress is shared with the website through PortalBridge: the game is
    /// marked played when a run starts, and each finished run adds 1-3 stars to
    /// the portal's total (Progression.StarsFor) and reports the score as a best.
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
        const string BestKey = "dribble.best";
        const float CountdownStep = 0.75f;

        [SerializeField] Course course;
        [SerializeField] Runner runner;
        [SerializeField] HudController hud;
        [SerializeField] DribbleAudio audio_;
        [SerializeField] CameraFramer framer;

        readonly LaneInput input = new LaneInput();

        bool running;
        bool countingDown;
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
        bool autopilot;
        int laneChanges;
        int conesHit;

        public bool Running => running;
        public float Speed => speed;
        public float Distance => distance;
        public int Stars => stars;
        public int Score => Mathf.FloorToInt(distance) + stars * StarPoints;

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

            hud?.StartButton?.onClick.AddListener(PlayAgain);
            PlayAgain();
        }

        /// <summary>Set the pitch up afresh and count the next run in.</summary>
        public void PlayAgain()
        {
            if (running || countingDown) return;
            StopAllCoroutines();
            StartCoroutine(CountdownThenRun());
        }

        /// <summary>
        /// "3, 2, 1, יאללה!" over the pitch, with the runner waiting on the ball,
        /// then the run starts by itself - no button to find first.
        /// </summary>
        IEnumerator CountdownThenRun()
        {
            countingDown = true;
            course.Begin(Progression.StartSpeed);
            runner.ResetToStart();
            hud?.HideOverlay();
            hud?.SetScore(0);
            hud?.SetDistance(0);
            hud?.SetStars(0);
            audio_?.FadeOutMenuMusic();

            // The first frames after loading can stall for a moment; let them pass
            // so the "3" is not swallowed.
            yield return null;
            yield return new WaitForSeconds(0.4f);

            foreach (var count in new[] { "3", "2", "1" })
            {
                hud?.Countdown(count);
                audio_?.PlayCount(false);
                yield return new WaitForSeconds(CountdownStep);
            }
            hud?.Countdown("יאללה!");
            audio_?.PlayCount(true);
            countingDown = false;
            StartRun();

            // The steering tip once "יאללה!" has had the screen to itself.
            yield return new WaitForSeconds(0.8f);
            if (running && !steered) hud?.ShowHint("החלק או הקש ימינה ושמאלה", 5f);
        }

        void Update()
        {
            if (!running)
            {
                // Space or Enter plays again from the end card too.
                if (hud != null && hud.OverlayVisible &&
                    (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
                    PlayAgain();
                return;
            }

            float dt = Time.deltaTime;
            runTime += dt;
            speedPenalty = Mathf.MoveTowards(speedPenalty, 0f, 0.6f * dt);
            speed = Mathf.Max(Progression.StartSpeed * 0.8f, Progression.SpeedAt(runTime) - speedPenalty);
            audio_?.SetRunTempo(Progression.Intensity(speed));

            int move = autopilot ? AutopilotMove() : input.Poll();
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
            Debug.Log($"[Dribble] Run over after {runTime:0}s: {Mathf.FloorToInt(distance)} m, {stars} stars, " +
                      $"score {score}, {laneChanges} lane changes, {conesHit} cones, speed {speed:0.0} m/s");
            int best = Best();
            bool newBest = score > best;
            if (newBest)
            {
                PlayerPrefs.SetInt(BestKey, score);
                PlayerPrefs.Save();
            }

            // Recorded straight away, so leaving during the replay loses nothing.
            int earned = Progression.StarsFor(score);
            PortalBridge.AddStars(earned);
            PortalBridge.ReportBest(Slug, score);

            audio_?.StopRunMusic();
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
            audio_?.PlayResult(earned);
            hud?.ShowEnd(title,
                $"כדררת {metres}\u00A0מ' {collected}.\nנקודות: {score}, שיא: {Best()}",
                "שחק שוב", earned, newBest);
        }

        void StartRun()
        {
            if (running) return;

            runTime = 0f;
            speed = Progression.StartSpeed;
            speedPenalty = 0f;
            distance = 0f;
            stars = 0;
            starRun = 0;
            nextMilestone = MilestoneMetres;
            steered = false;
            laneChanges = 0;
            conesHit = 0;

            runner.StartRunning();
            input.Reset();
            running = true;
            Debug.Log("[Dribble] Run started");

            bestAtStart = Best();
            bestCalled = false;
            PortalBridge.MarkPlayed(Slug);
            audio_?.PlayKickOff();
            audio_?.PlayRunMusic();
        }

        /// <summary>
        /// Test hook for the browser checks: SendMessage("DribbleGame", "Autopilot", "1")
        /// steers round every blocker so a scripted run can last as long as it needs
        /// (to reach a milestone or a star threshold); "0" hands control back.
        /// </summary>
        public void Autopilot(string on) => autopilot = on == "1";

        int AutopilotMove()
        {
            int lane = runner.Lane;
            if (Clearance(lane) > 2.2f * speed) return 0;
            // Head for the lane with the most room ahead, a step at a time.
            int best = lane;
            for (int other = 0; other < CourseGenerator.LaneCount; other++)
                if (Clearance(other) > Clearance(best) + 0.5f) best = other;
            return best == lane ? 0 : best > lane ? 1 : -1;
        }

        /// <summary>Metres to the nearest blocker ahead in a lane.</summary>
        float Clearance(int lane)
        {
            float nearest = float.MaxValue;
            var items = course.Active;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind == ItemKind.Star || item.Spent || item.Lane != lane) continue;
                float z = item.transform.localPosition.z;
                if (z > -0.4f && z < nearest) nearest = z;
            }
            return nearest;
        }

        static int Best() => PlayerPrefs.GetInt(BestKey, 0);
    }
}
