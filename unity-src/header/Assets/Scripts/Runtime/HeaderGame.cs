using System.Collections;
using MaccabiShared;
using UnityEngine;

namespace Headers
{
    /// <summary>
    /// The game loop. There are no levels: as soon as the game loads a short
    /// countdown runs and the round starts by itself. A round is ten corners. Each
    /// one is swung in from the left or the right; a ring closes on the spot where
    /// the ball will meet the attacker's head, and one tap both times the jump and
    /// aims the header at the point of the goal under the finger. The corners get
    /// harder through the round (Progression). The end card's one button counts
    /// down the next round.
    ///
    /// Progress is shared with the website through PortalBridge: the game is
    /// marked played when a round starts, and each finished round adds 1-3 stars
    /// to the portal's total (Progression.StarsFor) and reports the score as a best.
    /// </summary>
    public class HeaderGame : MonoBehaviour
    {
        public const string Slug = "header";
        const string BestKey = "header.best";
        const float CountdownStep = 0.75f;
        const float SetUpSeconds = 0.9f;
        const float RunUpSeconds = 0.55f;
        // Anim_Kick is 1.5 s long and the boot meets the ball halfway through it.
        const float KickContact = 0.75f;
        const float AfterCorner = 1.7f;
        const float EndOverlayDelay = 1.2f;

        [SerializeField] Ball ball;
        [SerializeField] Footballer attacker;
        [SerializeField] Footballer defender;
        [SerializeField] Footballer taker;
        [SerializeField] Keeper keeper;
        [SerializeField] HudController hud;
        [SerializeField] TimingRing ring;
        [SerializeField] HeaderAudio audio_;
        [SerializeField] CameraFramer framer;

        bool playing;
        bool countingDown;
        int corner;
        int goals;
        int score;
        int perfects;
        bool autopilot;

        // The corner in the air right now.
        bool accepting;
        bool jumped;
        float jumpTime;
        float aimX;
        CornerSettings settings;
        float keeperX;
        int hintsShown;

        public bool Playing => playing;
        public int Goals => goals;
        public int Score => score;

        public void Bind(Ball matchBall, Footballer striker, Footballer marker, Footballer cornerTaker, Keeper goalkeeper,
            HudController hudController, TimingRing timingRing, HeaderAudio audio, CameraFramer cameraFramer)
        {
            ball = matchBall;
            attacker = striker;
            defender = marker;
            taker = cornerTaker;
            keeper = goalkeeper;
            hud = hudController;
            ring = timingRing;
            audio_ = audio;
            framer = cameraFramer;
        }

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<HeaderAudio>();
            if (hud != null) hud.StarRevealed += i => audio_?.PlayStar(i);
            hud?.StartButton?.onClick.AddListener(PlayAgain);
            PlayAgain();
        }

        /// <summary>Set the pitch up afresh and count the next round in.</summary>
        public void PlayAgain()
        {
            if (playing || countingDown) return;
            StopAllCoroutines();
            StartCoroutine(Round());
        }

        IEnumerator Round()
        {
            countingDown = true;
            corner = 0;
            goals = 0;
            score = 0;
            perfects = 0;
            SetUpCorner(0, 1);
            hud?.HideOverlay();
            hud?.SetScore(0);
            hud?.SetGoals(0);
            hud?.SetCorner(1, Progression.CornersPerRound);
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
            playing = true;
            PortalBridge.MarkPlayed(Slug);
            audio_?.PlayKickOff();
            audio_?.PlayRoundMusic();
            Debug.Log("[Header] Round started");
            yield return new WaitForSeconds(0.6f);

            int bestAtStart = Best();
            for (corner = 0; corner < Progression.CornersPerRound; corner++)
            {
                audio_?.SetTempo(corner / (float)(Progression.CornersPerRound - 1));
                yield return PlayCorner(corner);
                if (bestAtStart > 0 && score > bestAtStart && score - Progression.GoalPoints - Progression.PerfectBonus <= bestAtStart)
                    hud?.ShowHint("שיא חדש!", 1.5f);
            }

            playing = false;
            Finish();
        }

