using System.Collections;
using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// The game loop. A round is five penalties against you. For each one the
    /// striker picks a spot, the spot glows for a moment (the tell), he runs in and
    /// shoots; dive the right way before the ball arrives and it is a save.
    /// Everything else in the scene reacts to this class.
    /// </summary>
    public class KeeperGame : MonoBehaviour
    {
        public const int ShotsPerRound = 5;
        const float ReadySeconds = 1.0f;
        const float ResultSeconds = 1.9f;
        // A drag longer than this (as a fraction of the screen's short side) is a swipe, not a tap.
        const float SwipeThreshold = 0.06f;

        [SerializeField] BallController ball;
        [SerializeField] GoalkeeperController keeper;
        [SerializeField] StrikerController striker;
        [SerializeField] SpotMarker[] markers;
        [SerializeField] HudController hud;
        [SerializeField] MatchAudio audio_;
        [SerializeField] Camera view;
        [SerializeField] int levelIndex = 1;

        Level level;
        bool live;          // a shot is coming: the player may dive
        int chosen = -1;    // where the player dived this shot, -1 for not yet
        bool kicked;        // the ball has left the spot
        int saves;
        int streak;
        int shotIndex;
        bool?[] results = new bool?[ShotsPerRound];
        int lastTarget = -1;
        int repeatCount;

        bool pressing;
        Vector2 pressStart;
        bool pressUsed;

        static readonly Color SaveColor = new Color(0.35f, 0.95f, 0.45f);
        static readonly Color GoalColor = new Color(1f, 0.38f, 0.3f);

        public bool AcceptingDive => live && chosen < 0;
        public int LevelIndex => levelIndex;

        void Start()
        {
            if (audio_ == null) audio_ = FindFirstObjectByType<MatchAudio>();
            if (view == null) view = Camera.main;
            level = Levels.All[levelIndex];

            striker?.ToMark(ball != null ? ball.transform.position : new Vector3(0f, 0f, Goal.PenaltySpotZ));
            ShowMarkers(level.SpotCount);

            if (hud == null) return;

            hud.StartButton?.onClick.AddListener(StartRound);
            var buttons = hud.LevelButtons;
            for (int i = 0; buttons != null && i < buttons.Length && i < Levels.All.Length; i++)
            {
                int index = i;
                buttons[i]?.onClick.AddListener(() => SelectLevel(index));
            }

            SelectLevel(levelIndex);
            hud.SetSaves(0, 0);
            hud.SetStreak(0);
            hud.SetPips(results, -1);
            hud.SetMessage("You're in goal for Maccabi Netanya!");
            hud.ShowOverlay("KEEPER",
                "Stop the penalties! Watch for the glowing spot, then dive: swipe or tap " +
                "left, middle or right. On a keyboard: arrow keys, or 1 2 3.",
                "Play!");
        }

        void SelectLevel(int index)
        {
            if (index < 0 || index >= Levels.All.Length) return;
            levelIndex = index;
            level = Levels.All[index];
            hud?.HighlightLevel(index);
            hud?.SetLevel(level.Name);
            hud?.SetBest(Best(index));
            if (!live) ShowMarkers(level.SpotCount);
        }

        static string BestKey(int index) => $"keeper.bestStreak.{index}";
        static int Best(int index) => PlayerPrefs.GetInt(BestKey(index), 0);

        void StartRound()
        {
            StopAllCoroutines();
            saves = 0;
            shotIndex = 0;
            results = new bool?[ShotsPerRound];
            hud?.HideOverlay();
            hud?.SetSaves(0, 0);
            hud?.SetStreak(streak);
            StartCoroutine(Round());
        }

        IEnumerator Round()
        {
            for (shotIndex = 0; shotIndex < ShotsPerRound; shotIndex++)
                yield return Shot();
            EndRound();
        }

        IEnumerator Shot()
        {
            int target = PickTarget();
            chosen = -1;
            live = false;
            kicked = false;

            ball?.Park();
            keeper?.ResetStance();
            striker?.ToMark(ball != null ? ball.transform.position : Vector3.zero);
            ShowMarkers(level.SpotCount);
            hud?.SetPips(results, shotIndex);
            hud?.SetMessage($"Shot {shotIndex + 1} of {ShotsPerRound} - get ready...");
            audio_?.PlayWhistle();

            yield return new WaitForSeconds(ReadySeconds);

            // The tell: the target glows while the striker starts his run.
            live = true;
            markers[target].Tell(level.TellSeconds);
            // Logged for the browser tests, which read the console to know when to dive.
            Debug.Log($"[Keeper] tell shot={shotIndex + 1} target={target}");
            hud?.SetMessage(level.HasHighSpots
                ? "Watch the glow! Swipe up for the top corners"
                : "Watch the glow, then dive!");

            Vector3 aim = Goal.Spots[target];
            if (striker != null) yield return striker.RunUpAndKick(aim, level.RunSeconds);

            audio_?.PlayKick();
            ball?.Strike(aim, level.FlightSeconds);
            kicked = true;
            if (chosen >= 0) LaunchDive();

            float arrival = Time.time + level.FlightSeconds;
            while (Time.time < arrival) yield return null;
            live = false;

            bool saved = chosen == target;
            results[shotIndex] = saved;

            if (saved)
            {
                saves++;
                streak++;
                ball?.Parry(Goal.Side(target));
                audio_?.PlaySave();
                markers[target].Show(SpotMarker.Look.Saved);
                hud?.ShowCallout(streak >= 3 ? $"SAVE! ×{streak}" : "SAVE!", SaveColor);
                hud?.SetMessage(streak >= 3 ? $"{streak} saves in a row!" : "What a save!");

                if (streak > Best(levelIndex))
                {
                    PlayerPrefs.SetInt(BestKey(levelIndex), streak);
                    PlayerPrefs.Save();
                    hud?.SetBest(streak);
                }
            }
            else
            {
                streak = 0;
                audio_?.PlayConceded();
                markers[target].Show(SpotMarker.Look.Conceded);
                hud?.ShowCallout("GOAL", GoalColor);
                hud?.SetMessage(chosen < 0
                    ? $"Too slow! It went {Goal.SpotNames[target]}."
                    : $"It went {Goal.SpotNames[target]}, you went {Goal.SpotNames[chosen]}.");
            }

            Debug.Log($"[Keeper] result shot={shotIndex + 1} target={target} chosen={chosen} saved={saved}");
            hud?.SetSaves(saves, shotIndex + 1);
            hud?.SetStreak(streak);
            hud?.SetPips(results, -1);

            yield return new WaitForSeconds(0.9f);
            keeper?.GetUp();
            yield return new WaitForSeconds(ResultSeconds - 0.9f);
        }

        /// <summary>Random spot, but never the same one three times running.</summary>
        int PickTarget()
        {
            int target;
            do target = Random.Range(0, level.SpotCount);
            while (target == lastTarget && repeatCount >= 1);

            repeatCount = target == lastTarget ? repeatCount + 1 : 0;
            lastTarget = target;
            return target;
        }

        void EndRound()
        {
            live = false;
            ball?.Park();
            keeper?.ResetStance();
            striker?.ToMark(ball != null ? ball.transform.position : Vector3.zero);
            ShowMarkers(level.SpotCount);

            audio_?.PlayFullTime(saves >= 3);

            string verdict = saves == ShotsPerRound ? "Perfect! A clean sheet!"
                : saves >= 3 ? "Great keeping!"
                : saves >= 1 ? "Good try - you'll get more next time!"
                : "Keep practising - watch for the glow!";

            Debug.Log($"[Keeper] round saves={saves}");
            hud?.SetMessage(verdict);
            hud?.ShowOverlay($"{saves} / {ShotsPerRound} SAVES",
                $"{verdict}  Best streak on {level.Name}: {Best(levelIndex)}." +
                (levelIndex < Levels.All.Length - 1 && saves >= 4 ? " Ready for the next level?" : ""),
                "Play again");
        }

        void ShowMarkers(int count)
        {
            if (markers == null) return;
            for (int i = 0; i < markers.Length; i++)
                markers[i]?.Show(i < count ? SpotMarker.Look.Idle : SpotMarker.Look.Hidden);
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (hud != null && hud.OverlayVisible)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartRound();
                pressing = false;
                return;
            }

            int spot = ReadKeys();
            if (spot < 0) spot = ReadPointer();
            if (spot >= 0 && AcceptingDive) DiveTo(Goal.Clamp(spot, level.SpotCount));
        }

        static int ReadKeys()
        {
            bool up = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                return up ? Goal.HighLeft : Goal.Left;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                return up ? Goal.HighRight : Goal.Right;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Space))
                return Goal.Centre;
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Keypad7)) return Goal.HighLeft;
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Keypad9)) return Goal.HighRight;

            for (int i = 0; i < 5; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    return i;
            return -1;
        }

        /// <summary>
        /// Mouse and touch (WebGL turns touches into mouse events). A drag past the
        /// threshold dives the moment it crosses it; a release without one is a tap
        /// on the spot nearest the finger.
        /// </summary>
        int ReadPointer()
        {
            Vector2 position = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                pressing = true;
                pressUsed = false;
                pressStart = position;
                // No return: a quick click can press and release within one frame.
            }

            if (!pressing) return -1;

            float threshold = SwipeThreshold * Mathf.Min(Screen.width, Screen.height);
            Vector2 delta = position - pressStart;

            if (!pressUsed && delta.magnitude > threshold)
            {
                pressUsed = true;
                return DiveInput.FromSwipe(delta, level.SpotCount);
            }

            if (Input.GetMouseButtonUp(0))
            {
                pressing = false;
                if (pressUsed) return -1;
                return DiveInput.FromTap(pressStart, SpotsOnScreen(), level.SpotCount);
            }

            return -1;
        }

        void LaunchDive()
        {
            keeper?.Dive(chosen);
            if (Goal.Side(chosen) != 0) audio_?.PlayDive();
        }

        Vector2[] SpotsOnScreen()
        {
            var points = new Vector2[Goal.Spots.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = view != null ? (Vector2)view.WorldToScreenPoint(Goal.Spots[i]) : Vector2.zero;
            return points;
        }

        /// <summary>
        /// Commit to a spot. A keeper who guesses early still waits for the strike
        /// before he leaves his line, so the dive and the ball arrive together.
        /// </summary>
        void DiveTo(int spot)
        {
            chosen = spot;
            if (kicked) LaunchDive();
            else keeper?.SetFor(spot);
            if (markers != null && spot < markers.Length) markers[spot].Pick();
        }

        public void Bind(BallController ballController, GoalkeeperController goalkeeper,
            StrikerController taker, SpotMarker[] spotMarkers, HudController hudController,
            MatchAudio audio, Camera camera)
        {
            ball = ballController;
            keeper = goalkeeper;
            striker = taker;
            markers = spotMarkers;
            hud = hudController;
            audio_ = audio;
            view = camera;
        }
    }
}
