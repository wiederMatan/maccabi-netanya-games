namespace Dribble
{
    public enum Blocker { None, Defender, Cone }

    /// <summary>One row across the pitch: what stands in each lane, and where the stars go.</summary>
    public struct CourseRow
    {
        public Blocker[] Lanes;
        /// <summary>Open lane that gets a short line of stars through the row, or -1.</summary>
        public int StarLane;
        /// <summary>Lane for a bonus line of stars half way to the next row, or -1.</summary>
        public int BonusStarLane;
    }

    /// <summary>
    /// Decides what goes in each row. Pure logic with no scene access, so
    /// BuildAndVerify can run thousands of rows to prove that a lane is always left
    /// open and that a star never sits on top of a blocker.
    /// </summary>
    public static class CourseGenerator
    {
        public const int LaneCount = 3;
        const float StarRowChance = 0.75f;
        const float BonusStarChance = 0.4f;

        public static CourseRow Next(Pace tier, System.Random random)
        {
            var row = new CourseRow { Lanes = new Blocker[LaneCount], StarLane = -1, BonusStarLane = -1 };

            int open = random.Next(LaneCount);
            int blocked = random.NextDouble() < tier.DoubleBlockChance ? 2 : 1;

            // Fill lanes other than the open one, starting from a random one of them.
            int first = (open + 1 + random.Next(LaneCount - 1)) % LaneCount;
            for (int i = 0; i < blocked; i++)
            {
                int lane = i == 0 ? first : 3 - open - first;
                row.Lanes[lane] = random.NextDouble() < tier.DefenderShare ? Blocker.Defender : Blocker.Cone;
            }

            if (random.NextDouble() < StarRowChance)
            {
                // With two open lanes, sometimes reward the one that needs a move.
                int starLane = open;
                if (blocked == 1 && random.NextDouble() < 0.5)
                    starLane = 3 - open - first;
                row.StarLane = starLane;
            }

            if (random.NextDouble() < BonusStarChance)
                row.BonusStarLane = random.Next(LaneCount);

            return row;
        }
    }
}
