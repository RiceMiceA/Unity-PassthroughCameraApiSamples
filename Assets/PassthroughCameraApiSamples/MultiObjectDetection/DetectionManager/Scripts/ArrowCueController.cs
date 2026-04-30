// NuChef — ArrowCueController
//
// Positions an arrow mesh above a target transform, faces it toward the target,
// and animates a gentle up/down bounce to attract attention.
//
// Prefab setup:
//   ArrowCueRoot  ← this script lives here
//     └─ ArrowVisual  (MeshFilter + MeshRenderer with Arrow.fbx mesh assigned)

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
        [SerializeField] private Vector3 m_worldOffset = new(0f, 0.15f, 0f);

        [Header("Bounce animation")]
        [SerializeField] private float m_bounceAmplitude = 0.02f;
        [SerializeField] private float m_bounceSpeed     = 2.5f;

        [Header("Visuals")]
        [Tooltip("Drag any .mat file here — applied to the ArrowVisual child renderer at runtime.")]
        [SerializeField] private Material m_arrowMaterial;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (m_arrowMaterial == null) return;
            // Apply the material to any MeshRenderer on this GameObject or its children.
            var mr = GetComponentInChildren<MeshRenderer>();
            if (mr != null)
                mr.material = m_arrowMaterial;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetTarget(Transform target) => m_target = target;

        // ── Update ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (m_target == null) return;

            // Position: above the target with bounce
            var bounce  = Mathf.Sin(Time.time * m_bounceSpeed) * m_bounceAmplitude;
            transform.position = m_target.position + m_worldOffset + new Vector3(0f, bounce, 0f);

            // Rotation: billboard — always face the camera so the arrow is flat-on visible
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        }
    }
}
