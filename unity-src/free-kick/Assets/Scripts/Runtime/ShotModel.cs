using UnityEngine;

namespace FreeKick
{
    /// <summary>Pitch measurements shared by the scene builder and the game, in metres.</summary>
    public static class Pitch
    {
        public const float GoalWidth = 7.32f;
        public const float GoalHeight = 2.44f;
        public const float GoalLineZ = 16f;
        public const float PostRadius = 0.08f;
        public const float BallRadius = 0.11f;
        public const float WallDistance = 9.15f;
        public const float PlayerHeight = 1.82f;
        // Shoulder to shoulder, so a wall of n players is n of these wide.
        public const float WallSpacing = 0.52f;
        // The keeper meets the ball a little in front of his line.
        public const float KeeperPlaneZ = GoalLineZ - 0.45f;

        /// <summary>Centre of a bonus ring in a top corner; side is -1 (left) or +1 (right).</summary>
        public static Vector2 RingCentre(int side, float radius) => new Vector2(
            side * (GoalWidth / 2f - PostRadius - radius - 0.04f),
            GoalHeight - PostRadius - radius - 0.04f);
    }

    public enum Level { Starter, Easy, Medium, Hard }

    /// <summary>Everything a difficulty level changes, in one place.</summary>
    public readonly struct LevelSettings
    {
        public readonly string Name;
        public readonly string Hint;
        public readonly int WallCount;
        public readonly float WallScale;      // of a full grown player
        public readonly float WallJump;       // metres the wall jumps at the kick
        public readonly float KeeperSkill;    // 0..1, chance of saving one he can reach
        public readonly float RingRadius;
        public readonly float AimAssist;      // 0..1, how far a wayward aim is pulled into the goal
        public readonly float MaxWind;        // metres of drift at the goal line
        public readonly float GuideLength;    // how much of the flight the aim guide shows
        public readonly float MinDistance, MaxDistance, MaxSide;

        public LevelSettings(string name, string hint, int wallCount, float wallScale, float wallJump,
            float keeperSkill, float ringRadius, float aimAssist, float maxWind, float guideLength,
            float minDistance, float maxDistance, float maxSide)
        {
            Name = name; Hint = hint; WallCount = wallCount; WallScale = wallScale; WallJump = wallJump;
            KeeperSkill = keeperSkill; RingRadius = ringRadius; AimAssist = aimAssist; MaxWind = maxWind;
            GuideLength = guideLength; MinDistance = minDistance; MaxDistance = maxDistance; MaxSide = maxSide;
        }

        public static readonly LevelSettings[] All =
        {
            new LevelSettings("Starter", "small wall", 2, 0.70f, 0f, 0.12f, 0.72f, 1f, 0f, 1f, 17f, 18f, 1.5f),
            new LevelSettings("Easy", "3 in the wall", 3, 0.84f, 0f, 0.25f, 0.60f, 0.55f, 0f, 1f, 18f, 20f, 3f),
            new LevelSettings("Medium", "4 in the wall", 4, 1f, 0.15f, 0.40f, 0.48f, 0.2f, 0f, 0.75f, 19f, 23f, 5f),
            new LevelSettings("Hard", "5 + wind", 5, 1f, 0.32f, 0.55f, 0.38f, 0f, 1.4f, 0.55f, 20f, 25f, 6f),
        };

        public static LevelSettings For(Level level) => All[(int)level];
    }

