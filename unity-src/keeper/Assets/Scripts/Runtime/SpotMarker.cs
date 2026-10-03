using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// A ring painted in the goal mouth at one of the spots a shot can go to. The
    /// rings show the player where they can dive; the one the striker has picked
    /// glows for a moment before the run-up (the tell), and after the shot the
    /// rings show where the ball went and where the keeper went.
    /// </summary>
    public class SpotMarker : MonoBehaviour
    {
        public enum Look { Hidden, Idle, Tell, Picked, Saved, Conceded }

        static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color TellColor = new Color(1f, 0.82f, 0.12f, 1f);
        static readonly Color PickedColor = new Color(0.55f, 0.85f, 1f, 0.9f);
        static readonly Color SavedColor = new Color(0.25f, 0.9f, 0.4f, 1f);
        static readonly Color ConcededColor = new Color(0.95f, 0.25f, 0.2f, 1f);

        [SerializeField] Renderer ring;

        MaterialPropertyBlock block;
        Look look = Look.Hidden;
        Look afterTell = Look.Idle;   // what the ring settles to once the glow ends
        Vector3 baseScale;
        float tellUntil;

        public Look Current => look;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            Show(Look.Hidden);
        }

        /// <summary>Glow as the striker's target until <paramref name="seconds"/> from now, then go back to idle.</summary>
        public void Tell(float seconds)
        {
            tellUntil = Time.time + seconds;
            afterTell = Look.Idle;
            Show(Look.Tell);
        }

        /// <summary>The player chose this spot. If it is still glowing, it turns to "picked" when the glow ends.</summary>
        public void Pick()
        {
            if (look == Look.Tell) afterTell = Look.Picked;
            else Show(Look.Picked);
        }

        public void Show(Look next)
        {
            look = next;
            if (ring == null) return;

            ring.enabled = next != Look.Hidden;
            transform.localScale = baseScale;
            Paint(next switch
            {
                Look.Tell => TellColor,
                Look.Picked => PickedColor,
                Look.Saved => SavedColor,
                Look.Conceded => ConcededColor,
                _ => IdleColor
            });
        }

        void Update()
        {
            if (look != Look.Tell) return;

            if (Time.time >= tellUntil)
            {
                Show(afterTell);
                return;
            }

            // A quick pulse is easier to catch out of the corner of an eye than a steady light.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
            transform.localScale = baseScale * (1.1f + 0.18f * pulse);
            Paint(Color.Lerp(TellColor, Color.white, pulse * 0.35f));
        }

        void Paint(Color color)
        {
            if (block == null) block = new MaterialPropertyBlock();
            ring.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            ring.SetPropertyBlock(block);
        }

        public void Bind(Renderer ringRenderer) => ring = ringRenderer;
    }
}