        /// <summary>Everyone to their marks for corner <paramref name="index"/>, swung in from <paramref name="side"/>.</summary>
        Vector3 SetUpCorner(int index, int side)
        {
            settings = Progression.For(index);
            var spot = Field.CornerSpot(side);
            float x = Mathf.Clamp(Random.Range(-settings.MeetSpread, settings.MeetSpread), -2.6f, 2.6f);
            var meet = new Vector3(x, Field.StandingHead + Field.JumpHeight, Field.GoalLineZ - settings.MeetDistance);
            var goal = new Vector3(0f, 0f, Field.GoalLineZ + 4f);

            // The attacker waits a step back from where the ball will come, so he
            // has to attack it; the defender stands goal side of him, toward the corner.
            attacker.Stand(new Vector3(meet.x * 0.4f - side * 1.1f, 0f, meet.z - 1.8f), goal);
            if (defender != null)
            {
                defender.gameObject.SetActive(settings.Defender);
                if (settings.Defender)
                    defender.Stand(new Vector3(meet.x + side * 0.85f, 0f, meet.z + 0.55f), spot);
            }

            // The keeper cheats toward the near post, more so as the round goes on.
            keeperX = side * Random.Range(0.3f, 1f) * settings.KeeperShift;
            keeper.TakePosition(keeperX, spot);

            taker.Stand(spot + new Vector3(side * 1.3f, 0f, -1.7f), meet);
            ball.Place(spot);
            ring?.Hide();
            return meet;
        }

        IEnumerator PlayCorner(int index)
        {
            int side = Random.value < 0.5f ? -1 : 1;
            var meet = SetUpCorner(index, side);
            var spot = Field.CornerSpot(side);
            hud?.SetCorner(index + 1, Progression.CornersPerRound);
            hud?.Toast(side > 0 ? "קרן מימין!" : "קרן משמאל!", 1f);
            if (index == Progression.FirstDefender) hud?.ShowHint("זהירות, מגן! תזמון טוב מנצח אותו", 2.6f);
            yield return new WaitForSeconds(SetUpSeconds);

            // The run-up and the strike.
            taker.RunTo(spot + new Vector3(side * 0.35f, 0f, -0.45f), RunUpSeconds, meet);
            yield return new WaitForSeconds(RunUpSeconds);
            taker.Kick();
            yield return new WaitForSeconds(KickContact);

            var cross = new Flight(spot, meet, settings.CrossTime);
            float arrive = Time.time + settings.CrossTime;
            float perfectAt = arrive - HeaderJudge.PerfectLead;
            audio_?.PlayCross();
            ball.Fly(cross, settings.CrossTime, null);
            var standAt = meet - Vector3.forward * 0.12f;
            attacker.RunTo(new Vector3(standAt.x, 0f, standAt.z), settings.CrossTime * 0.7f,
                new Vector3(meet.x * 0.5f, 0f, Field.GoalLineZ + 4f));
            ring?.Show(meet, perfectAt, settings.Tolerance);
            if (hintsShown == 0) { hud?.ShowHint("הקש כשהטבעת נסגרת!", 2.5f); hintsShown++; }
            else if (hintsShown == 1 && index >= 1) { hud?.ShowHint("הקש על המקום בשער שאליו תנגח", 2.5f); hintsShown++; }

            jumped = false;
            accepting = true;
            bool defenderUp = false;
            while (Time.time < arrive)
            {
                keeper.Watch(ball.transform.position);
                if (autopilot && !jumped && Time.time >= perfectAt) Jump(-Mathf.Sign(keeperX == 0f ? 1f : keeperX) * Field.AimLimit * 0.9f);
                if (!defenderUp && settings.Defender && defender != null && Time.time >= perfectAt + 0.03f)
                {
                    defenderUp = true;
                    defender.Jump(Field.JumpHeight * 0.8f);
                }
                yield return null;
            }
            accepting = false;
            ring?.Release();

            float jumpAge = jumped ? arrive - jumpTime : -1f;
            var outcome = HeaderJudge.Decide(settings, meet, jumpAge, aimX, keeperX, Random.Range(-1f, 1f));
            Debug.Log($"[Header] corner {index + 1} side {side} meet ({meet.x:0.00},{meet.z:0.00}) keeper {keeperX:0.00} " +
                      $"jump {(jumped ? $"{outcome.TimingError:+0.000;-0.000}s" : "none")} aim {aimX:0.00} -> {outcome.Result}" +
                      (outcome.Headed ? $" q {outcome.Quality:0.00} target ({outcome.Shot.Target.x:0.00},{outcome.Shot.Target.y:0.00})" : ""));

            if (outcome.Headed) yield return Headed(outcome);
            else if (outcome.Result == HeaderResult.Cleared) yield return Cleared(outcome, meet, side);
            else yield return Missed(outcome, cross);

            hud?.SetScore(score);
            hud?.SetGoals(goals);
            yield return new WaitForSeconds(AfterCorner);
        }

