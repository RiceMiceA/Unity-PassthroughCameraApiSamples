// Copyright (c) Meta Platforms, Inc. and affiliates.
// NuChef Quest — Ingredient Review Ray Selector
//
// Active only while in IngredientReview phase.
// Uses the right controller transform as a ray origin and Physics.Raycast against
// marker colliders (on the IngredientMarker layer) to detect hover.
//
// IMPORTANT: This does NOT use EnvironmentRaycastManager.
//   - EnvironmentRaycastManager hits real-world depth geometry, not Unity GameObjects.
//   - Spawned markers are ordinary GameObjects with colliders; Physics.Raycast is correct here.

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class MarkerReviewRaySelector : MonoBehaviour
    {
        [Header("Ray configuration")]
        [Tooltip("Transform used as the ray origin + forward direction. Assign the right controller anchor.")]
        [SerializeField] private Transform m_rayOrigin;

        [Tooltip("Maximum raycast distance in metres.")]
        [SerializeField] private float m_maxDistance = 5f;

        [Tooltip("Layer mask that includes the IngredientMarker layer only.")]
        [SerializeField] private LayerMask m_markerLayerMask = ~0;

        [Header("Visual feedback (optional)")]
        [Tooltip("LineRenderer used to draw the laser pointer. Leave empty to skip.")]
        [SerializeField] private LineRenderer m_lineRenderer;

        [Header("References")]
        [SerializeField] private DetectionManager m_detectionManager;

        // ── State ─────────────────────────────────────────────────────────────

        private DetectionSpawnMarkerAnim m_hoveredMarker;
        private bool m_isActive;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Enable or disable this selector. DetectionManager calls this on mode transitions.</summary>
        public void SetActive(bool active)
        {
            m_isActive = active;

            if (m_lineRenderer != null)
                m_lineRenderer.enabled = active;

            if (!active)
                SetHovered(null);
        }

        /// <summary>Returns the marker currently under the ray, or null.</summary>
        public DetectionSpawnMarkerAnim GetHoveredMarker() => m_hoveredMarker;

        /// <summary>
        /// Destroys the hovered marker via DetectionManager and returns its label.
        /// Returns false if nothing is hovered.
        /// </summary>
        public bool TryDeleteHoveredMarker(out string deletedLabel)
        {
            deletedLabel = null;
            if (m_hoveredMarker == null) return false;

            deletedLabel = m_hoveredMarker.GetYoloClassName();
            m_detectionManager.RemoveMarker(m_hoveredMarker);
            m_hoveredMarker = null;
            return true;
        }

        // ── Unity messages ────────────────────────────────────────────────────

        private void Awake()
        {
            // Start inactive; DetectionManager enables us when entering review mode.
            m_isActive = false;
            if (m_lineRenderer != null)
                m_lineRenderer.enabled = false;
        }

        private void Update()
        {
            if (!m_isActive || m_rayOrigin == null) return;

            var ray = new Ray(m_rayOrigin.position, m_rayOrigin.forward);
            DrawRay(ray);

            if (Physics.Raycast(ray, out var hit, m_maxDistance, m_markerLayerMask))
            {
                var marker = hit.collider.GetComponentInParent<DetectionSpawnMarkerAnim>();
                SetHovered(marker);
            }
            else
            {
                SetHovered(null);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetHovered(DetectionSpawnMarkerAnim marker)
        {
            if (m_hoveredMarker == marker) return;

            if (m_hoveredMarker != null)
                m_hoveredMarker.SetHovered(false);

            m_hoveredMarker = marker;

            if (m_hoveredMarker != null)
                m_hoveredMarker.SetHovered(true);
        }

        private void DrawRay(Ray ray)
        {
            if (m_lineRenderer == null) return;

            m_lineRenderer.SetPosition(0, ray.origin);
            m_lineRenderer.SetPosition(1, ray.origin + ray.direction * m_maxDistance);
        }
    }
}
