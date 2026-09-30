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
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] float designVerticalFov = 36f;
        [SerializeField] float designAspect = 16f / 9f;
        [SerializeField] float maxVerticalFov = 74f;

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
            if (cam == null || Screen.height <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            float designHalfHorizontal =
                Mathf.Atan(Mathf.Tan(designVerticalFov * 0.5f * Mathf.Deg2Rad) * designAspect);

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= 0.01f) return;

            float vertical = 2f * Mathf.Atan(Mathf.Tan(designHalfHorizontal) / aspect) * Mathf.Rad2Deg;

            // A wide window should not zoom in past the framing the scene was built
            // for; a very tall one is capped so the pitch does not distort.
            cam.fieldOfView = Mathf.Clamp(vertical, designVerticalFov, maxVerticalFov);
        }

        public void Configure(float verticalFov, float aspect)
        {
            designVerticalFov = verticalFov;
            designAspect = aspect;
            Apply();
        }
    }
}
