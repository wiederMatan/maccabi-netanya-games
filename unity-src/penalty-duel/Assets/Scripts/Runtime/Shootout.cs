using System.Collections.Generic;

namespace PenaltyDuel
{
    /// <summary>
    /// The rules of a penalty shootout, with no Unity in them so they can be checked
    /// headlessly. Player 0 shoots first in every round; each side takes five, the
    /// shootout ends as soon as one side cannot be caught, and a level score after
    /// five goes to sudden death one round at a time.
    /// </summary>
    public class Shootout
    {
        public const int Regulation = 5;

        // The goal is split into a 3 x 2 grid: index 0-2 is the top row left to
        // right (as seen from behind the taker), 3-5 the bottom row.
        public const int Spots = 6;
        public const int Columns = 3;

        readonly List<bool>[] kicks = { new List<bool>(), new List<bool>() };

        public IReadOnlyList<bool> Kicks(int player) => kicks[player];
        public int Taken(int player) => kicks[player].Count;

        public int Goals(int player)
        {
            int goals = 0;
            foreach (bool scored in kicks[player]) if (scored) goals++;
            return goals;
        }

        /// <summary>Who takes the next kick: player 0 unless they are a kick ahead.</summary>
        public int Shooter => kicks[0].Count > kicks[1].Count ? 1 : 0;

        /// <summary>Zero-based round of the next kick.</summary>
        public int Round => kicks[Shooter].Count;

        public bool SuddenDeath => Round >= Regulation;

        public void Record(bool scored)
        {
            if (Winner >= 0) return;
            kicks[Shooter].Add(scored);
        }

        /// <summary>The winning player, or -1 while the shootout is still alive.</summary>
        public int Winner
        {
            get
            {
                int taken0 = Taken(0), taken1 = Taken(1);
                int goals0 = Goals(0), goals1 = Goals(1);

                if (taken0 <= Regulation && taken1 <= Regulation)
                {
                    // Decided early once one side could not catch up even by
                    // scoring every kick they have left.
                    int left0 = Regulation - taken0, left1 = Regulation - taken1;
                    if (goals0 + left0 < goals1) return 1;
                    if (goals1 + left1 < goals0) return 0;
                    return -1;
                }

                // Sudden death: only a completed round can settle it.
                if (taken0 == taken1 && goals0 != goals1) return goals0 > goals1 ? 0 : 1;
                return -1;
            }
        }

        public static int Column(int spot) => spot % Columns;
        public static bool IsHigh(int spot) => spot < Columns;

        /// <summary>
        /// Does the keeper's dive stop the shot? Diving to the exact spot always
        /// saves it. Diving the right way but at the wrong height gets a hand to it
        /// half the time (<paramref name="roll"/> is a 0-1 random). The wrong way
        /// never saves.
        /// </summary>
        public static bool IsSave(int shotSpot, int diveSpot, float roll)
        {
            if (Column(shotSpot) != Column(diveSpot)) return false;
            if (shotSpot == diveSpot) return true;
            return roll < 0.5f;
        }
    }
}
