using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// The ball's flight, kept free of any scene object so VerifyScene can fly
    /// thousands of kicks headlessly and prove the ball stays in the play area.
    /// The ball moves in the vertical plane z = 0: x across, y up.
    /// </summary>
    public static class BallPhysics
    {
        // Losing a bit of sideways speed off a side wall keeps a ball that is
        // pinging between the walls from staying out of reach.
        const float WallBounce = 0.75f;
        // Smallest upward speed a kick ever gives, so a ball tapped near the top
        // of its flight still visibly pops up.
        const float MinRise = 0.6f;

        /// <summary>
        /// The velocity a kick gives the ball. <paramref name="offset"/> is where the
        /// tap landed across the ball, -1 (its right edge) to 1 (its left edge):
        /// tapping the left side sends it right, as a boot would.
        /// The rise is chosen to top out at <paramref name="apex"/>, not a fixed
        /// speed, so the ball always peaks in view whatever the gravity; and never
        /// past <paramref name="maxHeight"/>, however high the ball was when struck.
        /// </summary>
        public static Vector2 Kick(Vector2 position, float offset, float apex, float maxHeight,
            float gravity, float drift)
        {
            float rise = Mathf.Min(Mathf.Max(MinRise, apex - position.y),
                Mathf.Max(0.05f, maxHeight - position.y));
            float vy = Mathf.Sqrt(2f * gravity * rise);
            float vx = Mathf.Clamp(offset, -1f, 1f) * drift;
            return new Vector2(vx, vy);
        }

        /// <summary>
        /// Advance the ball by <paramref name="dt"/>. The side walls bounce it back;
        /// the grass does not - the caller decides what touching it means.
        /// Returns true once the bottom of the ball has reached the grass.
        /// </summary>
        public static bool Step(ref Vector2 position, ref Vector2 velocity, float dt,
            float gravity, float radius, float halfWidth)
        {
            velocity.y -= gravity * dt;
            position += velocity * dt;

            float wall = Mathf.Max(0f, halfWidth - radius);
            if (position.x > wall)
            {
                position.x = wall;
                velocity.x = -Mathf.Abs(velocity.x) * WallBounce;
            }
            else if (position.x < -wall)
            {
                position.x = -wall;
                velocity.x = Mathf.Abs(velocity.x) * WallBounce;
            }

            return position.y <= radius;
        }

        /// <summary>Highest the centre of the ball will rise from here.</summary>
        public static float PeakHeight(Vector2 position, Vector2 velocity, float gravity)
        {
            if (velocity.y <= 0f) return position.y;
            return position.y + velocity.y * velocity.y / (2f * gravity);
        }
    }
}
