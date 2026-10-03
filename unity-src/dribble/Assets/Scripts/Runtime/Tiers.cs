using UnityEngine;

namespace Dribble
{
    public enum Difficulty { Starter, Easy, Medium, Hard }

    /// <summary>How one difficulty level plays.</summary>
    public readonly struct TierSettings
    {
        /// <summary>Running speed at kick-off, in metres per second.</summary>
        public readonly float StartSpeed;
        /// <summary>How much faster the run gets each second.</summary>
        public readonly float Acceleration;
        public readonly float MaxSpeed;
        /// <summary>Seconds between rows of obstacles, so the gap feels the same at any speed.</summary>
        public readonly float RowGapSeconds;
        /// <summary>Chance a row blocks two lanes rather than one. One lane is always left open.</summary>
        public readonly float DoubleBlockChance;
        /// <summary>Share of blockers that are defenders rather than cones.</summary>
        public readonly float DefenderShare;

        public TierSettings(float startSpeed, float acceleration, float maxSpeed,
            float rowGapSeconds, float doubleBlockChance, float defenderShare)
        {
            StartSpeed = startSpeed;
            Acceleration = acceleration;
            MaxSpeed = maxSpeed;
            RowGapSeconds = rowGapSeconds;
            DoubleBlockChance = doubleBlockChance;
            DefenderShare = defenderShare;
        }

        /// <summary>Speed after running for <paramref name="seconds"/>.</summary>
        public float SpeedAt(float seconds) =>
            Mathf.Min(MaxSpeed, StartSpeed + Acceleration * Mathf.Max(0f, seconds));
    }

    public static class Tiers
    {
        public static readonly Difficulty[] All =
        {
            Difficulty.Starter, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard
        };

        public static readonly string[] Names = { "Starter", "Easy", "Medium", "Hard" };

        public static readonly string[] Hints = { "nice and slow", "a bit quicker", "busy pitch", "super fast" };

        /// <summary>
        /// Starter is built for a six year old: a jog, one blocker at a time, and
        /// plenty of time between rows. Hard is for the big kids.
        /// </summary>
        public static TierSettings For(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Starter => new TierSettings(4.5f, 0.04f, 7.5f, 2.5f, 0f, 0.5f),
            Difficulty.Easy => new TierSettings(5.5f, 0.07f, 9.5f, 2.1f, 0.15f, 0.6f),
            Difficulty.Medium => new TierSettings(7f, 0.11f, 12.5f, 1.75f, 0.35f, 0.7f),
            _ => new TierSettings(8.5f, 0.16f, 15.5f, 1.45f, 0.55f, 0.75f)
        };
    }
}
