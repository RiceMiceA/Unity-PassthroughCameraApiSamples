// NuChef — HudFollowCamera
//
// Add to any World-Space Canvas root (or parent) to make the panel lazily
// follow the Quest center-eye camera.
//
// "Lazy follow" = the panel drifts to a fixed camera-local offset over ~0.2 s
// so it never feels rigidly glued to the face.
//
// ── Scene setup ───────────────────────────────────────────────────────────────
// 1. Set the Canvas Render Mode → "World Space".
// 2. Choose a canvas Width × Height (e.g. 720 × 480) and Scale (e.g. 0.001
//    so 1 pixel = 1 mm, giving a 72 cm × 48 cm physical panel).
// 3. Place HudFollowCamera on the Canvas GameObject (or an always-active parent).
// 4. Inspector:
//    · m_localOffset  – camera-local (right, up, forward) metres.
//                       (0, -0.20, 0.65) = 65 cm ahead, 20 cm below gaze centre.
//                       (0.30, 0.10, 0.65) = top-right of view.
//    · m_posLerpSpeed – translation catch-up speed (6–10 is natural).
//    · m_rotLerpSpeed – rotation catch-up speed    (4–8 is natural).
//    · m_billboardYOnly – true = panel stays upright, only yaw follows.
//                         false = full camera orientation.
//
// Attach to both GuidanceHudController's canvas AND to the VisionRvHudController
// canvas to make them follow independently.

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class HudFollowCamera : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Offset from camera (metres, camera-local space)")]
        [Tooltip("(right, up, forward).\n" +
                 "e.g. (0, -0.20, 0.65) = 65 cm ahead, 20 cm below gaze centre.\n" +
                 "e.g. (0.30, 0.10, 0.65) = top-right corner.")]
        [SerializeField] private Vector3 m_localOffset = new Vector3(0f, -0.20f, 0.65f);

        [Header("Follow speeds (higher = snappier)")]
        [SerializeField] [Range(1f, 30f)] private float m_posLerpSpeed = 6f;
        [SerializeField] [Range(1f, 30f)] private float m_rotLerpSpeed = 5f;

        [Header("Rotation mode")]
        [Tooltip("true  = only yaw follows (panel stays upright, no pitch/roll).\n" +
                 "false = full camera orientation.")]
        [SerializeField] private bool m_billboardYOnly = true;

        [Header("Snap on first active frame")]
        [Tooltip("Teleport to target on re-enable so the panel doesn't fly in from far away.")]
        [SerializeField] private bool m_snapOnFirstFrame = true;

        // ── Private ───────────────────────────────────────────────────────────

        private bool      m_followEnabled = true;
        private bool      m_firstFrame    = true;
        private Transform m_cam;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()  => m_firstFrame = true;  // snap when re-shown

        private void LateUpdate()
        {
            if (!m_followEnabled) return;

            // Lazy camera resolve — works even if OVRCameraRig awakens after this component.
            if (m_cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                m_cam = main.transform;
            }

            // Target position: convert inspector offset from camera-local to world space.
            Vector3 targetPos = m_cam.TransformPoint(m_localOffset);

            // Target rotation.
            Quaternion targetRot = m_billboardYOnly
                ? Quaternion.Euler(0f, m_cam.eulerAngles.y, 0f)
                : m_cam.rotation;

            // Apply — instant on first frame, lerp thereafter.
            if (m_firstFrame && m_snapOnFirstFrame)
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
                m_firstFrame = false;
            }
            else
            {
                m_firstFrame = false;
                transform.position = Vector3.Lerp(
                    transform.position, targetPos, m_posLerpSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, m_rotLerpSpeed * Time.deltaTime);
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Pause or resume camera following.</summary>
        public void SetFollowEnabled(bool follow) => m_followEnabled = follow;

        /// <summary>Instantly teleport to the target position (skip lerp this frame).</summary>
        public void Snap() => m_firstFrame = true;

        /// <summary>Change the camera-local offset at runtime and snap into position.</summary>
        public void SetOffset(Vector3 localOffset) { m_localOffset = localOffset; Snap(); }
    }
}
