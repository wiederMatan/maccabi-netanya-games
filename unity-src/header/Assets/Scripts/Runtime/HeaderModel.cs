using UnityEngine;

namespace Headers
{
    /// <summary>Pitch measurements shared by the scene builder and the game, in metres.</summary>
    public static class Field
    {
        public const float GoalWidth = 7.32f;
        public const float GoalHeight = 2.44f;
        public const float GoalLineZ = 16f;
        public const float PostRadius = 0.08f;
        public const float BallRadius = 0.13f;
        public const float PlayerHeight = 1.82f;
        /// <summary>Height of the forehead of a player standing flat-footed.</summary>
        public const float StandingHead = 1.72f;
        /// <summary>How high a jump lifts him at the top of it.</summary>
        public const float JumpHeight = 0.5f;
        /// <summary>Seconds from leaving the grass to landing again.</summary>
        public const float JumpSeconds = 0.62f;
        /// <summary>The keeper meets the ball a little in front of his line.</summary>
        public const float KeeperLineZ = GoalLineZ - 0.4f;
        /// <summary>A header is aimed no wider than this, so a good one is always on target.</summary>
        public const float AimLimit = GoalWidth / 2f - 0.4f;
        public static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        /// <summary>Where the corner is swung in from; side is -1 (left) or +1 (right).</summary>
        public static Vector3 CornerSpot(int side) => new Vector3(side * 12.5f, BallRadius, GoalLineZ - 0.6f);

        /// <summary>Forehead height <paramref name="sinceJump"/> seconds after he took off.</summary>
        public static float HeadHeight(float sinceJump)
        {
            if (sinceJump <= 0f || sinceJump >= JumpSeconds) return StandingHead;
            float u = sinceJump / JumpSeconds;
            return StandingHead + 4f * JumpHeight * u * (1f - u);
        }
    }

    /// <summary>Everything that changes from one corner of a round to the next.</summary>
    public readonly struct CornerSettings
    {
        /// <summary>Seconds from the corner being struck to the ball reaching the meeting point.</summary>
        public readonly float CrossTime;
        /// <summary>Seconds either side of the perfect jump that still win the header.</summary>
        public readonly float Tolerance;
        public readonly float KeeperReaction;
        /// <summary>Metres per second the keeper covers across his goal once he moves.</summary>
        public readonly float KeeperSpeed;
        /// <summary>How far off centre the keeper may stand, cheating toward one post.</summary>
        public readonly float KeeperShift;
        /// <summary>How far across the box the ball may be swung in.</summary>
        public readonly float MeetSpread;
        public readonly float MeetDistance;
        public readonly bool Defender;
        /// <summary>A header weaker than this loses the ball to the defender.</summary>
        public readonly float DefenderBeats;

        public CornerSettings(float crossTime, float tolerance, float keeperReaction, float keeperSpeed,
            float keeperShift, float meetSpread, float meetDistance, bool defender, float defenderBeats)
        {
            CrossTime = crossTime; Tolerance = tolerance; KeeperReaction = keeperReaction;
            KeeperSpeed = keeperSpeed; KeeperShift = keeperShift; MeetSpread = meetSpread;
            MeetDistance = meetDistance; Defender = defender; DefenderBeats = defenderBeats;
        }

        /// <summary>The perfect timing is inside this share of the tolerance.</summary>
        public float PerfectWindow => Tolerance * 0.35f;
    }

    /// <summary>
    /// One round is ten corners and there are no levels. The first few are slow,
    /// floated balls with a forgiving jump and a sleepy keeper; by the last the
    /// cross is whipped in, the timing is tight, a defender competes for every
    /// ball from the fourth corner on and the keeper covers most of his goal, so
    /// the header has to go away from him.
    /// </summary>
    public static class Progression
    {
        public const int CornersPerRound = 10;
        public const int GoalPoints = 10;
        public const int PerfectBonus = 5;
        /// <summary>Goals for the second and third star; every finished round earns the first.</summary>
        public const int TwoStarGoals = 4;
        public const int ThreeStarGoals = 7;
        public const int FirstDefender = 3;

