// AI Nutritious Culinary Assistant — Ingredient weight-log review UI manager.
//
// Responsibilities:
//   - Poll GET /ingredient_review while weighing is active.
//   - Show selected ingredient, live scale weight, and logged weights.
//   - X = previous ingredient.
//   - Y = next ingredient.
//   - A = record current live backend weight for the selected ingredient.
//   - When all_complete, automatically calls GenerateRecipeFromConfirmed().
//
// Inspector setup:
//   - Add this to a persistent scene GameObject.
//   - Assign BackendClient, a root HUD panel, and a Text field.
//   - Leave the HUD panel disabled; BeginReview() enables it.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class IngredientReviewManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private BackendClient m_backendClient;
        [SerializeField] private DetectionManager m_detectionManager;

        [Header("Spatial Cues")]
        [Tooltip("Assign HighlightCuePrefab from the Prefabs folder.")]
        [SerializeField] private GameObject m_highlightPrefab;
        [Tooltip("Assign ArrowCuePrefab from the Prefabs folder.")]
        [SerializeField] private GameObject m_arrowPrefab;

        [Header("HUD Root")]
        [SerializeField] private GameObject m_reviewRoot;  // Panel — starts disabled

        [Header("Text fields (leave null to skip)")]
        [SerializeField] private Text m_titleText;         // "Ingredient Review"
        [SerializeField] private Text m_progressText;      // "2 / 3"
        [SerializeField] private Text m_ingredientText;    // "egg 2/3"
        [SerializeField] private Text m_liveWeightText;    // "Live: 51.8 g"
        [SerializeField] private Text m_loggedText;        // "Logged: 1/3"
        [SerializeField] private Text m_hintText;          // "A: log   X/Y: prev/next"

        [Header("Polling")]
        [SerializeField] private float m_pollInterval = 0.3f;

        /// <summary>True while the ingredient weigh-in review is active.</summary>
        public static bool IsReviewing { get; private set; }

        private IngredientReviewPayload m_current;
        private Coroutine m_pollCoroutine;
        private bool m_generationRequested;
        private int m_expectedIndex = -1;  // set before navigation POST; guards against stale GET responses

        // Active AR cue instances — destroyed and recreated each time the review index changes.
        private GameObject m_activeHighlight;
        private GameObject m_activeArrow;

        private void Awake()
        {
            if (m_reviewRoot == null)
                Debug.LogError("[IngredientReviewManager] m_reviewRoot is not assigned. " +
                    "This script must live on an always-active parent, NOT on the HUD panel itself.");
            else
                m_reviewRoot.SetActive(false);

            // Seed static hint text once — it never changes.
            SetText(m_titleText, "Ingredient Review");
            SetText(m_hintText, "A: log weight   X: prev   Y: next");
        }

        /// <summary>
        /// Begin the ingredient review / weigh-in phase.
        /// Called by DetectionManager when the user presses Y to leave marker-review mode.
        /// </summary>
        public void BeginReview()
        {
            IsReviewing = true;
            m_generationRequested = false;

            if (m_reviewRoot != null)
                m_reviewRoot.SetActive(true);

            // Tare the load cell so the empty tray weight is zeroed.
            m_backendClient?.TareScale();
            // Tell the backend to enter ingredient_review state.
            m_backendClient?.StartIngredientReview();

            if (m_pollCoroutine != null)
                StopCoroutine(m_pollCoroutine);
            m_pollCoroutine = StartCoroutine(PollLoop());
        }

        /// <summary>End the review and hide the HUD.</summary>
        public void EndReview()
        {
            IsReviewing = false;

            if (m_pollCoroutine != null)
            {
                StopCoroutine(m_pollCoroutine);
                m_pollCoroutine = null;
            }

            ClearCues();

            if (m_reviewRoot != null)
                m_reviewRoot.SetActive(false);
        }

        // ── Spatial cue helpers ────────────────────────────────────────────

        private void ClearCues()
        {
            if (m_activeHighlight != null) { Destroy(m_activeHighlight); m_activeHighlight = null; }
            if (m_activeArrow     != null) { Destroy(m_activeArrow);     m_activeArrow     = null; }
        }

        /// <summary>
        /// Spawn a highlight + arrow on the spatial marker that corresponds to the
        /// ingredient instance currently being weighed.
        /// Matches by label then picks the Nth marker for that label (instance_index,
        /// which is 1-based from the backend so we subtract 1 for the 0-based list walk).
        /// </summary>
        private void UpdateCues()
        {
            ClearCues();
            if (m_current?.current == null) return;
            if (m_detectionManager == null) return;

            string targetLabel = m_current.current.label?.ToLower().Trim().Replace("_", " ");
            // instance_index from backend is 1-based (egg_1, egg_2, …).
            int targetOrdinal  = m_current.current.instance_index - 1;  // convert to 0-based

            Transform markerTransform = null;
            int matchCount = 0;
            foreach (var marker in m_detectionManager.SpawnedMarkers)
            {
                if (marker == null) continue;
                string markerLabel = marker.GetYoloClassName()?.ToLower().Trim().Replace("_", " ");
                if (markerLabel != targetLabel) continue;
                if (matchCount == targetOrdinal) { markerTransform = marker.transform; break; }
                matchCount++;
            }

            if (markerTransform == null)
            {
                Debug.Log($"[IngredientReviewManager] No marker for '{targetLabel}' ordinal {targetOrdinal}.");
                return;
            }

            if (m_highlightPrefab != null)
            {
                m_activeHighlight = Instantiate(m_highlightPrefab, markerTransform.position, Quaternion.identity);
                var hlCtrl = m_activeHighlight.GetComponentInChildren<HighlightCueController>();
                if (hlCtrl != null) { hlCtrl.SetTarget(markerTransform); hlCtrl.SetPulse(true); }
            }

            if (m_arrowPrefab != null)
            {
                m_activeArrow = Instantiate(m_arrowPrefab, markerTransform.position, Quaternion.identity);
                var arrowCtrl = m_activeArrow.GetComponentInChildren<ArrowCueController>();
                arrowCtrl?.SetTarget(markerTransform);
            }
        }

        private IEnumerator PollLoop()
        {
            while (IsReviewing)
            {
                m_backendClient?.GetIngredientReview(OnReviewReceived);
                yield return new WaitForSeconds(m_pollInterval);
            }
        }

        private void Update()
        {
            if (!IsReviewing || m_current == null) return;

            // X = navigate to previous ingredient.
            if (IsButtonXDown())
            {
                int prev = Mathf.Max(0, m_current.review_index - 1);
                m_expectedIndex = prev;
                m_backendClient?.SetIngredientReviewIndex(prev);
                RefreshNow();   // don't wait for the poll interval
            }

            // Y = navigate to next ingredient.
            if (IsButtonYDown())
            {
                int next = Mathf.Min(m_current.total - 1, m_current.review_index + 1);
                m_expectedIndex = next;
                m_backendClient?.SetIngredientReviewIndex(next);
                RefreshNow();   // don't wait for the poll interval
            }

            // A / pinch = record live scale weight for the currently selected ingredient.
            if (InputManager.IsButtonADownOrPinchStarted())
            {
                m_backendClient?.RecordIngredientWeight(m_current.review_index);
                RefreshNow();   // update logged count immediately
            }
        }

        /// <summary>Fire an immediate GET /ingredient_review outside the normal poll cadence.</summary>
        private void RefreshNow()
        {
            // Small yield so the navigation POST has a chance to land first.
            StartCoroutine(RefreshAfterDelay(0.12f));
        }

        private IEnumerator RefreshAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (IsReviewing)
                m_backendClient?.GetIngredientReview(OnReviewReceived);
        }

        private void OnReviewReceived(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            IngredientReviewPayload parsed;
            try
            {
                parsed = JsonUtility.FromJson<IngredientReviewPayload>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[IngredientReviewManager] Failed to parse /ingredient_review JSON: {ex.Message}\nRaw: {json}");
                return;
            }

            if (parsed == null)
            {
                Debug.LogWarning("[IngredientReviewManager] /ingredient_review returned null payload.");
                return;
            }

            // Guard against a slow, stale response overwriting a fresher one.
            // If we already have data and the incoming payload is for an older review_index
            // while we know the user just navigated, discard it.
            if (m_current != null &&
                parsed.review_index < m_current.review_index &&
                m_expectedIndex >= 0 &&
                parsed.review_index != m_expectedIndex)
            {
                Debug.Log($"[IngredientReviewManager] Discarding stale response (got idx={parsed.review_index}, expected={m_expectedIndex}).");
                return;
            }

            m_current = parsed;
            m_expectedIndex = -1;
            UpdateHud();

            // When all ingredients are weighed, trigger recipe generation automatically.
            if (m_current != null && m_current.all_complete && !m_generationRequested)
            {
                m_generationRequested = true;
                m_backendClient?.GenerateRecipeFromConfirmed();
                EndReview();
            }
        }

        private void UpdateHud()
        {
            if (m_current == null) return;

            if (m_current.current == null)
            {
                SetText(m_ingredientText, "No ingredients to review.");
                SetText(m_progressText, "");
                SetText(m_liveWeightText, "");
                SetText(m_loggedText, "");
                return;
            }

            string displayName = string.IsNullOrEmpty(m_current.current.display_name)
                ? m_current.current.label
                : m_current.current.display_name;

            // Per-label logged count — use ingredient_summary from backend (uses is-not-None,
            // which is reliable even when weight_g == 0.0 and JsonUtility maps null→0).
            string currentLabel = m_current.current.label;
            int labelTotal  = 0;
            int labelDone   = 0;
            if (m_current.ingredient_summary != null)
            {
                var summary = m_current.ingredient_summary.Find(s => s.label == currentLabel);
                if (summary != null)
                {
                    labelTotal = summary.count;
                    labelDone  = summary.measured_count;
                }
            }
            // Fall back to iterating ingredients list if summary is missing.
            if (labelTotal == 0 && m_current.ingredients != null)
            {
                foreach (var item in m_current.ingredients)
                {
                    if (item.label != currentLabel) continue;
                    labelTotal++;
                    if (item.is_measured) labelDone++;
                }
            }
            // Final fallback to count_for_label from the current item itself.
            if (labelTotal == 0)
            {
                labelTotal = Mathf.Max(1, m_current.current.count_for_label);
                labelDone  = m_current.current.is_measured ? 1 : 0;
            }

            SetText(m_progressText,    $"{m_current.review_index + 1} / {m_current.total}");
            SetText(m_ingredientText,  displayName);
            SetText(m_liveWeightText,  $"Live: {m_current.live_weight_g:F1} g");
            SetText(m_loggedText,      $"Logged: {labelDone} / {labelTotal}");

            // Point the AR cues at the matching spatial marker.
            UpdateCues();
        }

        private static void SetText(Text field, string value)
        {
            if (field != null) field.text = value;
        }

        // Quest controller button helpers (left controller: X=Three, Y=Four).
        private static bool IsButtonXDown()
            => OVRInput.GetDown(OVRInput.Button.Three);

        private static bool IsButtonYDown()
            => OVRInput.GetDown(OVRInput.Button.Four);
    }
}
