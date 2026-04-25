// NuChef — HighlightCueController
//
// Follows a target transform and pulses its alpha / scale to draw attention.
// Works with any GameObject that has a Renderer (MeshRenderer, SpriteRenderer, etc.).
//
// Two modes:
//   Soft  — gentle steady glow (soft_highlight)
//   Pulse — faster scale + alpha pulse (pulse_highlight)

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [RequireComponent(typeof(Renderer))]
    public class HighlightCueController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Target (set at runtime by CueRenderer)")]
        [SerializeField] private Transform m_target;

        [Header("Offset from target centre")]
        [SerializeField] private Vector3 m_offset = Vector3.zero;

        [Header("Soft mode")]
        [SerializeField] private float m_softMinAlpha = 0.25f;
        [SerializeField] private float m_softMaxAlpha = 0.55f;
        [SerializeField] private float m_softSpeed = 1.2f;

        [Header("Pulse mode")]
        [SerializeField] private float m_pulseMinAlpha = 0.1f;
        [SerializeField] private float m_pulseMaxAlpha = 0.85f;
        [SerializeField] private float m_pulseSpeed = 3.0f;
        [SerializeField] private float m_pulseScaleMin = 0.85f;
        [SerializeField] private float m_pulseScaleMax = 1.15f;

        // ── Private state ──────────────────────────────────────────────────────

        private Renderer m_renderer;
        private MaterialPropertyBlock m_propBlock;
        private Vector3 m_baseScale;
        private bool m_pulse;
        private static readonly int s_colorProp = Shader.PropertyToID("_Color");
        private static readonly int s_baseColorProp = Shader.PropertyToID("_BaseColor"); // URP

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetTarget(Transform target) => m_target = target;
        public void SetPulse(bool pulse) => m_pulse = pulse;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            m_renderer = GetComponent<Renderer>();
            m_propBlock = new MaterialPropertyBlock();
            m_baseScale = transform.localScale;
        }

        private void Update()
        {
            // Follow target
            if (m_target != null)
                transform.position = m_target.position + m_offset;

            // Animate alpha
            float t = (Mathf.Sin(Time.time * (m_pulse ? m_pulseSpeed : m_softSpeed)) + 1f) * 0.5f;
            float alpha = m_pulse
                ? Mathf.Lerp(m_pulseMinAlpha, m_pulseMaxAlpha, t)
                : Mathf.Lerp(m_softMinAlpha, m_softMaxAlpha, t);

            // Apply via property block to avoid shared-material side-effects
            m_renderer.GetPropertyBlock(m_propBlock);
            Color c = m_renderer.sharedMaterial.color;
            c.a = alpha;
            m_propBlock.SetColor(s_colorProp, c);
            m_propBlock.SetColor(s_baseColorProp, c); // URP support
            m_renderer.SetPropertyBlock(m_propBlock);

            // Animate scale (pulse only)
            if (m_pulse)
            {
                float scale = Mathf.Lerp(m_pulseScaleMin, m_pulseScaleMax, t);
                transform.localScale = m_baseScale * scale;
            }
        }
    }
}
