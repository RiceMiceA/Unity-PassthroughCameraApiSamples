// NuChef — SceneObjectRegistry
//
// Resolves a label string (e.g. "onion") to the best available world-space Transform.
//
// Priority:
//   1. Spawned spatial marker with matching label  (persistent, preferred)
//   2. Live YOLO detection box with matching label (may flicker, fallback)
//   3. null if neither is found
//
// Attach to the same GameObject as DetectionManager (or its own prefab).
// Wire m_detectionManager and m_uiInference in the Inspector.

using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class SceneObjectRegistry : MonoBehaviour
    {
        [SerializeField] private DetectionManager m_detectionManager;
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Find the best Transform for the given label.
        /// Returns null if nothing is currently trackable.
        /// </summary>
        public Transform ResolveTarget(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string lower = label.Trim().ToLower();

            // 1) prefer a persistent spatial marker
            if (m_detectionManager != null)
            {
                foreach (var marker in m_detectionManager.SpawnedMarkers)
                {
                    if (marker == null) continue;
                    if (marker.GetYoloClassName().Trim().ToLower() == lower)
                        return marker.transform;
                }
            }

            // 2) fall back to a live YOLO box (may disappear between frames)
            if (m_uiInference != null)
            {
                foreach (var box in m_uiInference.m_boxDrawn)
                {
                    if (box?.BoxRectTransform == null) continue;
                    if (box.ClassName.Trim().ToLower() == lower)
                        return box.BoxRectTransform;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if at least one marker or live box exists for the label.
        /// </summary>
        public bool HasTarget(string label) => ResolveTarget(label) != null;

        /// <summary>
        /// Returns all spawned marker Transforms whose label matches (case-insensitive).
        /// </summary>
        public List<Transform> ResolveAllMarkers(string label)
        {
            var result = new List<Transform>();
            if (string.IsNullOrEmpty(label) || m_detectionManager == null) return result;
            string lower = label.Trim().ToLower();
            foreach (var marker in m_detectionManager.SpawnedMarkers)
            {
                if (marker != null && marker.GetYoloClassName().Trim().ToLower() == lower)
                    result.Add(marker.transform);
            }
            return result;
        }
    }
}
