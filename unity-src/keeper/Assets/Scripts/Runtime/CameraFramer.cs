using UnityEngine;

namespace Keeper
{
    /// <summary>
    /// Frames the goal and the penalty taker from behind the keeper, whatever shape
    /// the window is.
    ///
    /// Unity holds the vertical field of view fixed and derives the horizontal one
    /// from the aspect ratio, so on a portrait phone the goal posts would fall off
    /// the sides. Instead this aims and zooms so the whole goal frame, the keeper
    /// and the striker fill the band of screen between the message card at the top
    /// and the score bar (plus the page's round buttons) at the bottom. The camera
    /// sits high enough that the striker shows above the crossbar rather than
    /// hidden behind the keeper. A narrow
    /// window also pulls the camera further back, so the wide field of view it
    /// needs does not bend the goal.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] Vector3 widePosition = new Vector3(0f, 5.0f, -7.5f);
        [SerializeField] Vector3 tallPosition = new Vector3(0f, 6.2f, -10.5f);
        [SerializeField] float maxVerticalFov = 76f;

        // HUD space reserved at the top and bottom, in the canvas's 1920x1080
        // reference units (see SceneBuilder.BuildHud and the web template's placeCorner).
        [SerializeField] float reservedTopUnits = 200f;
        [SerializeField] float reservedBottomUnits = 300f;
        [SerializeField] float sideMargin = 0.03f;

        // What has to stay in shot, in world space: the goal frame with a little
        // room around the posts, the keeper's head, the ball on the spot and the
        // striker on his mark.
        static readonly Vector3[] Subject =
        {
            new Vector3(-4.0f, -0.05f, 0f), new Vector3(4.0f, -0.05f, 0f),
            new Vector3(-4.0f, 2.7f, 0f), new Vector3(4.0f, 2.7f, 0f),
            new Vector3(0f, 0f, Goal.PenaltySpotZ),
            new Vector3(-0.6f, 2.0f, Goal.PenaltySpotZ + 3.4f), new Vector3(1.2f, 2.0f, Goal.PenaltySpotZ + 3.4f),
        };

        static readonly Vector3 LookTarget = new Vector3(0f, 1f, 8f);

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

        void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || Screen.height <= 0 || Screen.width <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            // Only move the camera in play mode, so the saved scene keeps its pose.
            if (!Application.isPlaying) return;
            Frame(Screen.width, Screen.height);
        }

        /// <summary>
        /// Aim and zoom so the subject fills the safe band. A few rounds of: project
        /// the subject, scale the field of view to fit it, and turn the camera to
        /// centre it.
        /// </summary>
        public void Frame(float width, float height)
        {
            float aspect = width / height;
            float unitScale = Mathf.Sqrt(width * height / (1920f * 1080f));
            float safeTop = 1f - Mathf.Clamp01(reservedTopUnits * unitScale / height);
            float safeBottom = Mathf.Clamp01(reservedBottomUnits * unitScale / height);

            Vector3 position = Vector3.Lerp(tallPosition, widePosition, Mathf.InverseLerp(0.5f, 1.5f, aspect));
            Vector3 forward = (LookTarget - position).normalized;
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

            float left = -1f + 2f * sideMargin, right = 1f - 2f * sideMargin;
            float bottom = -1f + 2f * safeBottom, top = -1f + 2f * safeTop;

            for (int round = 0; round < 16; round++)
            {
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var inverse = Quaternion.Inverse(rotation);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

                foreach (var p in Subject)
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
                Vector3 shift = new Vector3(offsetX * tanHalf * aspect, offsetY * tanHalf, 1f);
                forward = (rotation * shift).normalized;
            }

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 20f, maxVerticalFov);
        }
    }
}
