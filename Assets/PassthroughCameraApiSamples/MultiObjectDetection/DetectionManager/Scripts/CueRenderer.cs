// NuChef — CueRenderer
//
// Owns all AR cue GameObjects (highlight, arrow, ghost hand, etc.).
// Receives a RenderPlan + resolved target transforms from RecipeGuidanceManager
// and spawns / repositions / destroys the appropriate prefabs.
//
// Preset name → prefab slot mapping (set in Inspector):
//   soft_highlight / pulse_highlight   → m_highlightPrefab
//   arrow_to_target                    → m_arrowPrefab
//   arrow_dispenser_to_target          → m_arrowDispenserPrefab  (falls back to m_arrowPrefab)
//   ghost_hand_grab / move / sprinkle  → m_ghostHandPrefab       (single prefab for now)
//   timer_ring                         → m_timerRingPrefab
//   success_pulse                      → m_successPulsePrefab
//   text_panel_only                    → no extra cue spawned

using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class CueRenderer : MonoBehaviour
    {
        // ── Inspector — prefab slots ───────────────────────────────────────────

        [Header("Cue Prefabs (assign in Inspector — all optional)")]
        [SerializeField] private GameObject m_highlightPrefab;
        [SerializeField] private GameObject m_arrowPrefab;
        [SerializeField] private GameObject m_arrowDispenserPrefab;  // falls back to m_arrowPrefab
        [SerializeField] private GameObject m_ghostHandPrefab;
        [SerializeField] private GameObject m_timerRingPrefab;
        [SerializeField] private GameObject m_successPulsePrefab;

        // ── Private state ──────────────────────────────────────────────────────

        private readonly List<GameObject> m_activeCues = new();

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Spawn/update cues based on the render_plan in <paramref name="step"/>.
        /// <paramref name="primaryTarget"/> is the world-space Transform to pin cues to.
        /// <paramref name="destinationTarget"/> is only used for move steps.
        /// </summary>
        public void ApplyRenderPlan(RecipeStepPayload step, Transform primaryTarget, Transform destinationTarget)
        {
            ClearCues();

            if (step?.render_plan == null) return;

            var plan = step.render_plan;

            // ── Focus preset ───────────────────────────────────────────────────
            if (primaryTarget != null)
            {
                switch (plan.focus_preset)
                {
                    case "soft_highlight":
                    case "pulse_highlight":
                        SpawnHighlight(primaryTarget, plan.focus_preset == "pulse_highlight");
                        break;
                    case "success_pulse":
                        SpawnPrefabAt(m_successPulsePrefab, primaryTarget, followTarget: true);
                        break;
                    // "text_panel_only" → no cue spawned
                }
            }

            // ── Assist presets ─────────────────────────────────────────────────
            if (plan.assist_presets != null)
            {
                foreach (var preset in plan.assist_presets)
                {
                    switch (preset)
                    {
                        case "arrow_to_target":
                            if (primaryTarget != null)
                                SpawnArrow(primaryTarget, isPulse: false);
                            break;

                        case "arrow_dispenser_to_target":
                            if (primaryTarget != null)
                                SpawnArrow(primaryTarget, isPulse: false, useDispenserArrow: true);
                            break;

                        case "ghost_hand_grab":
                        case "ghost_hand_move":
                        case "ghost_hand_sprinkle":
                            if (primaryTarget != null)
                                SpawnPrefabAt(m_ghostHandPrefab, primaryTarget, followTarget: true);
                            break;

                        case "timer_ring":
                            if (primaryTarget != null)
                                SpawnPrefabAt(m_timerRingPrefab, primaryTarget, followTarget: true);
                            break;
                    }
                }
            }
        }

        /// <summary>Destroy all active cue GameObjects.</summary>
        public void ClearCues()
        {
            foreach (var go in m_activeCues)
            {
                if (go != null) Destroy(go);
            }
            m_activeCues.Clear();
        }

        // ── Private spawn helpers ──────────────────────────────────────────────

        private void SpawnHighlight(Transform target, bool pulse)
        {
            if (m_highlightPrefab == null)
            {
                Debug.LogWarning("[CueRenderer] m_highlightPrefab not assigned.");
                return;
            }
            var go = Instantiate(m_highlightPrefab, target.position, Quaternion.identity);
            var ctrl = go.GetComponent<HighlightCueController>();
            if (ctrl != null)
            {
                ctrl.SetTarget(target);
                ctrl.SetPulse(pulse);
            }
            else
            {
                // If no controller, just parent it to follow the target
                go.transform.SetParent(target, worldPositionStays: true);
            }
            m_activeCues.Add(go);
        }

        private void SpawnArrow(Transform target, bool isPulse, bool useDispenserArrow = false)
        {
            var prefab = useDispenserArrow
                ? (m_arrowDispenserPrefab != null ? m_arrowDispenserPrefab : m_arrowPrefab)
                : m_arrowPrefab;

            if (prefab == null)
            {
                Debug.LogWarning("[CueRenderer] Arrow prefab not assigned.");
                return;
            }

            var go = Instantiate(prefab, target.position, Quaternion.identity);
            var ctrl = go.GetComponent<ArrowCueController>();
            if (ctrl != null)
                ctrl.SetTarget(target);
            else
                go.transform.SetParent(target, worldPositionStays: true);

            m_activeCues.Add(go);
        }

        private void SpawnPrefabAt(GameObject prefab, Transform target, bool followTarget)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, target.position, Quaternion.identity);
            if (followTarget)
                go.transform.SetParent(target, worldPositionStays: true);
            m_activeCues.Add(go);
        }

        private void OnDestroy() => ClearCues();
    }
}
