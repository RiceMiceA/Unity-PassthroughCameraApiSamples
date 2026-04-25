// NuChef — ArrowCueController
//
// Positions an arrow mesh above a target transform, faces it toward the target,
// and animates a gentle up/down bounce to attract attention.
//
// Prefab setup recommendation:
//   ArrowCueRoot  ← this script lives here
//     └─ ArrowVisual  (the imported mesh)
//
// The pivot does NOT need to sit at the mesh tip — this script manages position
// and rotation entirely in world space.

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class ArrowCueController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Target (set at runtime by CueRenderer)")]
        [SerializeField] private Transform m_target;

        [Header("Positioning")]
        [Tooltip("Offset above the target centre in world space.")]
        [SerializeField] private Vector3 m_worldOffset = new(0f, 0.12f, 0f);

        [Header("Bounce animation")]
        [SerializeField] private float m_bounceAmplitude = 0.02f;
        [SerializeField] private float m_bounceSpeed = 2.5f;

        [Header("Yaw sway animation (optional)")]
        [SerializeField] private float m_yawSwayAmplitude = 8f;   // degrees
        [SerializeField] private float m_yawSwaySpeed = 1.2f;

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetTarget(Transform target) => m_target = target;

        // ── Update ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (m_target == null) return;

            // Vertical bounce
            float bounce = Mathf.Sin(Time.time * m_bounceSpeed) * m_bounceAmplitude;
            Vector3 basePos = m_target.position + m_worldOffset;
            transform.position = basePos + new Vector3(0f, bounce, 0f);

            // Point toward the target (arrow tip faces down toward target centre)
            Vector3 toTarget = (m_target.position - transform.position).normalized;
            if (toTarget != Vector3.zero)
            {
                Quaternion lookRot = Quaternion.LookRotation(toTarget);
                // Apply gentle yaw sway on top of the look rotation
                float yaw = Mathf.Sin(Time.time * m_yawSwaySpeed) * m_yawSwayAmplitude;
                transform.rotation = lookRot * Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
