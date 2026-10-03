using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// The ball on screen. It holds the ball's position and velocity in the juggling
    /// plane and draws it there, with a shadow on the grass that shrinks and fades as
    /// the ball climbs - the cue that tells a child how far it has to fall. The
    /// flight itself is worked out by <see cref="BallPhysics"/>.
    /// </summary>
    public class JuggleBall : MonoBehaviour
    {
        [SerializeField] Renderer shadow;

        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius { get; private set; } = 0.5f;

        float roll;
        float spin;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public Renderer Shadow => shadow;

        public void Bind(Renderer shadowRenderer)
        {
            shadow = shadowRenderer;
        }

        public void SetRadius(float radius)
        {
            Radius = radius;
            transform.localScale = Vector3.one * radius * 2f;
            Sync();
        }

        /// <summary>A kick sets the ball spinning the way it was struck.</summary>
        public void Spin(float amount) => spin = amount;

        /// <summary>Put the ball where it is and drop its shadow straight below it.</summary>
        public void Sync()
        {
            roll += spin * Time.deltaTime;
            transform.SetPositionAndRotation(new Vector3(Position.x, Position.y, 0f),
                Quaternion.Euler(0f, 0f, roll));

            if (shadow == null) return;

            float height = Mathf.Max(0f, Position.y - Radius);
            float closeness = 1f - Mathf.Clamp01(height / CameraFramer.Ceiling);
            float width = Radius * 2f * Mathf.Lerp(0.55f, 1.15f, closeness);
            // Just above the mown stripes, which stand 0.02 proud of the pitch.
            shadow.transform.position = new Vector3(Position.x, 0.03f, 0f);
            shadow.transform.localScale = new Vector3(width, 0.004f, width * 0.6f);

            block ??= new MaterialPropertyBlock();
            block.SetColor(ColorId, new Color(0f, 0f, 0f, Mathf.Lerp(0.15f, 0.5f, closeness)));
            shadow.SetPropertyBlock(block);
        }

        /// <summary>The ball's centre and radius in screen pixels.</summary>
        public void ScreenCircle(Camera cam, out Vector2 centre, out float radius)
        {
            var world = new Vector3(Position.x, Position.y, 0f);
            Vector3 c = cam.WorldToScreenPoint(world);
            Vector3 edge = cam.WorldToScreenPoint(world + cam.transform.right * Radius);
            centre = new Vector2(c.x, c.y);
            radius = Vector2.Distance(centre, new Vector2(edge.x, edge.y));
        }
    }
}
