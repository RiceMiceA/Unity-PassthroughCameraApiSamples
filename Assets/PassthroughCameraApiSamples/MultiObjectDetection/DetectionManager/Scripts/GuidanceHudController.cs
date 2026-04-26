// NuChef — GuidanceHudController
//
// Manages the in-headset HUD panel that shows:
//   - Step display text
//   - Spice name (for season steps)
//   - Target grams / current grams progress
//   - Timer countdown (for wait steps)
//
// All UI fields are optional — leave them null in the Inspector to skip that element.
// Wire to Unity UI Text (TMP or legacy) or TextMesh components in the Inspector.
//
// Text component type is UnityEngine.UI.Text (legacy).
// Swap to TMPro.TextMeshProUGUI by replacing the [SerializeField] types if using TMP.

using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class GuidanceHudController : MonoBehaviour
    {
        // ── Inspector — UI references ──────────────────────────────────────────

        [Header("HUD Canvas / Root")]
        [SerializeField] private GameObject m_hudRoot;   // toggle visibility

        [Header("Text fields (optional — leave null to skip)")]
        [SerializeField] private Text m_stepText;
        [SerializeField] private Text m_spiceText;
        [SerializeField] private Text m_gramsText;       // "X.X / Y.Y g"
        [SerializeField] private Text m_timerText;
        [SerializeField] private Text m_progressLabel;   // "Step 2 / 5"

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            // IMPORTANT: This component must be on an always-active parent GameObject.
            // m_hudRoot should be the child HUD panel that starts disabled in the Inspector.
            // If m_hudRoot is the same GameObject this script is on, disabling it prevents
            // Awake from running and the HUD will never appear.
            if (m_hudRoot == null)
            {
                Debug.LogError("[GuidanceHudController] m_hudRoot is not assigned! " +
                               "Drag the HUD panel GameObject into the m_hudRoot field. " +
                               "This script must live on an always-active parent, NOT on the HUD panel itself.");
            }

            // Warn on missing text fields so the developer knows what to wire.
            if (m_stepText == null)
                Debug.LogWarning("[GuidanceHudController] m_stepText is not assigned.");
            if (m_progressLabel == null)
                Debug.LogWarning("[GuidanceHudController] m_progressLabel is not assigned.");
        }
        // NOTE: No Start() Hide() call — the HUD panel is already disabled in the Inspector.
        // ApplyStep → Show() will enable it when the first step arrives.

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Called once when a new step begins.</summary>
        public void ApplyStep(RecipeStepPayload step)
        {
            Show();

            SetText(m_stepText, step.display_text);
            SetText(m_progressLabel, $"Step {step.step_index + 1} / {step.total_steps}");

            var hud = step.render_plan?.hud;
            bool showSpice = hud?.show_spice ?? false;
            bool showGrams = hud?.show_target_grams ?? false;
            bool showTimer = hud?.show_timer ?? false;

            if (m_spiceText != null)
            {
                m_spiceText.gameObject.SetActive(showSpice);
                if (showSpice && step.dispense != null)
                    m_spiceText.text = step.dispense.spice;
            }

            if (m_gramsText != null)
            {
                m_gramsText.gameObject.SetActive(showGrams);
                if (showGrams && step.dispense != null)
                    m_gramsText.text = $"0.0 / {step.dispense.grams:F1} g";
            }

            if (m_timerText != null)
            {
                m_timerText.gameObject.SetActive(showTimer);
                if (showTimer && step.duration_s > 0)
                    m_timerText.text = FormatTime(step.duration_s);
            }
        }

        /// <summary>Called every poll cycle to refresh live progress values.</summary>
        public void UpdateLiveStatus(StepStatus status)
        {
            if (status == null) return;

            // Grams progress
            if (m_gramsText != null && m_gramsText.gameObject.activeSelf)
            {
                float current = status.current_grams;
                float target = status.target_grams;
                string done = status.dispense_status == "done" ? " ✓" : string.Empty;
                m_gramsText.text = $"{current:F1} / {target:F1} g{done}";
            }
        }

        /// <summary>Called by the timer watcher in RecipeGuidanceManager each second.</summary>
        public void UpdateTimerDisplay(float remainingSeconds)
        {
            if (m_timerText == null || !m_timerText.gameObject.activeSelf) return;
            m_timerText.text = FormatTime(remainingSeconds);
        }

        public void Show()
        {
            if (m_hudRoot != null) m_hudRoot.SetActive(true);
        }

        public void Hide()
        {
            if (m_hudRoot != null) m_hudRoot.SetActive(false);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void SetText(Text field, string value)
        {
            if (field == null) return;
            field.text = value ?? string.Empty;
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s >= 60 ? $"{s / 60}:{s % 60:D2}" : $"{s}s";
        }
    }
}
