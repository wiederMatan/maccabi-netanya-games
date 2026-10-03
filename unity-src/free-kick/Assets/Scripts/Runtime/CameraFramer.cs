using UnityEngine;

namespace FreeKick
{
    /// <summary>
    /// Frames each free kick so the taker, the ball, the wall and the whole goal fit
    /// in the band of screen between the message card at the top and the score bar at
    /// the bottom - whatever the shape of the window.
    ///
    /// The camera stands behind the ball looking at the goal. On a portrait phone it
    /// climbs higher, so the view looks down over the wall and the goal sits near the
    /// top of the tall screen instead of shrinking to a strip. Then it aims and zooms
    /// so the subject fills the band: a few rounds of project, scale the field of view
    /// to fit, and turn to centre.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] Vector2 landscapeOffset = new Vector2(7.2f, 2.3f);   // back, up
        [SerializeField] Vector2 portraitOffset = new Vector2(8.2f, 4.4f);
        [SerializeField] float sideMargin = 0.03f;
        [SerializeField] float maxVerticalFov = 75f;

        Vector3 ball = new Vector3(0f, 0f, Pitch.GoalLineZ - 20f);
        Vector3[] subject;

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

        /// <summary>Frame the kick from <paramref name="ballSpot"/>, with the taker waiting at <paramref name="taker"/>.</summary>
        public void FrameKick(Vector3 ballSpot, Vector3 taker)
        {
            ball = ballSpot;
            float half = Pitch.GoalWidth / 2f + 0.35f;
            float top = Pitch.GoalHeight + 0.35f;
            float z = Pitch.GoalLineZ;
            subject = new[]
            {
                new Vector3(-half, 0f, z), new Vector3(half, 0f, z),
                new Vector3(-half, top, z), new Vector3(half, top, z),
                new Vector3(ball.x, 0f, ball.z - 0.4f), new Vector3(ball.x, 0.3f, ball.z + 0.4f),
                taker + new Vector3(-0.35f, 0f, 0f), taker + new Vector3(-0.35f, 1.85f, 0f),
            };
            lastWidth = 0;
            Apply();
        }

        void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || Screen.height <= 0 || Screen.width <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            if (subject == null) FrameKick(ball, ball + new Vector3(-1.4f, 0f, -2.2f));

            float aspect = (float)Screen.width / Screen.height;
            bool portrait = aspect < 1f;
            Vector2 offset = portrait ? portraitOffset : landscapeOffset;

            // Stand behind the ball on the line to the middle of the goal.
            Vector3 toGoal = new Vector3(-ball.x, 0f, Pitch.GoalLineZ - ball.z).normalized;
            Vector3 position = ball - toGoal * offset.x + Vector3.up * offset.y;

            var (bottom, top) = HudController.SceneBand(Screen.width, Screen.height);
            Frame(position, aspect, bottom, top);
        }

        void Frame(Vector3 position, float aspect, float bandBottom, float bandTop)
        {
            Vector3 centre = Vector3.zero;
            foreach (var p in subject) centre += p;
            centre /= subject.Length;

            Vector3 forward = (centre - position).normalized;
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

            float left = -1f + 2f * sideMargin, right = 1f - 2f * sideMargin;
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
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 18f, maxVerticalFov);
        }
    }
}
