using UnityEngine;

namespace Keeper
{
    /// <summary>One difficulty tier: how many spots the striker can aim at and how much warning the keeper gets.</summary>
    public readonly struct Level
    {
        public readonly string Name;
        public readonly string Hint;
        /// <summary>How many of <see cref="Goal.Spots"/> are in play, taken from the front of the list.</summary>
        public readonly int SpotCount;
        /// <summary>How long the target spot glows before it goes dark.</summary>
        public readonly float TellSeconds;
        /// <summary>The striker's run from his mark to the ball.</summary>
        public readonly float RunSeconds;
        /// <summary>Ball flight from the boot to the goal line.</summary>
        public readonly float FlightSeconds;

        public Level(string name, string hint, int spotCount, float tellSeconds, float runSeconds, float flightSeconds)
        {
            Name = name;
            Hint = hint;
            SpotCount = spotCount;
            TellSeconds = tellSeconds;
            RunSeconds = runSeconds;
            FlightSeconds = flightSeconds;
        }

        public bool HasHighSpots => SpotCount > 3;
    }

    public static class Levels
    {
        // Starter is for the youngest: the spot glows for the whole run-up and the
        // ball floats. Each step up shortens the glow and speeds the shot, and from
        // Medium the striker can go for the top corners too.
        public static readonly Level[] All =
        {
            new Level("Starter", "3 spots · slow", 3, 1.6f, 1.6f, 1.25f),
            new Level("Easy", "3 spots", 3, 0.9f, 1.05f, 0.95f),
            new Level("Medium", "5 spots", 5, 0.6f, 0.85f, 0.8f),
            new Level("Hard", "5 spots · fast", 5, 0.35f, 0.7f, 0.62f),
        };
    }

    /// <summary>
    /// The goal and the spots a shot can go to, in world space. The goal line is
    /// z = 0, the mouth faces +z toward the penalty spot, and x runs left to right
    /// as the keeper (and the camera behind him) sees it.
    /// </summary>
    public static class Goal
    {
        public const float Width = 7.32f;
        public const float Height = 2.44f;
        public const float PenaltySpotZ = 11f;

        public const int Left = 0, Centre = 1, Right = 2, HighLeft = 3, HighRight = 4;

        /// <summary>Low left, centre, low right, then the two top corners.</summary>
        public static readonly Vector3[] Spots =
        {
            new Vector3(-2.55f, 0.5f, 0f),
            new Vector3(0f, 1.05f, 0f),
            new Vector3(2.55f, 0.5f, 0f),
            new Vector3(-2.75f, 1.9f, 0f),
            new Vector3(2.75f, 1.9f, 0f),
        };

        public static readonly string[] SpotNames =
        {
            "bottom left", "middle", "bottom right", "top left", "top right"
        };

        public static bool IsHigh(int spot) => spot == HighLeft || spot == HighRight;

        /// <summary>-1 for the left side, 1 for the right, 0 for the middle.</summary>
        public static int Side(int spot) => spot == Centre ? 0 : (spot == Left || spot == HighLeft) ? -1 : 1;

        /// <summary>A high choice on a level without high spots becomes the low spot on that side.</summary>
        public static int Clamp(int spot, int spotCount)
        {
            if (spot < spotCount) return spot;
            return spot == HighLeft ? Left : spot == HighRight ? Right : Centre;
        }
    }

    /// <summary>
    /// Turns the player's gestures into a spot. Kept free of Unity input so the
    /// mapping can be checked headlessly by VerifyScene.
    /// </summary>
    public static class DiveInput
    {
        /// <summary>
        /// A swipe picks a side by its horizontal direction; a mostly vertical swipe
        /// means the middle. When the top corners are in play, a swipe that climbs
        /// as it goes sideways goes high.
        /// </summary>
        public static int FromSwipe(Vector2 delta, int spotCount)
        {
            float ax = Mathf.Abs(delta.x), ay = Mathf.Abs(delta.y);
            if (ax < 0.6f * ay) return Goal.Centre;

            bool high = spotCount > 3 && delta.y > 0.45f * ax;
            if (delta.x < 0f) return high ? Goal.HighLeft : Goal.Left;
            return high ? Goal.HighRight : Goal.Right;
        }

        /// <summary>
        /// A tap dives for the spot nearest to it on screen. Height counts for less
        /// than side, so a tap anywhere toward the left still means left.
        /// </summary>
        public static int FromTap(Vector2 tap, Vector2[] spotsOnScreen, int spotCount)
        {
            int best = Goal.Centre;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < spotCount && i < spotsOnScreen.Length; i++)
            {
                Vector2 d = tap - spotsOnScreen[i];
                float distance = d.x * d.x + 0.45f * d.y * d.y;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }
    }
}
