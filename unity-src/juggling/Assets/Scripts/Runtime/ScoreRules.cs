namespace Juggling
{
    /// <summary>
    /// How touches turn into points. Every touch scores; a "clean" touch - one
    /// struck near the middle of the ball - builds the combo, and a long enough
    /// combo makes each touch worth double. An off-centre touch still counts, it
    /// just starts the combo again.
    /// </summary>
    public static class ScoreRules
    {
        /// <summary>A tap this close to the middle (as a fraction of the hit radius) is clean.</summary>
        public const float CleanOffset = 0.45f;

        /// <summary>From this many clean touches in a row, each touch is worth two.</summary>
        public const int DoublePointsCombo = 5;

        /// <summary>Scores the crowd celebrates.</summary>
        public static readonly int[] Milestones = { 10, 25, 50, 75, 100, 150, 200 };

        public static bool IsClean(float offset) => offset >= -CleanOffset && offset <= CleanOffset;

        public static int PointsFor(int combo) => combo >= DoublePointsCombo ? 2 : 1;

        /// <summary>The milestone passed going from one score to the next, or 0.</summary>
        public static int MilestoneCrossed(int before, int after)
        {
            foreach (int milestone in Milestones)
                if (before < milestone && after >= milestone) return milestone;
            return 0;
        }
    }
}
