// NuChef — RecipeGuidanceManager
//
// Main coordinator for AR recipe guidance on the Quest side.
//
// Responsibilities:
//   - Poll /current_step every <m_pollInterval> seconds
//   - Detect step_id changes — only update cues when the step actually changes
//   - Resolve target ingredient transforms via SceneObjectRegistry
//   - Hand the step + resolved targets to CueRenderer
//   - Watch completion_mode and advance the step when the condition is met
//   - Expose IsGuiding so DetectionManager can gate its own input handling
//
// Wire in Inspector:
//   m_backendClient, m_sceneRegistry, m_cueRenderer, m_hudController (optional)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class RecipeGuidanceManager : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Dependencies")]
        [SerializeField] private BackendClient m_backendClient;
        [SerializeField] private SceneObjectRegistry m_sceneRegistry;
        [SerializeField] private CueRenderer m_cueRenderer;
        [SerializeField] private GuidanceHudController m_hudController;   // optional

        [Header("Polling")]
        [SerializeField] private float m_pollInterval = 0.3f;

        [Header("Completion — auto-advance delay (seconds)")]
        [SerializeField] private float m_autoAdvanceDelay = 1.5f;

        // ── Public state ───────────────────────────────────────────────────────

        /// <summary>True while a recipe is actively being guided step-by-step.</summary>
        public static bool IsGuiding { get; private set; }

        /// <summary>Last successfully parsed step payload.</summary>
        public RecipeStepPayload CurrentStep { get; private set; }

        // ── Private state ──────────────────────────────────────────────────────

        private string m_currentStepId = string.Empty;
        private bool m_waitingForCompletion;
        private Coroutine m_completionWatcher;
        private Coroutine m_pollCoroutine;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void OnEnable()
        {
            m_pollCoroutine = StartCoroutine(PollLoop());
        }

        private void OnDisable()
        {
            if (m_pollCoroutine != null) StopCoroutine(m_pollCoroutine);
            IsGuiding = false;
        }

        private void Update()
        {
            if (!IsGuiding) return;
            if (CurrentStep == null) return;

            // ── A / index-pinch → user confirms current step ───────────────────
            if (InputManager.IsButtonADownOrPinchStarted())
            {
                if (CurrentStep.completion_mode == "user_confirm" && m_waitingForCompletion)
                {
                    Debug.Log("[RecipeGuidanceManager] User confirmed step.");
                    AdvanceStep();
                }
            }
        }

        // ── Polling loop ───────────────────────────────────────────────────────

        private IEnumerator PollLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(m_pollInterval);
                m_backendClient?.GetCurrentStep(OnStepReceived);
            }
        }

        // ── Response handling ──────────────────────────────────────────────────

        private void OnStepReceived(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // The backend can return {"step": null, "demo_state": "..."} when no recipe is
            // loaded yet.  Detect that by checking whether step_id parses to a non-empty string.
            RecipeStepPayload payload;
            try
            {
                payload = JsonUtility.FromJson<RecipeStepPayload>(json);
            }
            catch
            {
                Debug.LogWarning("[RecipeGuidanceManager] Failed to parse /current_step JSON.");
                return;
            }

            if (payload == null || !payload.IsValid)
            {
                // No active recipe — clear guidance if we were guiding
                if (IsGuiding)
                {
                    Debug.Log("[RecipeGuidanceManager] No active recipe — clearing guidance.");
                    ClearGuidance();
                }
                return;
            }

            // ── step_id gate: only act on changes ─────────────────────────────
            if (payload.step_id == m_currentStepId)
            {
                // Same step — update live HUD values (grams progress etc.) but no cue rebuild
                CurrentStep = payload;
                m_hudController?.UpdateLiveStatus(payload.step_status);
                return;
            }

            // ── New step received ──────────────────────────────────────────────
            Debug.Log($"[RecipeGuidanceManager] Step changed: {m_currentStepId} → {payload.step_id}  action={payload.action}");
            ApplyStep(payload);
        }

        // ── Apply a new step ───────────────────────────────────────────────────

        private void ApplyStep(RecipeStepPayload step)
        {
            // Stop any running completion watcher
            if (m_completionWatcher != null)
            {
                StopCoroutine(m_completionWatcher);
                m_completionWatcher = null;
            }

            m_currentStepId = step.step_id;
            CurrentStep = step;
            IsGuiding = true;
            m_waitingForCompletion = false;

            // ── Resolve primary target transform ───────────────────────────────
            Transform primaryTarget = null;
            if (step.targets != null && step.targets.Count > 0)
            {
                var t = step.targets[0];
                if (t.selector == "label" && !t.IsEmpty)
                    primaryTarget = m_sceneRegistry?.ResolveTarget(t.value);
            }

            // ── Resolve destination transform (move steps) ─────────────────────
            Transform destinationTarget = null;
            if (step.destination != null && !step.destination.IsEmpty && step.destination.selector == "label")
                destinationTarget = m_sceneRegistry?.ResolveTarget(step.destination.value);

            // ── Update HUD ────────────────────────────────────────────────────
            m_hudController?.ApplyStep(step);

            // ── Tell CueRenderer what to show ──────────────────────────────────
            m_cueRenderer?.ApplyRenderPlan(step, primaryTarget, destinationTarget);

            // ── Start the appropriate completion watcher ───────────────────────
            m_completionWatcher = step.completion_mode switch
            {
                "user_confirm"  => null,   // handled in Update() via input
                "dispense_done" => StartCoroutine(WatchDispenseDone()),
                "timer_done"    => StartCoroutine(WatchTimerDone(step.duration_s)),
                "auto"          => StartCoroutine(WatchAutoAdvance()),
                _               => null,
            };

            m_waitingForCompletion = true;

            if (step.action == "complete")
            {
                Debug.Log("[RecipeGuidanceManager] Recipe complete!");
                IsGuiding = false;
            }
        }

        // ── Completion watchers ────────────────────────────────────────────────

        /// <summary>Watches step_status.dispense_status until it becomes "done".</summary>
        private IEnumerator WatchDispenseDone()
        {
            Debug.Log("[RecipeGuidanceManager] Waiting for dispense_done...");
            while (true)
            {
                yield return new WaitForSeconds(m_pollInterval);
                if (CurrentStep?.step_status?.dispense_status == "done")
                {
                    Debug.Log("[RecipeGuidanceManager] Dispense done — advancing.");
                    AdvanceStep();
                    yield break;
                }
            }
        }

        /// <summary>Local countdown for wait steps.</summary>
        private IEnumerator WatchTimerDone(float seconds)
        {
            float remaining = seconds > 0 ? seconds : 5f;
            Debug.Log($"[RecipeGuidanceManager] Timer wait: {remaining}s");
            while (remaining > 0f)
            {
                yield return new WaitForSeconds(1f);
                remaining -= 1f;
                m_hudController?.UpdateTimerDisplay(remaining);
            }
            Debug.Log("[RecipeGuidanceManager] Timer done — advancing.");
            AdvanceStep();
        }

        /// <summary>Short pause then auto-advance (used for "complete" or simple auto steps).</summary>
        private IEnumerator WatchAutoAdvance()
        {
            yield return new WaitForSeconds(m_autoAdvanceDelay);
            AdvanceStep();
        }

        // ── Step advancement ───────────────────────────────────────────────────

        private void AdvanceStep()
        {
            m_waitingForCompletion = false;
            m_cueRenderer?.ClearCues();
            m_backendClient?.AdvanceStep();
            // Next poll will detect the new step_id and call ApplyStep()
        }

        // ── Clear guidance ─────────────────────────────────────────────────────

        private void ClearGuidance()
        {
            if (m_completionWatcher != null)
            {
                StopCoroutine(m_completionWatcher);
                m_completionWatcher = null;
            }
            m_cueRenderer?.ClearCues();
            m_hudController?.Hide();
            m_currentStepId = string.Empty;
            CurrentStep = null;
            IsGuiding = false;
            m_waitingForCompletion = false;
        }

        // ── Public helpers (for external scripts / debug UI) ───────────────────

        /// <summary>Force an immediate step poll, bypassing the interval timer.</summary>
        public void ForcePoll() => m_backendClient?.GetCurrentStep(OnStepReceived);

        /// <summary>
        /// Stop guidance and clear all cues. Called by B button via DetectionManager.
        /// Does NOT reset the backend — the recipe stays loaded.
        /// </summary>
        public void StopGuidance() => ClearGuidance();

        /// <summary>Manually request a dispense for the current step.</summary>
        public void RequestDispense() => m_backendClient?.DispenseStep();
    }
}
