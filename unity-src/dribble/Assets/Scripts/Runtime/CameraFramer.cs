using UnityEngine;

namespace Dribble
{
    /// <summary>
    /// Chase camera that keeps all three lanes in shot whatever shape the window is.
    ///
    /// Unity holds the vertical field of view fixed and derives the horizontal one
    /// from the aspect ratio, so on a portrait phone the outer lanes would fall off
    /// the sides. Instead the field of view is solved each time the window changes
    /// shape: wide enough that the full width of the three lanes fits at the
    /// runner, and never narrower than a comfortable minimum. The camera then tilts
    /// so the runner's feet sit at a fixed height on screen, clear of the page's
    /// buttons below and leaving the rest of the screen for the pitch ahead.
    ///
    /// Portrait sits higher and further back, so the extra height of a tall
    /// screen shows more of what is coming rather than sky.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] Vector3 landscapeOffset = new Vector3(0f, 4.2f, -5f);
        [SerializeField] Vector3 portraitOffset = new Vector3(0f, 6.6f, -6.6f);
        [SerializeField] float minVerticalFov = 52f;
        [SerializeField] float maxVerticalFov = 82f;
        // Half the width that must fit at the runner: outer lane centre plus a
        // shoulder and a margin.
        [SerializeField] float halfWidth = 2.6f;
        // Feet height on screen, as a fraction of the screen from the bottom.
        [SerializeField] float landscapeFeet = 0.13f;
        [SerializeField] float portraitFeet = 0.2f;
        // How much the camera drifts with the runner across the lanes.
        [SerializeField] float follow = 0.25f;

        Camera cam;
        int lastWidth;
        int lastHeight;
        float pitchDegrees;
        Vector3 offset;
        float trackedX;
        float trackedVelocity;

        public Transform Target { get; set; }

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            Apply();
        }

        void LateUpdate()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight) Apply();

            float targetX = Target != null ? Target.position.x * follow : 0f;
            trackedX = Mathf.SmoothDamp(trackedX, targetX, ref trackedVelocity, 0.25f);
            transform.SetPositionAndRotation(
                offset + new Vector3(trackedX, 0f, 0f),
                Quaternion.Euler(pitchDegrees, 0f, 0f));
        }

        public void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || Screen.height <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= 0.01f) return;

            bool portrait = aspect < 1f;
            offset = portrait ? portraitOffset : landscapeOffset;
            float feet = portrait ? portraitFeet : landscapeFeet;

            // The drift across lanes eats into the side margin, so allow for it.
            float width = halfWidth + Course.LaneWidth * follow;

            // A few rounds settle it: the tilt barely changes the depth to the runner.
            float fov = minVerticalFov;
            for (int round = 0; round < 3; round++)
            {
                pitchDegrees = SolvePitch(fov, feet);
                fov = SolveFov(aspect, width);
            }
            pitchDegrees = SolvePitch(fov, feet);

            cam.fieldOfView = fov;
            transform.SetPositionAndRotation(offset, Quaternion.Euler(pitchDegrees, 0f, 0f));
        }

        /// <summary>Tilt so the runner's feet project <paramref name="feet"/> of the way up the screen.</summary>
        float SolvePitch(float fov, float feet)
        {
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            // Angle of the feet below the horizontal, seen from the camera.
            float down = Mathf.Atan2(offset.y, -offset.z) * Mathf.Rad2Deg;
            // ...and how far below the screen centre they should appear.
            float belowCentre = Mathf.Atan((1f - 2f * feet) * tanHalf) * Mathf.Rad2Deg;
            return down - belowCentre;
        }

        /// <summary>Vertical field of view that fits the lanes at the runner across the screen.</summary>
        float SolveFov(float aspect, float width)
        {
            var rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            var inverse = Quaternion.Inverse(rotation);
            float needed = 0f;
            // Check the lane edges at the feet and at head height.
            foreach (float y in new[] { 0f, 1.9f })
            {
                Vector3 local = inverse * (new Vector3(width, y, 0f) - offset);
                if (local.z < 0.1f) continue;
                needed = Mathf.Max(needed, local.x / local.z);
            }
            float tanHalfVertical = needed / aspect;
            float fov = 2f * Mathf.Atan(tanHalfVertical) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, minVerticalFov, maxVerticalFov);
        }
    }
}
