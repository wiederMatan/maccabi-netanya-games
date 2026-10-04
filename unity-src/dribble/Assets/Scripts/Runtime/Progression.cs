using UnityEngine;

namespace Dribble
{
    /// <summary>How busy the pitch is at one moment of a run.</summary>
    public readonly struct Pace
    {
        /// <summary>Seconds between rows of obstacles, so the gap feels the same at any speed.</summary>
        public readonly float RowGapSeconds;
        /// <summary>Chance a row blocks two lanes rather than one. One lane is always left open.</summary>
        public readonly float DoubleBlockChance;
        /// <summary>Share of blockers that are defenders rather than cones.</summary>
        public readonly float DefenderShare;

        public Pace(float rowGapSeconds, float doubleBlockChance, float defenderShare)
        {
            RowGapSeconds = rowGapSeconds;
            DoubleBlockChance = doubleBlockChance;
            DefenderShare = defenderShare;
        }
    }

    /// <summary>
    /// One progression for every run, no levels. The run kicks off at a brisk
    /// jog and speeds up steadily to a sprint within about a minute; as it does,
    /// rows of obstacles come closer together and more of them block two lanes,
    /// so the opening is gentle and the end is hard.
    /// </summary>
    public static class Progression
    {
        public const float StartSpeed = 5.5f;
        public const float MaxSpeed = 15f;
        /// <summary>Metres per second gained each second: top speed after 65 s.</summary>
        public const float Acceleration = (MaxSpeed - StartSpeed) / 65f;

        // At kick-off: one blocker at a time, well spaced, half of them cones.
        static readonly Pace Calm = new Pace(2.5f, 0f, 0.5f);
        // At top speed: tighter rows, more than half blocking two lanes, mostly defenders.
        static readonly Pace Frantic = new Pace(1.45f, 0.55f, 0.75f);

        /// <summary>
        /// Scores for the second and third star at the end of a run; every finished
        /// run earns the first. Score is metres plus 10 per star collected: a kid who
        /// lasts half a minute and picks up some stars passes 250, and a good run of
        /// three-quarters of a minute or more, with stars, passes 650.
        /// </summary>
        public const int TwoStarScore = 250;
        public const int ThreeStarScore = 650;

        public static float SpeedAt(float seconds) =>
            Mathf.Min(MaxSpeed, StartSpeed + Acceleration * Mathf.Max(0f, seconds));

        /// <summary>0 at kick-off speed, 1 at top speed.</summary>
        public static float Intensity(float speed) => Mathf.InverseLerp(StartSpeed, MaxSpeed, speed);

        public static Pace PaceAt(float speed)
        {
            float t = Intensity(speed);
            return new Pace(
                Mathf.Lerp(Calm.RowGapSeconds, Frantic.RowGapSeconds, t),
                Mathf.Lerp(Calm.DoubleBlockChance, Frantic.DoubleBlockChance, t),
                Mathf.Lerp(Calm.DefenderShare, Frantic.DefenderShare, t));
        }

        /// <summary>1 to 3 stars for a run that scored <paramref name="score"/>.</summary>
        public static int StarsFor(int score) =>
            score >= ThreeStarScore ? 3 : score >= TwoStarScore ? 2 : 1;
    }
}
