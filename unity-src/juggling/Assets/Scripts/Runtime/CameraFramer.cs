using UnityEngine;

namespace Juggling
{
    /// <summary>
    /// Frames the juggling plane (z = 0) so the grass line and the top of the
    /// ball's flight land in the same places on screen whatever shape the window is.
    ///
    /// The ball lives in a vertical slice of the world, from the grass (y = 0) up to
    /// <see cref="Ceiling"/>. The camera looks straight down the pitch at that slice
    /// and picks its height and field of view so that the grass sits a fixed
    /// fraction up from the bottom of the screen - leaving a strip below it for the
    /// page's round buttons - and the ceiling sits just under the HUD's top bar.
    /// Width follows from the aspect ratio: a phone held upright gets a narrow
    /// column to juggle in, a landscape window a wide one (capped, so the ball
    /// never wanders off to the far edges of a desktop monitor).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        /// <summary>Highest the top of the ball may ever reach, in metres.</summary>
        public const float Ceiling = 5f;

        // Fraction of the screen height, from the bottom, where the grass line sits.
        // Mirrors placeCorner() in the WebGL template, which centres the page's
        // buttons in the strip below it.
        public const float GroundFraction = 0.16f;

        // The HUD's top bar plus a little air, in canvas units. Mirrors
        // HudController.TopReserve.
        [SerializeField] float topReserve = 138f;
        [SerializeField] float distance = 10f;
        [SerializeField] float maxPlayHalfWidth = 3.2f;
        [SerializeField] float sideMargin = 0.12f;

        Camera cam;
        int lastWidth;
        int lastHeight;

        /// <summary>Half the visible width of the juggling plane, in metres.</summary>
        public float VisibleHalfWidth { get; private set; } = 4f;

        /// <summary>Half the width the ball's edge may travel in, in metres.</summary>
        public float PlayHalfWidth => Mathf.Min(maxPlayHalfWidth, VisibleHalfWidth - sideMargin);

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
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || Screen.height <= 0 || Screen.width <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            float aspect = (float)Screen.width / Screen.height;

            // The HUD canvas is 720 units across its short side (HudController), so
            // its height in canvas units depends on which side that is.
            float canvasHeight = aspect < 1f ? HudController.ReferenceSize / aspect : HudController.ReferenceSize;
            float topFraction = 1f - topReserve / canvasHeight;

            // World height visible on the juggling plane, and where the camera has
            // to sit so the grass lands on GroundFraction.
            float visible = Ceiling / Mathf.Max(0.2f, topFraction - GroundFraction);
            float height = (0.5f - GroundFraction) * visible;

            transform.SetPositionAndRotation(new Vector3(0f, height, -distance), Quaternion.identity);
            cam.fieldOfView = 2f * Mathf.Atan(visible * 0.5f / distance) * Mathf.Rad2Deg;
            VisibleHalfWidth = visible * 0.5f * aspect;
        }
    }
}
