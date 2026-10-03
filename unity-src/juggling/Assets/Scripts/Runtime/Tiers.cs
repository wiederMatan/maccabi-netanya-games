namespace Juggling
{
    /// <summary>
    /// One difficulty level. Everything that makes the game harder lives here, so
    /// the four levels can be compared - and checked by VerifyScene - side by side.
    /// </summary>
    public struct Tier
    {
        public string Name;
        public string Hint;

        /// <summary>Radius of the ball, in metres.</summary>
        public float BallRadius;

        /// <summary>How far outside the ball a tap still counts, as a multiple of its radius.</summary>
        public float HitScale;

        /// <summary>Gravity on the first touch, and once the ramp is complete.</summary>
        public float GravityStart;
        public float GravityEnd;

        /// <summary>Touches it takes to go from GravityStart to GravityEnd.</summary>
        public int RampTouches;

        /// <summary>Sideways speed for a tap right on the edge of the hit area, in m/s.</summary>
        public float Drift;

        /// <summary>The top of each kick lands somewhere in this band of heights.</summary>
        public float ApexMin;
        public float ApexMax;

        public float GravityAt(int touches)
        {
            float t = RampTouches <= 0 ? 1f : UnityEngine.Mathf.Clamp01(touches / (float)RampTouches);
            return UnityEngine.Mathf.Lerp(GravityStart, GravityEnd, t);
        }
    }

    public static class Tiers
    {
        // Starter is for the youngest players: a big, slow, floaty ball that barely
        // drifts, so every tap near it is a touch. Hard is a real ball's worth of
        // pace and a small target that slides about.
        public static readonly Tier[] All =
        {
            new Tier
            {
                Name = "Starter", Hint = "big & slow",
                BallRadius = 0.50f, HitScale = 1.8f,
                GravityStart = 3.2f, GravityEnd = 6.0f, RampTouches = 40,
                Drift = 0.5f, ApexMin = 3.3f, ApexMax = 4.2f
            },
            new Tier
            {
                Name = "Easy", Hint = "a bit quicker",
                BallRadius = 0.43f, HitScale = 1.65f,
                GravityStart = 4.0f, GravityEnd = 7.5f, RampTouches = 35,
                Drift = 0.9f, ApexMin = 3.3f, ApexMax = 4.3f
            },
            new Tier
            {
                Name = "Medium", Hint = "faster, drifts",
                BallRadius = 0.36f, HitScale = 1.5f,
                GravityStart = 5.0f, GravityEnd = 9.5f, RampTouches = 30,
                Drift = 1.4f, ApexMin = 3.3f, ApexMax = 4.4f
            },
            new Tier
            {
                Name = "Hard", Hint = "small & speedy",
                BallRadius = 0.30f, HitScale = 1.35f,
                GravityStart = 6.5f, GravityEnd = 10.5f, RampTouches = 25,
                Drift = 2.0f, ApexMin = 3.3f, ApexMax = 4.5f
            }
        };
    }
}