    /// <summary>
    /// One struck ball: a ballistic arc from the spot to a point on the goal line,
    /// bowed sideways by the bend on the swipe and pushed along by any wind. The
    /// flight is scripted rather than simulated so the aim guide can show exactly
    /// where the ball will go, and so the outcome can be decided the moment it is struck.
    /// </summary>
    public readonly struct ShotPath
    {
        public const float MaxBend = 2.6f;

        public readonly Vector3 Start;
        public readonly Vector3 Target;     // where it crosses the goal line, before wind
        public readonly float FlightTime;
        public readonly float Bend;         // sideways bow at mid-flight, metres (+ = right)
        public readonly float Wind;         // sideways drift by the goal line, metres

        public ShotPath(Vector3 start, Vector3 target, float flightTime, float bend, float wind)
        {
            Start = start; Target = target; FlightTime = flightTime; Bend = bend; Wind = wind;
        }

        /// <summary>
        /// Build a shot from what the swipe said: where along the goal line it points,
        /// how hard (0..1) and how much bend (-1..1). Power sets the height it crosses
        /// the line at, and a little of the pace.
        /// </summary>
        public static ShotPath FromSwipe(Vector3 start, float aimX, float power, float curve,
            in LevelSettings level, float wind)
        {
            power = Mathf.Clamp01(power);
            var target = new Vector3(aimX, Mathf.Lerp(0.15f, 3.5f, power), Pitch.GoalLineZ);
            float bend = Mathf.Clamp(curve, -1f, 1f) * MaxBend;

            // Kind to small children: pull a wild aim back toward the frame. The bend
            // carries the ball across too, so it is allowed for when pulling it in.
            if (level.AimAssist > 0f)
            {
                float halfWidth = Pitch.GoalWidth / 2f - 0.55f;
                var inside = new Vector3(
                    Mathf.Clamp(target.x, -halfWidth, halfWidth),
                    Mathf.Clamp(target.y, 0.25f, Pitch.GoalHeight - 0.4f),
                    target.z);
                target = Vector3.Lerp(target, inside, level.AimAssist);
            }

            float distance = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(target.x, 0f, target.z));
            float time = Mathf.Clamp(distance / 21f, 0.7f, 1.3f) * Mathf.Lerp(0.88f, 1.08f, power);
            return new ShotPath(start, target, time, bend, wind);
        }

        /// <summary>Position at <paramref name="s"/> = 0 (struck) .. 1 (goal line).</summary>
        public Vector3 Evaluate(float s, bool withWind = true)
        {
            float g = -Physics.gravity.y;
            float x = Mathf.Lerp(Start.x, Target.x, s) + Bend * 4f * s * (1f - s) + (withWind ? Wind * s * s : 0f);
            float y = Mathf.Lerp(Start.y, Target.y, s) + 0.5f * g * FlightTime * FlightTime * s * (1f - s);
            float z = Mathf.Lerp(Start.z, Target.z, s);
            return new Vector3(x, y, z);
        }

        /// <summary>Velocity at <paramref name="s"/>, for handing the ball over to physics.</summary>
        public Vector3 Velocity(float s)
        {
            float g = -Physics.gravity.y;
            float dx = Target.x - Start.x + Bend * 4f * (1f - 2f * s) + Wind * 2f * s;
            float dy = Target.y - Start.y + 0.5f * g * FlightTime * FlightTime * (1f - 2f * s);
            float dz = Target.z - Start.z;
            return new Vector3(dx, dy, dz) / FlightTime;
        }

        /// <summary>The s at which the ball reaches depth <paramref name="z"/>.</summary>
        public float AtDepth(float z) => Mathf.Approximately(Target.z, Start.z) ? 1f : (z - Start.z) / (Target.z - Start.z);