        public static CornerSettings For(int corner)
        {
            float t = Mathf.Clamp01(corner / (float)(CornersPerRound - 1));
            return new CornerSettings(
                crossTime: Mathf.Lerp(1.55f, 1.15f, t),
                tolerance: Mathf.Lerp(0.24f, 0.13f, t),
                keeperReaction: Mathf.Lerp(0.32f, 0.17f, t),
                keeperSpeed: Mathf.Lerp(3.2f, 5.2f, t),
                keeperShift: Mathf.Lerp(0.4f, 1.2f, t),
                meetSpread: Mathf.Lerp(0.6f, 2.4f, t),
                meetDistance: Mathf.Lerp(6.5f, 8.5f, t),
                defender: corner >= FirstDefender,
                defenderBeats: Mathf.Lerp(0.25f, 0.5f, t));
        }

        public static int StarsFor(int goals) =>
            goals >= ThreeStarGoals ? 3 : goals >= TwoStarGoals ? 2 : 1;
    }

    /// <summary>
    /// A ball flying under gravity from <see cref="Start"/> so that it is at
    /// <see cref="Target"/> after <see cref="Time"/> seconds. Used for the corner
    /// swung in to the meeting point and for the header on to the goal; both are
    /// scripted rather than simulated, so the outcome is known the moment the ball
    /// is struck and the timing ring can close exactly when it should.
    /// </summary>
    public readonly struct Flight
    {
        public readonly Vector3 Start;
        public readonly Vector3 Target;
        public readonly float Time;
        public readonly Vector3 Launch;

        public Flight(Vector3 start, Vector3 target, float time)
        {
            Start = start;
            Target = target;
            Time = Mathf.Max(0.05f, time);
            Launch = (target - start - 0.5f * Field.Gravity * Time * Time) / Time;
        }

        public Vector3 At(float t) => Start + Launch * t + 0.5f * Field.Gravity * t * t;
        public Vector3 VelocityAt(float t) => Launch + Field.Gravity * t;
        public float Apex => Start.y + Mathf.Max(0f, Launch.y * Launch.y / (2f * -Field.Gravity.y));

        /// <summary>Seconds until the ball is at depth <paramref name="z"/>, or -1 if it never gets there.</summary>
        public float TimeToZ(float z)
        {
            if (Mathf.Abs(Launch.z) < 1e-4f) return -1f;
            float t = (z - Start.z) / Launch.z;
            return t >= 0f ? t : -1f;
        }
    }

    public enum HeaderResult { Goal, Saved, Over, Wide, Cleared, Missed }

    /// <summary>What happens to one corner, decided the moment the ball reaches the meeting point.</summary>
    public readonly struct HeaderOutcome
    {
        public readonly HeaderResult Result;
        /// <summary>0 for a whiff, up to 1 for a perfectly timed jump.</summary>
        public readonly float Quality;
        public readonly bool Perfect;
        /// <summary>Seconds the jump was early (+) or late (-) against the perfect moment.</summary>
        public readonly float TimingError;
        /// <summary>The header's flight to the goal line, when there is one.</summary>
        public readonly Flight Shot;
        /// <summary>Where the keeper dives to, and how much of the way he gets.</summary>
        public readonly Vector3 KeeperTarget;
        public readonly float KeeperReach;

        public HeaderOutcome(HeaderResult result, float quality, bool perfect, float timingError,
            Flight shot, Vector3 keeperTarget, float keeperReach)
        {
            Result = result; Quality = quality; Perfect = perfect; TimingError = timingError;
            Shot = shot; KeeperTarget = keeperTarget; KeeperReach = keeperReach;
        }

        public bool Headed => Result == HeaderResult.Goal || Result == HeaderResult.Saved ||
                              Result == HeaderResult.Over || Result == HeaderResult.Wide;
    }

    /// <summary>
    /// The rules of a header, kept free of the scene so they can be checked headlessly.
    /// </summary>
    public static class HeaderJudge
    {
        /// <summary>Seconds before the ball arrives that the jump should start, to meet it at the top.</summary>
        public static float PerfectLead => Field.JumpSeconds * 0.5f;

        /// <summary>
        /// Positive when the jump came early (he is already on the way down when the
        /// ball arrives), negative when late. <paramref name="jumpAge"/> is how long
        /// before the ball's arrival he took off; a negative age means he never jumped.
        /// </summary>
        public static float TimingError(float jumpAge) => jumpAge - PerfectLead;

        /// <summary>1 for a perfect jump, falling to 0 at the edge of the tolerance; 0 outside it.</summary>
        public static float Quality(float timingError, float tolerance) =>
            Mathf.Clamp01(1f - Mathf.Abs(timingError) / Mathf.Max(0.01f, tolerance));

        /// <summary>Header speed in metres per second: a good contact is struck harder.</summary>
        public static float Speed(float quality) => Mathf.Lerp(10f, 13.5f, quality);

