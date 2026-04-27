// Copyright (c) Meta Platforms, Inc. and affiliates.
// AI Nutritious Culinary Assistant — Quest-side ingredient inventory.
//
// Owns:
//   - candidate ingredient list (live detections, not yet confirmed)
//   - confirmed ingredient list (user pressed A / pinch)
//   - stability logic (SeenCount threshold + timeout)
//   - periodic candidate POST to backend (only on change)
//   - immediate confirmed POST on confirmation

using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class IngredientInventoryManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BackendClient m_backendClient;

        [Header("Stability settings")]
        [Tooltip("Frames an ingredient must appear before it is considered stable.")]
        [SerializeField] private int m_stabilityThreshold = 3;

        [Tooltip("Seconds without a detection before a candidate is removed.")]
        [SerializeField] private float m_candidateTimeout = 2.0f;

        [Tooltip("Minimum interval between candidate list POSTs to backend.")]
        [SerializeField] private float m_candidatePostInterval = 1.0f;

        // ── Internal observation model ────────────────────────────────────────

        private class IngredientObservation
        {
            public string Name;
            public float BestScore;
            public int SeenCount;
            public float LastSeenTime;
        }

        private readonly Dictionary<string, IngredientObservation> m_candidates = new();
        private readonly Dictionary<string, IngredientObservation> m_confirmed = new();
        private readonly List<string> m_confirmedInstances = new();

        private float m_lastCandidatePost = 0f;
        private HashSet<string> m_lastPostedCandidates = new();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns stable candidate ingredient names (SeenCount >= threshold).</summary>
        public IReadOnlyCollection<string> GetCandidateIngredientNames()
        {
            var result = new List<string>();
            foreach (var kv in m_candidates)
                if (kv.Value.SeenCount >= m_stabilityThreshold)
                    result.Add(kv.Key);
            return result;
        }

        /// <summary>Returns all confirmed ingredient names (preserves duplicates for per-instance weighing).</summary>
        public IReadOnlyCollection<string> GetConfirmedIngredientNames()
        {
            return new List<string>(m_confirmedInstances);
        }

        /// <summary>
        /// Called by SentisInferenceRunManager every inference frame.
        /// Updates candidate observations and POSTs to backend if the stable set changed.
        /// </summary>
        public void UpdateCandidates(List<DetectionResult> detections)
        {
            float now = Time.time;

            // Group by class name, keep highest score per class in this frame.
            var frameScores = new Dictionary<string, float>();
            foreach (var d in detections)
            {
                if (string.IsNullOrEmpty(d.ClassName)) continue;
                string name = d.ClassName.ToLower().Trim();
                if (!frameScores.ContainsKey(name) || d.Score > frameScores[name])
                    frameScores[name] = d.Score;
            }

            // Update existing observations or add new ones.
            foreach (var kv in frameScores)
            {
                if (m_candidates.TryGetValue(kv.Key, out var obs))
                {
                    obs.SeenCount++;
                    obs.BestScore = Mathf.Max(obs.BestScore, kv.Value);
                    obs.LastSeenTime = now;
                }
                else
                {
                    m_candidates[kv.Key] = new IngredientObservation
                    {
                        Name = kv.Key,
                        BestScore = kv.Value,
                        SeenCount = 1,
                        LastSeenTime = now,
                    };
                }
            }

            // Expire stale unconfirmed candidates.
            var toRemove = new List<string>();
            foreach (var kv in m_candidates)
                if (!m_confirmed.ContainsKey(kv.Key) && now - kv.Value.LastSeenTime > m_candidateTimeout)
                    toRemove.Add(kv.Key);
            foreach (var k in toRemove)
                m_candidates.Remove(k);

            // POST candidate list when the stable set changes (rate-limited).
            if (now - m_lastCandidatePost >= m_candidatePostInterval)
            {
                var stableNow = new HashSet<string>(GetCandidateIngredientNames());
                if (!stableNow.SetEquals(m_lastPostedCandidates))
                {
                    m_lastPostedCandidates = stableNow;
                    m_lastCandidatePost = now;
                    m_backendClient?.PostCandidateIngredients(new List<string>(stableNow));
                }
            }
        }

        /// <summary>
        /// Called by DetectionManager when the user presses A / pinches to save markers.
        /// Promotes the given names to confirmed and immediately POSTs to backend.
        /// </summary>
        public void ConfirmVisibleIngredients(IEnumerable<string> ingredientNames)
        {
            float now = Time.time;
            m_confirmedInstances.Clear();

            foreach (var name in ingredientNames)
            {
                string key = name.ToLower().Trim().Replace("_", " ");
                if (string.IsNullOrEmpty(key)) continue;

                // Preserve duplicates for per-instance weighing.
                m_confirmedInstances.Add(key);

                // Maintain unique dictionary for candidate/legacy display.
                if (!m_confirmed.ContainsKey(key))
                {
                    m_candidates.TryGetValue(key, out var obs);
                    m_confirmed[key] = new IngredientObservation
                    {
                        Name = key,
                        BestScore = obs != null ? obs.BestScore : 1f,
                        SeenCount = 1,
                        LastSeenTime = now,
                    };
                }
            }

            m_backendClient?.PostConfirmedIngredients(new List<string>(m_confirmedInstances));
        }

        /// <summary>
        /// Called by DetectionManager when the user presses B / middle pinch to clear markers.
        /// Clears all candidate and confirmed state and notifies the backend.
        /// </summary>
        public void ClearAll()
        {
            m_candidates.Clear();
            m_confirmed.Clear();
            m_confirmedInstances.Clear();
            m_lastPostedCandidates.Clear();
            m_backendClient?.PostCandidateIngredients(new List<string>());
            m_backendClient?.PostConfirmedIngredients(new List<string>());
        }

        /// <summary>
        /// Rebuilds the confirmed set from the markers currently present in the scene.
        /// Call this after deleting a marker in review mode to keep the backend in sync.
        /// </summary>
        public void RebuildConfirmedFromMarkers(IReadOnlyList<DetectionSpawnMarkerAnim> markers)
        {
            m_confirmed.Clear();
            m_confirmedInstances.Clear();

            float now = Time.time;
            foreach (var marker in markers)
            {
                if (marker == null) continue;
                string label = marker.GetYoloClassName()?.Trim().ToLower().Replace("_", " ");
                if (string.IsNullOrEmpty(label)) continue;

                // One instance entry per marker (preserves duplicates).
                m_confirmedInstances.Add(label);

                if (!m_confirmed.ContainsKey(label))
                {
                    m_confirmed[label] = new IngredientObservation
                    {
                        Name = label,
                        BestScore = 1f,
                        SeenCount = m_stabilityThreshold,
                        LastSeenTime = now,
                    };
                }
            }

            m_backendClient?.PostConfirmedIngredients(new List<string>(m_confirmedInstances));
        }
    }
}
