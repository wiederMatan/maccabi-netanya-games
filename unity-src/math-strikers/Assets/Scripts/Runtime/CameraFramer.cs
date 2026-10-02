using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// Keeps the same width of pitch in shot whatever shape the window is.
    ///
    /// Unity holds the vertical field of view fixed and derives the horizontal one
    /// from the aspect ratio, so on a narrow window - a phone, or a browser that is
    /// taller than it is wide - the sides get cropped and the striker walks out of
    /// frame. This inverts that: the horizontal field of view is the fixed one, and
    /// the vertical opens up as the window narrows.
    ///
    /// A portrait phone is different again: holding the pitch width there leaves
    /// most of the screen as sky, with the striker and goal small at the bottom.
    /// So in portrait the camera moves up and in behind the ball, and aims and
    /// zooms so the striker, the answer boards and the goal fill the band between
    /// the HUD at the top and the striker card at the bottom.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] float designVerticalFov = 36f;
        [SerializeField] float designAspect = 16f / 9f;
        [SerializeField] float maxVerticalFov = 74f;

        [Header("Portrait")]
        [SerializeField] Vector3 portraitPosition = new Vector3(-0.3f, 4.6f, -7f);
        // Screen band the subject has to fit in, as fractions of the height from
        // the bottom: clear of the striker card below and the HUD panels above.
        [SerializeField] float portraitSafeBottom = 0.12f;
        [SerializeField] float portraitSafeTop = 0.70f;
        [SerializeField] float portraitSideMargin = 0.03f;

        // What has to stay in shot, in world space. These mirror SceneBuilder and
        // MatchManager.PortraitStrikerMark: the striker's portrait mark and run-up,
        // the ball on the spot, the three answer boards and the goal frame.
        static readonly Vector3[] Subject =
        {
            new Vector3(-1.75f, 0f, -1.2f), new Vector3(-1.75f, 1.9f, -1.2f),
            new Vector3(-0.85f, 1.9f, -1.2f), new Vector3(0.3f, 0f, 1.2f),
            new Vector3(-3.15f, 0f, 4.5f), new Vector3(3.15f, 0.75f, 4.5f),
            new Vector3(-3.8f, 0f, 12f), new Vector3(3.8f, 2.6f, 12f),
            new Vector3(-3.8f, 2.6f, 12f), new Vector3(3.8f, 0f, 12f),
        };

        Vector3 designPosition;
        Quaternion designRotation;
        bool hasDesignPose;

        Camera cam;
        int lastWidth;
        int lastHeight;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            if (!hasDesignPose)
            {
                designPosition = transform.position;
                designRotation = transform.rotation;
                hasDesignPose = true;
            }
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
            if (cam == null || Screen.height <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            float designHalfHorizontal =
                Mathf.Atan(Mathf.Tan(designVerticalFov * 0.5f * Mathf.Deg2Rad) * designAspect);

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= 0.01f) return;

            // Only move the camera in play mode, so the saved scene keeps its pose.
            if (Application.isPlaying)
            {
                if (aspect < 1f)
                {
                    FramePortrait(aspect);
                    return;
                }
                transform.SetPositionAndRotation(designPosition, designRotation);
            }

            float vertical = 2f * Mathf.Atan(Mathf.Tan(designHalfHorizontal) / aspect) * Mathf.Rad2Deg;

            // A wide window should not zoom in past the framing the scene was built
            // for; a very tall one is capped so the pitch does not distort.
            cam.fieldOfView = Mathf.Clamp(vertical, designVerticalFov, maxVerticalFov);
        }

        /// <summary>
        /// Aim and zoom from the portrait position so the subject fills the safe
        /// band. A few rounds of: project the subject, scale the field of view to
        /// fit it, and turn the camera to centre it.
        /// </summary>
        public void FramePortrait(float aspect)
        {
            Vector3 position = portraitPosition;
            Vector3 centre = Vector3.zero;
            foreach (var p in Subject) centre += p;
            centre /= Subject.Length;

            Vector3 forward = (centre - position).normalized;
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

            float left = -1f + 2f * portraitSideMargin, right = 1f - 2f * portraitSideMargin;
            float bottom = -1f + 2f * portraitSafeBottom, top = -1f + 2f * portraitSafeTop;

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
                Vector3 local_ = new Vector3(offsetX * tanHalf * aspect, offsetY * tanHalf, 1f);
                forward = (rotation * local_).normalized;
            }

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 20f, maxVerticalFov);
        }

        public void Configure(float verticalFov, float aspect)
        {
            designVerticalFov = verticalFov;
            designAspect = aspect;
            Apply();
        }
    }
}