        public Vector3 Crossing => Evaluate(1f);
    }

    public enum ShotResult { Goal, Saved, Blocked, Woodwork, Over, Wide }

    public readonly struct Outcome
    {
        public readonly ShotResult Result;
        public readonly float At;          // s along the path where it happens
        public readonly int Ring;          // -1 none, 0 left, 1 right
        public readonly int WallIndex;     // which wall player blocked it, -1 if none

        public Outcome(ShotResult result, float at, int ring = -1, int wallIndex = -1)
        {
            Result = result; At = at; Ring = ring; WallIndex = wallIndex;
        }

        public bool IsGoal => Result == ShotResult.Goal;
    }

    /// <summary>Where the wall and keeper stand for one kick.</summary>
    public readonly struct Defence
    {
        public readonly float WallCentreX;
        public readonly float WallZ;
        public readonly int WallCount;
        public readonly float WallTop;     // highest point of the wall when it meets the ball
        public readonly float KeeperX;
        public readonly float KeeperSkill;
        public readonly float RingRadius;

        public Defence(float wallCentreX, float wallZ, int wallCount, float wallTop,
            float keeperX, float keeperSkill, float ringRadius)
        {
            WallCentreX = wallCentreX; WallZ = wallZ; WallCount = wallCount; WallTop = wallTop;
            KeeperX = keeperX; KeeperSkill = keeperSkill; RingRadius = ringRadius;
        }

        public float WallLeft => WallCentreX - WallCount * Pitch.WallSpacing / 2f;
        public float WallRight => WallCentreX + WallCount * Pitch.WallSpacing / 2f;

        /// <summary>X of wall player <paramref name="i"/>, left to right.</summary>
        public float WallPlayerX(int i) => WallLeft + (i + 0.5f) * Pitch.WallSpacing;
    }

    /// <summary>Decides what a shot does: wall, woodwork, keeper, goal or off target.</summary>
    public static class ShotJudge
    {
        /// <summary>
        /// <paramref name="roll"/> is a 0..1 random number for the keeper, passed in so
        /// a check can make the keeper certain to save or certain to miss.
        /// </summary>
        public static Outcome Judge(in ShotPath path, in Defence defence, float roll)
        {
            // The wall first: it is the only thing between the spot and the goal.
            if (defence.WallCount > 0)
            {
                float s = path.AtDepth(defence.WallZ);
                if (s > 0f && s < 1f)
                {
                    var p = path.Evaluate(s);
                    bool across = p.x > defence.WallLeft - Pitch.BallRadius && p.x < defence.WallRight + Pitch.BallRadius;
                    if (across && p.y < defence.WallTop + Pitch.BallRadius * 0.5f)
                    {
                        int index = Mathf.Clamp(Mathf.FloorToInt((p.x - defence.WallLeft) / Pitch.WallSpacing),
                            0, defence.WallCount - 1);
                        return new Outcome(ShotResult.Blocked, s, wallIndex: index);
                    }
                }
            }

            var c = path.Crossing;
            float half = Pitch.GoalWidth / 2f;
            float reach = Pitch.PostRadius + Pitch.BallRadius;

            bool underBar = c.y < Pitch.GoalHeight - Pitch.PostRadius;
            bool betweenPosts = Mathf.Abs(c.x) < half - Pitch.PostRadius;

            if (underBar && betweenPosts)
            {
                int ring = RingAt(c, defence.RingRadius);
                if (KeeperSaves(c, defence, ring, roll))
                    return new Outcome(ShotResult.Saved, path.AtDepth(Pitch.KeeperPlaneZ), ring);
                return new Outcome(ShotResult.Goal, 1f, ring);
            }

            bool onPost = Mathf.Abs(Mathf.Abs(c.x) - half) < reach && c.y < Pitch.GoalHeight + reach;
            bool onBar = Mathf.Abs(c.y - Pitch.GoalHeight) < reach && Mathf.Abs(c.x) < half + reach;
            if (onPost || onBar) return new Outcome(ShotResult.Woodwork, 1f);

            return new Outcome(c.y >= Pitch.GoalHeight ? ShotResult.Over : ShotResult.Wide, 1f);
        }

        public static int RingAt(Vector3 crossing, float radius)
        {
            for (int i = 0; i < 2; i++)
            {
                var centre = Pitch.RingCentre(i == 0 ? -1 : 1, radius);
                if (Vector2.Distance(new Vector2(crossing.x, crossing.y), centre) <= radius) return i;
            }
            return -1;
        }

        /// <summary>
        /// The chance the keeper gets there. He covers what is near him well and the
        /// far top corner badly - so a ring shot is the hardest to keep out.
        /// </summary>
        public static float SaveChance(Vector3 crossing, in Defence defence, int ring)
        {
            float distance = Mathf.Abs(crossing.x - defence.KeeperX);
            float chance = defence.KeeperSkill * Mathf.Clamp01(1.25f - distance / 3.2f);
            if (crossing.y > 1.8f && distance > 1.8f) chance *= 0.6f;
            if (ring >= 0) chance *= 0.45f;
            return chance;
        }

        static bool KeeperSaves(Vector3 crossing, in Defence defence, int ring, float roll) =>
            roll < SaveChance(crossing, defence, ring);
    }
}
