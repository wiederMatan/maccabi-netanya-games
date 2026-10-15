using UnityEngine;

namespace Headers
{
    /// <summary>
    /// Frames the box from behind the attacker so the whole goal, the strip of
    /// grass where the crosses land and the players fit between the counters at
    /// the top and the page's buttons at the bottom, whatever the window's shape.
    ///
    /// The camera stands back from the penalty spot and a little up, close
    /// enough that the players read on a phone. The corner taker out by the
    /// byline is left at the edge of the shot or just off it: taking him in would
    /// shrink the goal, so the ball swings in from the side. A few rounds of
    /// project, scale the field of view to fit, and turn to centre settle the shot.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        // Constants rather than serialized fields, so a tweak here cannot be
        // shadowed by a value saved in the scene.
        static readonly Vector2 LandscapeOffset = new Vector2(6.2f, 3.1f);   // back from the spot, up
        static readonly Vector2 PortraitOffset = new Vector2(8f, 7f);
        const float SideMargin = 0.02f;
        const float MaxVerticalFov = 75f;
        // Share of the screen height kept clear: counters at the top, page buttons at the bottom.
        const float LandscapeTop = 0.87f;
        const float PortraitTop = 0.88f;
        const float BandBottom = 0.04f;

        /// <summary>Where the attacker waits for the corner: the penalty spot, more or less.</summary>
        public static readonly Vector3 Spot = new Vector3(0f, 0f, Field.GoalLineZ - 8f);

        Camera cam;
        int lastWidth;
        int lastHeight;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            Apply();
        }

        void Update()
        {
            if (Screen.width == lastWidth && Screen.height == lastHeight) return;
            Apply();
        }

        public void Apply()
        {
            if (Screen.height <= 0 || Screen.width <= 0) return;
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            ApplyFor(Screen.width, Screen.height);
        }

        /// <summary>Frame the shot for a window <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
        public void ApplyFor(int width, int height)
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || height <= 0 || width <= 0) return;

            float aspect = (float)width / height;
            bool portrait = aspect < 1f;
            Vector2 offset = portrait ? PortraitOffset : LandscapeOffset;
            Vector3 position = Spot + new Vector3(0f, offset.y, -offset.x);
            Frame(position, Subject(portrait), aspect, BandBottom, portrait ? PortraitTop : LandscapeTop);
        }

        static Vector3[] Subject(bool portrait)
        {
            float half = Field.GoalWidth / 2f + 0.4f;
            float top = Field.GoalHeight + 0.5f;
            float z = Field.GoalLineZ;
            // The spread of meeting points, at the attackers' feet and heads.
            float near = Field.GoalLineZ - 9f;
            // A portrait screen is narrow, so there an attacker running in from
            // wide may start just off its edge.
            float width = portrait ? 2.6f : 3.2f;
            var points = new System.Collections.Generic.List<Vector3>
            {
                new Vector3(-half, 0f, z), new Vector3(half, 0f, z),
                new Vector3(-half, top, z), new Vector3(half, top, z),
                new Vector3(-width, 0f, near), new Vector3(width, 0f, near),
                new Vector3(-width, 2.4f, near), new Vector3(width, 2.4f, near),
            };
            return points.ToArray();
        }

        void Frame(Vector3 position, Vector3[] subject, float aspect, float bandBottom, float bandTop)
        {
            Vector3 centre = Vector3.zero;
            foreach (var p in subject) centre += p;
            centre /= subject.Length;

            Vector3 forward = (centre - position).normalized;
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

            float left = -1f + 2f * SideMargin, right = 1f - 2f * SideMargin;
            float bottom = -1f + 2f * bandBottom, top = -1f + 2f * bandTop;

            for (int round = 0; round < 16; round++)
            {
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var inverse = Quaternion.Inverse(rotation);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

                foreach (var p in subject)
                {
                    Vector3 local = inverse * (p - position);
                    if (local.z < 0.1f) local.z = 0.1f;
                    float x = local.x / local.z / (tanHalf * aspect);
                    float y = local.y / local.z / tanHalf;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }

                // Zoom so the subject just fits the band in its tighter direction.
                float scale = Mathf.Max((maxX - minX) / (right - left), (maxY - minY) / (top - bottom));
                tanHalf *= scale;

                // Then turn toward wherever the subject sits off-centre.
                float offsetX = ((minX + maxX) / scale - (left + right)) * 0.5f;
                float offsetY = ((minY + maxY) / scale - (bottom + top)) * 0.5f;
                Vector3 turn = new Vector3(offsetX * tanHalf * aspect, offsetY * tanHalf, 1f);
                forward = (rotation * turn).normalized;
            }

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 18f, MaxVerticalFov);
        }
    }
}