        IEnumerator Headed(HeaderOutcome outcome)
        {
            attacker.Header();
            audio_?.PlayHeader(outcome.Quality);
            var shot = outcome.Shot;

            float atKeeper = shot.TimeToZ(Field.KeeperLineZ);
            if (atKeeper < 0f) atKeeper = shot.Time;
            keeper.Dive(outcome.KeeperTarget, outcome.KeeperReach, settings.KeeperReaction,
                Mathf.Max(0.15f, atKeeper - settings.KeeperReaction));

            bool saved = outcome.Result == HeaderResult.Saved;
            float flyFor = saved ? atKeeper : shot.Time;
            bool done = false;
            ball.Fly(shot, flyFor, () => done = true);
            while (!done) yield return null;

            switch (outcome.Result)
            {
                case HeaderResult.Goal:
                    ball.Release(shot.VelocityAt(shot.Time));
                    goals++;
                    score += Progression.GoalPoints + (outcome.Perfect ? Progression.PerfectBonus : 0);
                    if (outcome.Perfect) perfects++;
                    hud?.Toast(outcome.Perfect ? "נגיחה מושלמת!" : "גול!", 1.4f);
                    audio_?.PlayGoal(outcome.Perfect);
                    attacker.Celebrate();
                    break;
                case HeaderResult.Saved:
                    // Parried out wide of the post.
                    var v = shot.VelocityAt(atKeeper);
                    float away = Mathf.Abs(v.x) > 0.3f ? Mathf.Sign(v.x) : (Random.value < 0.5f ? -1f : 1f);
                    ball.Release(new Vector3(away * 4.5f, 3f, -3.5f));
                    audio_?.PlayBlock();
                    audio_?.PlayMiss();
                    hud?.Toast("השוער הציל!", 1.3f);
                    break;
                case HeaderResult.Over:
                    ball.Release(shot.VelocityAt(shot.Time));
                    audio_?.PlayMiss();
                    hud?.Toast("מעל המשקוף!", 1.3f);
                    hud?.ShowHint("קפצת מוקדם מדי", 1.6f);
                    break;
                default:
                    ball.Release(shot.VelocityAt(shot.Time));
                    audio_?.PlayMiss();
                    hud?.Toast("ליד הקורה!", 1.3f);
                    break;
            }
        }

        IEnumerator Cleared(HeaderOutcome outcome, Vector3 meet, int side)
        {
            defender?.Header();
            audio_?.PlayBlock();
            // Headed back out the way it came.
            var clearance = new Flight(meet, new Vector3(meet.x + side * 7f, 0.4f, meet.z - 6f), 0.9f);
            bool done = false;
            ball.Fly(clearance, 0.6f, () => done = true);
            while (!done) yield return null;
            ball.Release(clearance.VelocityAt(0.6f));
            audio_?.PlayMiss();
            hud?.Toast("המגן הרחיק!", 1.3f);
            hud?.ShowHint(TimingTip(outcome), 1.8f);
        }

