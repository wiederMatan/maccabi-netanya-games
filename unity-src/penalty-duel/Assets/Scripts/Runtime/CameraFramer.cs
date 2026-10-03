using UnityEngine;

namespace PenaltyDuel
{
    /// <summary>
    /// Frames the match for whatever shape the window is, in two shots:
    ///
    /// - the aim view, close on the goal, while a player picks a spot - so the six
    ///   spots are as big as the screen allows, even on a small phone;
    /// - the wide view, from behind the taker, for the run-up and the kick.
    ///
    /// Both are fitted rather than fixed: from a set position the camera turns and
    /// zooms until its subject fills the band of screen left between the HUD at the
    /// top and the score bar at the bottom. Portrait and landscape each have their
    /// own positions and bands, so a phone held either way gets a full frame.
    /// Switching view eases the camera across rather than cutting.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] float maxVerticalFov = 74f;
        [SerializeField] float blendSpeed = 5f;

        [Header("Landscape")]
        [SerializeField] Vector3 landscapeWidePosition = new Vector3(0f, 2.6f, -8f);
        [SerializeField] Vector3 landscapeAimPosition = new Vector3(0f, 1.5f, 3f);
        // Screen band the subject has to fit in, as fractions of the height from the
        // bottom: above the score bar, below the scoreboard and the prompt.
        [SerializeField] float landscapeSafeBottom = 0.16f;
        [SerializeField] float landscapeSafeTop = 0.75f;

        [Header("Portrait")]
        [SerializeField] Vector3 portraitWidePosition = new Vector3(-0.3f, 4.6f, -7f);
        [SerializeField] Vector3 portraitAimPosition = new Vector3(0f, 1.6f, 2f);
        [SerializeField] float portraitSafeBottom = 0.14f;
        [SerializeField] float portraitSafeTop = 0.76f;
        [SerializeField] float sideMargin = 0.03f;

        // What has to stay in shot, in world space. These mirror SceneBuilder and
        // MatchManager: the goal frame with the spots in it, and for the wide view
        // also the taker's mark, his run-up and the ball on the spot.
        static readonly Vector3[] GoalSubject =
        {
            new Vector3(-3.85f, -0.05f, 12f), new Vector3(3.85f, -0.05f, 12f),
            new Vector3(-3.85f, 2.62f, 12f), new Vector3(3.85f, 2.62f, 12f),
        };

        static readonly Vector3[] WideSubject =
        {
            new Vector3(-1.75f, 0f, -1.2f), new Vector3(-1.75f, 1.9f, -1.2f),
            new Vector3(-0.85f, 1.9f, -1.2f), new Vector3(0.3f, 0f, 1.2f),
            new Vector3(-3.8f, 0f, 12f), new Vector3(3.8f, 2.6f, 12f),
            new Vector3(-3.8f, 2.6f, 12f), new Vector3(3.8f, 0f, 12f),
        };

        Camera cam;
        int lastWidth;
        int lastHeight;
        bool aimView;
        bool snap = true;

        // Band handed over by the HUD once it has laid itself out; until then the
        // per-orientation defaults above stand in.
        float bandBottom = -1f;
        float bandTop = -1f;

        Vector3 targetPosition;
        Quaternion targetRotation;
        float targetFov;

        public bool AimView => aimView;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            snap = true;
            Refit();
        }

        /// <summary>Ease to the close-up on the goal (true) or the wide shot (false).</summary>
        public void SetAimView(bool aim)
        {
            if (aim == aimView) return;
            aimView = aim;
            Refit();
        }

        /// <summary>
        /// The HUD reports the screen band it leaves free, as fractions of the height
        /// from the bottom. Only a new screen shape changes it, so the camera snaps.
        /// </summary>
        public void SetBand(float bottom, float top)
        {
            if (Mathf.Approximately(bottom, bandBottom) && Mathf.Approximately(top, bandTop)) return;
            bandBottom = bottom;
            bandTop = top;
            snap = true;
            Refit();
        }

        void LateUpdate()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                // A rotated phone jumps straight to its new framing.
                snap = true;
                Refit();
            }

            if (cam == null) return;

            if (snap || !Application.isPlaying)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                cam.fieldOfView = targetFov;
                snap = false;
                return;
            }

            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPosition, k),
                Quaternion.Slerp(transform.rotation, targetRotation, k));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, k);
        }

        void Refit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || Screen.height <= 0) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= 0.01f) return;

            bool portrait = aspect < 1f;
            Vector3 position = portrait
                ? (aimView ? portraitAimPosition : portraitWidePosition)
                : (aimView ? landscapeAimPosition : landscapeWidePosition);
            float bottom = bandBottom >= 0f ? bandBottom : portrait ? portraitSafeBottom : landscapeSafeBottom;
            float top = bandTop >= 0f ? bandTop : portrait ? portraitSafeTop : landscapeSafeTop;

            Fit(position, aimView ? GoalSubject : WideSubject, aspect, bottom, top,
                out targetRotation, out targetFov);
            targetPosition = position;
        }

        /// <summary>
        /// Aim and zoom from <paramref name="position"/> so the subject fills the safe
        /// band. A few rounds of: project the subject, scale the field of view to fit
        /// it, and turn the camera to centre it.
        /// </summary>
        void Fit(Vector3 position, Vector3[] subject, float aspect, float safeBottom, float safeTop,
            out Quaternion rotation, out float fov)
        {
            Vector3 centre = Vector3.zero;
            foreach (var p in subject) centre += p;
            centre /= subject.Length;

            Vector3 forward = (centre - position).normalized;
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

            float left = -1f + 2f * sideMargin, right = 1f - 2f * sideMargin;
            float bottom = -1f + 2f * safeBottom, top = -1f + 2f * safeTop;

            for (int round = 0; round < 16; round++)
            {
                var look = Quaternion.LookRotation(forward, Vector3.up);
                var inverse = Quaternion.Inverse(look);
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
                Vector3 shift = new Vector3(offsetX * tanHalf * aspect, offsetY * tanHalf, 1f);
                forward = (look * shift).normalized;
            }

            rotation = Quaternion.LookRotation(forward, Vector3.up);
            fov = Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 12f, maxVerticalFov);
        }
    }
}