        /// <summary>
        /// Where the header crosses the goal line. Across the goal it goes where the
        /// player tapped, give or take a little for a scrappy contact; up and down it
        /// follows the timing: early jumps get under the ball and send it high, late
        /// ones get on top of it and send it into the grass.
        /// </summary>
        public static Vector3 Target(float aimX, float timingError, float tolerance, float quality, float scatter)
        {
            float x = Mathf.Clamp(aimX, -Field.AimLimit, Field.AimLimit) + scatter * (1f - quality) * 1.1f;
            float lift = Mathf.Clamp(timingError / Mathf.Max(0.01f, tolerance), -1f, 1.2f);
            float y = Mathf.Max(Field.BallRadius, 1.1f + lift * 1.5f);
            return new Vector3(x, y, Field.GoalLineZ);
        }

        /// <summary>
        /// Decides one corner. <paramref name="jumpAge"/> is how long before the ball
        /// reached <paramref name="meet"/> the player took off (negative for no jump),
        /// <paramref name="aimX"/> where across the goal he tapped, <paramref name="keeperX"/>
        /// where the keeper stands, and <paramref name="scatter"/> a random number in
        /// -1..1 for the wobble on a scrappy header.
        /// </summary>
        public static HeaderOutcome Decide(CornerSettings settings, Vector3 meet, float jumpAge, float aimX,
            float keeperX, float scatter)
        {
            var none = default(Flight);
            var home = new Vector3(keeperX, 0f, Field.KeeperLineZ);
            if (jumpAge < 0f)
                return new HeaderOutcome(settings.Defender ? HeaderResult.Cleared : HeaderResult.Missed,
                    0f, false, float.NaN, none, home, 0f);

            float error = TimingError(jumpAge);
            float quality = Quality(error, settings.Tolerance);
            if (quality <= 0f)
                return new HeaderOutcome(settings.Defender ? HeaderResult.Cleared : HeaderResult.Missed,
                    0f, false, error, none, home, 0f);
            if (settings.Defender && quality < settings.DefenderBeats)
                return new HeaderOutcome(HeaderResult.Cleared, quality, false, error, none, home, 0f);

            bool perfect = Mathf.Abs(error) <= settings.PerfectWindow;
            var target = Target(aimX, error, settings.Tolerance, quality, scatter);
            float distance = Vector3.Distance(meet, target);
            var shot = new Flight(meet, target, distance / Speed(quality));

            if (target.y > Field.GoalHeight - Field.BallRadius)
                return new HeaderOutcome(HeaderResult.Over, quality, perfect, error, shot, KeeperGuess(home, target, 0.6f), 0.6f);
            if (Mathf.Abs(target.x) > Field.GoalWidth / 2f - Field.PostRadius - Field.BallRadius)
                return new HeaderOutcome(HeaderResult.Wide, quality, perfect, error, shot, KeeperGuess(home, target, 0.6f), 0.6f);

            // Can the keeper get across in time? He reacts, then covers ground at his
            // speed, and his arms reach a little further than his body.
            float atKeeper = shot.TimeToZ(Field.KeeperLineZ);
            if (atKeeper < 0f) atKeeper = shot.Time;
            float covers = KeeperCover(settings, atKeeper);
            float needed = Mathf.Abs(shot.At(atKeeper).x - keeperX);
            if (needed <= covers)
                return new HeaderOutcome(HeaderResult.Saved, quality, perfect, error, shot,
                    new Vector3(shot.At(atKeeper).x, shot.At(atKeeper).y, Field.KeeperLineZ), 1f);

            float reach = Mathf.Clamp01(covers / Mathf.Max(0.01f, needed)) * 0.85f;
            return new HeaderOutcome(HeaderResult.Goal, quality, perfect, error, shot,
                new Vector3(shot.At(atKeeper).x, shot.At(atKeeper).y, Field.KeeperLineZ), reach);
        }

        /// <summary>Metres across his goal the keeper can reach in <paramref name="seconds"/>.</summary>
        public static float KeeperCover(CornerSettings settings, float seconds) =>
            ArmReach + settings.KeeperSpeed * Mathf.Max(0f, seconds - settings.KeeperReaction);

        public const float ArmReach = 0.65f;

        static Vector3 KeeperGuess(Vector3 home, Vector3 target, float reach) =>
            new Vector3(Mathf.Lerp(home.x, target.x, reach), Mathf.Min(target.y, 2f), Field.KeeperLineZ);
    }
}