        IEnumerator Missed(HeaderOutcome outcome, Flight cross)
        {
            // The ball sails on past him, untouched.
            ball.Release(cross.VelocityAt(cross.Time));
            audio_?.PlayMiss();
            hud?.Toast("פספוס!", 1.2f);
            hud?.ShowHint(TimingTip(outcome), 1.8f);
            yield break;
        }

        static string TimingTip(HeaderOutcome outcome)
        {
            if (float.IsNaN(outcome.TimingError)) return "הקש כשהטבעת נסגרת!";
            if (outcome.Quality > 0f) return "כמעט! קפוץ בדיוק כשהטבעת נסגרת";
            return outcome.TimingError > 0f ? "קפצת מוקדם מדי" : "קפצת מאוחר מדי";
        }

        void Update()
        {
            if (!playing && !countingDown)
            {
                // Space or Enter plays again from the end card too.
                if (hud != null && hud.OverlayVisible &&
                    (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
                    PlayAgain();
                return;
            }

            if (!accepting || jumped || autopilot) return;

            if (Input.GetMouseButtonDown(0)) Jump(AimFromScreen(Input.mousePosition));
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Jump(-Field.AimLimit * 0.85f);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) Jump(Field.AimLimit * 0.85f);
            // Space aims for the side the keeper has left open.
            else if (Input.GetKeyDown(KeyCode.Space))
                Jump(-Mathf.Sign(keeperX == 0f ? 1f : keeperX) * Field.AimLimit * 0.85f);
        }

        void Jump(float aim)
        {
            jumped = true;
            jumpTime = Time.time;
            aimX = Mathf.Clamp(aim, -Field.AimLimit, Field.AimLimit);
            attacker.Jump();
            audio_?.PlayJump();
            ring?.Release();
        }

        /// <summary>Where across the goal mouth the finger landed.</summary>
        static float AimFromScreen(Vector3 screen)
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            var ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.z) < 1e-4f) return 0f;
            float t = (Field.GoalLineZ - ray.origin.z) / ray.direction.z;
            return t > 0f ? ray.origin.x + ray.direction.x * t : 0f;
        }

        void Finish()
        {
            int best = Best();
            bool newBest = score > best;
            if (newBest)
            {
                PlayerPrefs.SetInt(BestKey, score);
                PlayerPrefs.Save();
            }

            // Recorded straight away, so leaving during the end card loses nothing.
            int earned = Progression.StarsFor(goals);
            PortalBridge.AddStars(earned);
            PortalBridge.ReportBest(Slug, score);
            Debug.Log($"[Header] Round over: {goals}/{Progression.CornersPerRound} goals, {perfects} perfect, score {score}, {earned} stars");

            audio_?.StopRoundMusic();
            audio_?.PlayFinalWhistle(newBest);
            StartCoroutine(ShowEnd(earned, newBest));
        }

        IEnumerator ShowEnd(int earned, bool newBest)
        {
            yield return new WaitForSeconds(EndOverlayDelay);
            string title = earned == 3 ? "מדהים!" : earned == 2 ? "כל הכבוד!" : "יפה מאוד!";
            string scored = goals == 0 ? "לא הבקעת הפעם" : goals == 1 ? "הבקעת גול אחד" : $"הבקעת {goals} גולים";
            string perfect = perfects == 0 ? "" : perfects == 1 ? ", אחד מהם בנגיחה מושלמת" : $", {perfects} מהם בנגיחה מושלמת";
            audio_?.PlayResult(earned);
            hud?.ShowEnd(title,
                $"{scored} מתוך {Progression.CornersPerRound} קרנות{perfect}.\nנקודות: {score}, שיא: {Best()}",
                "שחק שוב", earned, newBest);
        }

        /// <summary>
        /// Test hook for the browser checks: SendMessage("HeaderGame", "Autopilot", "1")
        /// jumps at the perfect moment every corner and heads it away from the keeper,
        /// so a scripted round can reach three stars; "0" hands control back.
        /// </summary>
        public void Autopilot(string on) => autopilot = on == "1";

        static int Best() => PlayerPrefs.GetInt(BestKey, 0);
    }
}
