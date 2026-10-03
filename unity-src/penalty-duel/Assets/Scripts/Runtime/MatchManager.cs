using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PenaltyDuel
{
    /// <summary>
    /// The game loop. A duel is a penalty shootout between two players sharing one
    /// device (or one player and the computer). Every kick has two picks: the
    /// shooter taps a spot in the goal, the phone is passed behind a screen that
    /// hides the choice, then the keeper taps where to dive. The run-up, the kick
    /// and the dive then play out together. The players swap roles every kick.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        const float RunUpSeconds = 0.55f;
        // Where the boot meets the ball in Anim_Kick, as a fraction of the clip:
        // the right foot is moving fastest, at ground level, at 0.75s of 1.5s.
        const float KickContact = 0.5f;
        // Fallback if the animator never reports the kick (no striker model).
        const float KickContactTimeout = 1.2f;
        // A tap that ends one step must not also count as a pick in the next.
        const float PickGuardSeconds = 0.25f;
        static readonly int KickState = Animator.StringToHash("Kick");
        static readonly int KickTrigger = Animator.StringToHash("Kick");
        static readonly int RunTrigger = Animator.StringToHash("Run");

        public static MatchManager Instance { get; private set; }

        [SerializeField] BallController ball;
        [SerializeField] GoalkeeperController keeper;
        [SerializeField] TargetZone[] zones;
        [SerializeField] HudController hud;
        [SerializeField] Transform striker;
        [SerializeField] CameraFramer framer;
        [SerializeField] MatchAudio audio_;

        enum Phase { Menu, Shoot, Pass, Save, Kick, Over }

        /// <summary>One side of the duel: a name, a kit, and the squad player they shoot as.</summary>
        class Side
        {
            public string Name;
            public Color Colour;
            public Color Shorts;
            public SquadMember Member;
            public bool Computer;
        }

        // In-game text sticks to plain ASCII punctuation: the built-in font that ships
        // in the web build has no dashes or ellipsis, and they render as gaps.

        // Player 1 wears Maccabi yellow and black; Player 2 a sky-blue away kit, so
        // the two can never be confused on the pitch or on the scoreboard.
        public static readonly Color Player1Colour = new Color(0.98f, 0.82f, 0.09f);
        public static readonly Color Player2Colour = new Color(0.33f, 0.68f, 1.00f);
        static readonly Color Player1Shorts = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color Player2Shorts = new Color(0.94f, 0.95f, 0.97f);
        static readonly Color SpotIdle = new Color(1f, 1f, 1f, 0.22f);

        Phase phase = Phase.Menu;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void ReportPhase(string phase);
#else
        static void ReportPhase(string phase) { }
#endif

        void SetPhase(Phase next)
        {
            phase = next;
            ReportPhase(next.ToString());
        }

        Shootout shootout = new Shootout();
        readonly Side[] sides = new Side[2];
        bool vsComputer;
        int shotSpot;
        int diveSpot;
        float pickOpensAt;
        float lastPress = -1f;
        readonly System.Collections.Generic.HashSet<int> touchesDown = new System.Collections.Generic.HashSet<int>();
        bool mouseDown;

        Animator strikerAnimator;
        Vector3 strikerHome;
        Quaternion strikerHomeRotation;

        Side ShooterSide => sides[shootout.Shooter];
        Side KeeperSide => sides[1 - shootout.Shooter];

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
            if (framer == null) framer = FindFirstObjectByType<CameraFramer>();

            if (striker != null)
            {
                strikerAnimator = striker.GetComponentInChildren<Animator>();
                strikerHome = striker.position;
                strikerHomeRotation = striker.rotation;
            }

            foreach (var zone in zones)
            {
                zone.Configure(SpotIdle);
                zone.SetInteractable(false);
                zone.SetVisible(false);
            }

            if (hud != null)
            {
                hud.PrimaryButton?.onClick.AddListener(OnPrimary);
                hud.SecondaryButton?.onClick.AddListener(OnSecondary);
                hud.PassButton?.onClick.AddListener(OnPassReady);
            }

            SetUpSides(false);
            ShowMenu();
            ReportPhase(phase.ToString());
        }

        void Update()
        {
            // Taps are read straight from the pointer rather than through the UI
            // event system or OnMouseDown: a quick tap in a mobile browser can start
            // and end within one frame, and both of those then miss it - the button
            // or the goal just looks dead to a child.
            bool pressed = PointerPressed(out Vector2 pointer);
            if (pressed && hud != null)
            {
                switch (hud.ButtonAt(pointer))
                {
                    case 0: OnPrimary(); return;
                    case 1: OnSecondary(); return;
                    case 2: OnPassReady(); return;
                }
            }

            switch (phase)
            {
                case Phase.Menu:
                    if (Pressed(KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space)) OnPrimary();
                    else if (Input.GetKeyDown(KeyCode.C)) OnSecondary();
                    break;

                case Phase.Over:
                    if (Pressed(KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space)) OnPrimary();
                    else if (Pressed(KeyCode.Escape, KeyCode.M, KeyCode.Backspace)) OnSecondary();
                    break;

                case Phase.Pass:
                    if (Pressed(KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space)) OnPassReady();
                    break;

                case Phase.Shoot:
                case Phase.Save:
                    for (int i = 0; i < zones.Length; i++)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                        {
                            PickSpot(i);
                            return;
                        }
                    }
                    if (pressed) PickSpot(SpotAt(pointer));
                    break;
            }
        }

        /// <summary>
        /// A press this frame, and where: a touch or click going down - or, when a
        /// quick tap was folded into a single frame and the down never showed, the
        /// release. Releasing after a seen press does not count again, so holding a
        /// button until the next screen is up cannot press what appears under it.
        /// </summary>
        bool PointerPressed(out Vector2 position)
        {
            // The mouse is read first, every frame, so its state stays true even
            // when a touch (which the browser also reports as a mouse) answers below.
            bool mouseWentDown = Input.GetMouseButtonDown(0);
            bool mouseWentUp = Input.GetMouseButtonUp(0);
            bool mouseFolded = mouseWentUp && !mouseDown && !mouseWentDown;
            if (mouseWentDown) mouseDown = true;
            if (mouseWentUp) mouseDown = false;

            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                position = touch.position;
                if (touch.phase == TouchPhase.Began)
                {
                    touchesDown.Add(touch.fingerId);
                    return true;
                }
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (!touchesDown.Remove(touch.fingerId) && touch.phase == TouchPhase.Ended) return true;
                }
            }

            position = Input.mousePosition;
            return mouseWentDown || mouseFolded;
        }

        /// <summary>The spot under a screen point, or -1.</summary>
        int SpotAt(Vector2 screenPoint)
        {
            var cam = framer != null ? framer.GetComponent<Camera>() : Camera.main;
            if (cam == null) return -1;

            // Through the viewport rather than ScreenPointToRay: in the browser the
            // camera can render at a different pixel size from the one input is
            // reported in, and the tap then lands somewhere else in the goal.
            if (Screen.width <= 0 || Screen.height <= 0) return -1;
            var viewport = new Vector3(screenPoint.x / Screen.width, screenPoint.y / Screen.height, 0f);
            var hits = Physics.RaycastAll(cam.ViewportPointToRay(viewport), 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            int spot = -1;
            foreach (var hit in hits)
            {
                var zone = hit.collider.GetComponent<TargetZone>();
                if (zone == null || hit.distance >= nearest) continue;
                nearest = hit.distance;
                spot = zone.Index;
            }
            return spot;
        }

        static bool Pressed(KeyCode a, KeyCode b, KeyCode c) =>
            Input.GetKeyDown(a) || Input.GetKeyDown(b) || Input.GetKeyDown(c);

        // ---------------------------------------------------------------- menu and end

        void ShowMenu()
        {
            SetPhase(Phase.Menu);
            StopAllCoroutines();
            hud?.HidePass();
            HideSpots();
            framer?.SetAimView(false);
            ball?.Park();
            keeper?.ResetStance();
            ResetStriker();

            hud?.SetPrompt("", Color.white, "");
            hud?.SetRound("");
            hud?.SetHint("");
            hud?.ShowOverlay(
                "PENALTY DUEL",
                "One shoots, one saves, then swap!\nFive kicks each. Most goals wins.",
                null, "2 PLAYERS", "VS COMPUTER");
        }

        /// <summary>
        /// One press, one action: the event system and the pointer check in Update
        /// can both report the same tap, and the screen it lands on has changed by
        /// the time the second one arrives.
        /// </summary>
        bool Debounce()
        {
            if (Time.unscaledTime - lastPress < 0.25f) return false;
            lastPress = Time.unscaledTime;
            return true;
        }

        void OnPrimary()
        {
            Deselect();
            if (!Debounce()) return;
            if (phase == Phase.Menu) StartDuel(false);
            else if (phase == Phase.Over) StartDuel(vsComputer);
        }

        void OnSecondary()
        {
            Deselect();
            if (!Debounce()) return;
            if (phase == Phase.Menu) StartDuel(true);
            else if (phase == Phase.Over) ShowMenu();
        }

        /// <summary>
        /// A clicked button stays selected, and Enter would press it again on top of
        /// the keyboard shortcut - so let go of it.
        /// </summary>
        static void Deselect()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>Two different squad players, one for each side to shoot as.</summary>
        void SetUpSides(bool computer)
        {
            var first = Roster.Random();
            var second = Roster.Random();
            for (int tries = 0; tries < 20 && second.Name == first.Name; tries++) second = Roster.Random();

            sides[0] = new Side
            {
                Name = "PLAYER 1", Colour = Player1Colour, Shorts = Player1Shorts, Member = first
            };
            sides[1] = new Side
            {
                Name = computer ? "COMPUTER" : "PLAYER 2", Colour = Player2Colour, Shorts = Player2Shorts,
                Member = second, Computer = computer
            };

            for (int i = 0; i < sides.Length; i++)
                hud?.SetPlayer(i, sides[i].Name, sides[i].Colour, sides[i].Member);
        }

        void StartDuel(bool computer)
        {
            if (phase != Phase.Menu && phase != Phase.Over) return;

            vsComputer = computer;
            shootout = new Shootout();
            SetUpSides(computer);

            hud?.HideOverlay();
            hud?.HidePass();
            audio_?.PlayWhistle();
            BeginKick();
        }

        void EndDuel()
        {
            SetPhase(Phase.Over);
            HideSpots();
            framer?.SetAimView(false);

            int winner = shootout.Winner;
            var side = sides[winner];
            int goals0 = shootout.Goals(0), goals1 = shootout.Goals(1);
            bool computerWon = side.Computer;

            audio_?.PlayFullTime(!computerWon);

            hud?.SetActive(-1);
            hud?.SetPrompt($"{side.Name} WINS!", side.Colour, "");
            hud?.SetHint("");
            hud?.ShowOverlay(
                $"{side.Name} WINS!",
                $"{sides[0].Name}  {goals0} - {goals1}  {sides[1].Name}\n" +
                (computerWon ? "So close! Have another go." : $"{side.Member.Name} is the penalty hero!"),
                Roster.LoadPortrait(side.Member),
                "PLAY AGAIN", "MENU");
        }

        // ---------------------------------------------------------------- a kick

        void BeginKick()
        {
            int shooter = shootout.Shooter;
            var shooterSide = sides[shooter];
            var keeperSide = sides[1 - shooter];

            if (striker != null) KitPainter.Paint(striker.gameObject, shooterSide.Colour, shooterSide.Shorts);
            if (keeper != null) KitPainter.Paint(keeper.gameObject, keeperSide.Colour, keeperSide.Shorts);

            ball?.Park();
            keeper?.ResetStance();
            ResetStriker();

            RefreshScoreboard();
            hud?.SetActive(shooter);
            hud?.SetRound(shootout.SuddenDeath
                ? "SUDDEN DEATH"
                : $"KICK {shootout.Round + 1} OF {Shootout.Regulation}");

            if (shooterSide.Computer)
            {
                // The computer aims in secret, then the player in goal guesses.
                shotSpot = Random.Range(0, Shootout.Spots);
                BeginSave();
                return;
            }

            SetPhase(Phase.Shoot);
            hud?.SetPrompt($"{shooterSide.Name}: SHOOT!", shooterSide.Colour, "Tap a spot in the goal to aim.");
            hud?.SetHint("Keyboard: press 1 to 6");
            OpenSpots(shooterSide.Colour);
        }

        void BeginSave()
        {
            SetPhase(Phase.Save);
            var keeperSide = KeeperSide;
            hud?.SetPrompt($"{keeperSide.Name}: SAVE!", keeperSide.Colour,
                ShooterSide.Computer
                    ? "The computer is shooting! Tap where you will dive."
                    : "Tap where you will dive.");
            hud?.SetHint("Keyboard: press 1 to 6");
            OpenSpots(keeperSide.Colour);
        }

        /// <summary>A spot in the goal was tapped, clicked or picked by its number key.</summary>
        public void PickSpot(int index)
        {
            if (Time.time < pickOpensAt || index < 0 || index >= zones.Length) return;

            if (phase == Phase.Shoot)
            {
                shotSpot = index;
                CloseSpots();
                zones[index].Flash(Tint(ShooterSide.Colour, 0.85f));

                if (KeeperSide.Computer)
                {
                    diveSpot = Random.Range(0, Shootout.Spots);
                    StartCoroutine(TakeKick());
                }
                else
                {
                    SetPhase(Phase.Pass);
                    StartCoroutine(PassAfter(0.35f));
                }
            }
            else if (phase == Phase.Save)
            {
                diveSpot = index;
                CloseSpots();
                zones[index].Flash(Tint(KeeperSide.Colour, 0.85f));
                StartCoroutine(TakeKick());
            }
        }

        /// <summary>
        /// Hand the phone over: an opaque screen goes up and the spots are wiped, so
        /// the keeper sees nothing of where the shot is going.
        /// </summary>
        IEnumerator PassAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            foreach (var zone in zones) zone.ResetVisual();

            var shooterSide = ShooterSide;
            var keeperSide = KeeperSide;
            hud?.SetPrompt("", Color.white, "");
            hud?.ShowPass(
                $"PASS TO {keeperSide.Name}",
                $"{Capitalise(shooterSide.Name)} has aimed. No peeking!\n" +
                $"{Capitalise(keeperSide.Name)}, you are in goal.",
                "READY TO SAVE!", keeperSide.Colour);
            ReportPhase("PassShown");
        }

        void OnPassReady()
        {
            Deselect();
            if (phase != Phase.Pass || hud == null || !hud.PassShowing || !Debounce()) return;
            hud.HidePass();
            BeginSave();
        }

        IEnumerator TakeKick()
        {
            SetPhase(Phase.Kick);
            var shooterSide = ShooterSide;
            var keeperSide = KeeperSide;
            bool saved = Shootout.IsSave(shotSpot, diveSpot, Random.value);

            // Let the last pick register, then pull back to watch the kick.
            yield return new WaitForSeconds(0.35f);
            HideSpots();
            framer?.SetAimView(false);
            hud?.SetPrompt($"{shooterSide.Name} shoots...", shooterSide.Colour, "");
            hud?.SetHint("");
            yield return new WaitForSeconds(0.35f);

            var spot = zones[shotSpot];
            // A saved shot finishes in the keeper's gloves just off the line rather
            // than in the net.
            Vector3 target = saved ? spot.AimPoint + new Vector3(0f, 0f, -0.95f) : spot.AimPoint;
            yield return RunUpAndStrike(target, saved);

            yield return new WaitForSeconds(0.6f);
            shootout.Record(!saved);
            RefreshScoreboard();

            if (saved)
            {
                audio_?.PlayMiss();
                hud?.SetPrompt("SAVED!", keeperSide.Colour, $"What a save by {Capitalise(keeperSide.Name)}!");
            }
            else
            {
                audio_?.PlayGoal();
                hud?.SetPrompt("GOAL!", shooterSide.Colour, $"{Capitalise(shooterSide.Name)} scores!");
            }

            yield return new WaitForSeconds(2.0f);

            if (shootout.Winner >= 0) EndDuel();
            else BeginKick();
        }

        /// <summary>
        /// The striker runs in from his mark and strikes. The ball only leaves the
        /// spot once the boot reaches it in the kick animation, and the keeper goes
        /// at the same moment, so kick, shot and dive read as one action.
        /// </summary>
        IEnumerator RunUpAndStrike(Vector3 target, bool saved)
        {
            if (striker != null)
            {
                Vector3 spot = ball != null ? ball.transform.position : Vector3.zero;
                Vector3 plant = spot + new Vector3(-0.42f, 0f, -0.62f);
                // Face where the shot is going, not just downfield.
                Vector3 lookAt = new Vector3(target.x, 0f, target.z) - new Vector3(plant.x, 0f, plant.z);
                Quaternion facing = lookAt.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(lookAt)
                    : strikerHomeRotation;

                strikerAnimator?.SetTrigger(RunTrigger);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / RunUpSeconds;
                    float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                    striker.position = Vector3.Lerp(strikerHome, plant, eased);
                    striker.rotation = Quaternion.Slerp(strikerHomeRotation, facing, eased);
                    yield return null;
                }

                striker.position = plant;
                striker.rotation = facing;
                strikerAnimator?.SetTrigger(KickTrigger);

                yield return WaitForKickContact();
            }

            audio_?.PlayKick();
            ball?.Strike(target, saved);
            keeper?.Dive(zones[diveSpot].AimPoint, Shootout.IsHigh(diveSpot));
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

        /// <summary>Put the striker back on his mark for the next kick.</summary>
        void ResetStriker()
        {
            if (striker == null) return;
            striker.position = strikerHome;
            striker.rotation = strikerHomeRotation;
        }

        // ---------------------------------------------------------------- spots

        void OpenSpots(Color colour)
        {
            framer?.SetAimView(true);
            pickOpensAt = Time.time + PickGuardSeconds;
            foreach (var zone in zones)
            {
                zone.SetVisible(true);
                zone.ResetVisual();
                zone.SetInteractable(true, Tint(colour, 0.55f));
            }
        }

        void CloseSpots()
        {
            foreach (var zone in zones) zone.SetInteractable(false);
        }

        void HideSpots()
        {
            foreach (var zone in zones)
            {
                zone.SetInteractable(false);
                zone.SetVisible(false);
            }
        }

        static Color Tint(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        /// <summary>"PLAYER 1" reads as a shout in a sentence; "Player 1" does not.</summary>
        static string Capitalise(string name) =>
            name.Length == 0 ? name : name[0] + name.Substring(1).ToLowerInvariant();

        // ---------------------------------------------------------------- scoreboard

        /// <summary>
        /// Five kicks a row, like a TV graphic. In sudden death the window slides on
        /// so the newest round is always on screen.
        /// </summary>
        void RefreshScoreboard()
        {
            if (hud == null) return;
            int slots = Mathf.Max(1, hud.MarkSlots);
            int lastRound = shootout.Winner >= 0 ? shootout.Taken(0) - 1 : shootout.Round;
            int firstRound = Mathf.Max(0, lastRound - slots + 1);
            for (int i = 0; i < 2; i++)
                hud.SetKicks(i, shootout.Kicks(i), firstRound, shootout.Goals(i));
        }

        public void Bind(BallController ballController, GoalkeeperController goalkeeper,
            TargetZone[] targetZones, HudController hudController, Transform strikerTransform,
            CameraFramer cameraFramer, MatchAudio matchAudio)
        {
            ball = ballController;
            keeper = goalkeeper;
            zones = targetZones;
            hud = hudController;
            striker = strikerTransform;
            framer = cameraFramer;
            audio_ = matchAudio;
        }
    }
}
