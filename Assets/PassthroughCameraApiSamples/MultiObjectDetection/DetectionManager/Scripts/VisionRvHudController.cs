// NuChef — VisionRvHudController
//
// Togglable in-headset debug / R&V overlay that proves every ECE 445 vision
// requirement without cluttering the main cooking HUD.
//
// ── Recommended scene hierarchy ───────────────────────────────────────────────
//
//   [RvHudCanvas]                  ← World-Space Canvas, scale 0.001
//     ├── HudFollowCamera          ← add HudFollowCamera component for head-follow
//     ├── VisionRvHudController    ← this script
//     └── Panel
//           ├── InferenceText      ← live inference stats
//           ├── DetectionText      ← per-detection list
//           ├── PlannerText        ← planner / backend state
//           └── CommText           ← communication / latency
//
// Canvas settings:
//   Render Mode   : World Space
//   Width / Height: 720 × 480  (pixels)
//   Scale         : 0.001  → panel is 72 cm × 48 cm in world space
//
// Text components: UnityEngine.UI.Text (legacy).
// Swap field types to TMPro.TextMeshProUGUI if using TMP.
//
// ── Usage ─────────────────────────────────────────────────────────────────────
//
//   // Called each inference frame by SentisInferenceRunManager:
//   rvHud.SetInferenceStats(frameId, fps, inferenceMs, detectionCount,
//                           maxDetections, scoreThreshold, iouThreshold);
//   rvHud.SetDetections(detections);          // List<DetectionResult>
//
//   // Called after a backend round-trip:
//   rvHud.SetBackendState(candidates, confirmed, demoState);
//
//   // Called after latency measurements:
//   rvHud.SetCommStats(questConnected, esp32Connected,
//                      lastLatencyMs, burstP95Ms, droppedEvents);
//
//   // Toggle on / off (bind to controller button):
//   rvHud.ToggleVisible();

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class VisionRvHudController : MonoBehaviour
    {
        // ── Inspector – UI references ──────────────────────────────────────────

        [Header("Root to show/hide (must NOT be same object as this script)")]
        [SerializeField] private GameObject m_root;

        [Header("Text blocks (UnityEngine.UI.Text)")]
        [SerializeField] private Text m_inferenceText;  // live inference stats
        [SerializeField] private Text m_detectionText;  // per-detection list
        [SerializeField] private Text m_plannerText;    // planner / backend state
        [SerializeField] private Text m_commText;       // communication / latency

        [Header("Start visible?")]
        [SerializeField] private bool m_startVisible = false;

        [Header("Max detections constant (match SentisInferenceRunManager)")]
        [SerializeField] private int m_maxDetections = 20;

        // ── Private state ──────────────────────────────────────────────────────

        // Inference telemetry
        private int   _frameId;
        private float _fps;
        private float _inferenceMs;
        private int   _detectionCount;
        private float _scoreThreshold;
        private float _iouThreshold;

        // Detection list
        private IReadOnlyList<DetectionResult> _detections;

        // Planner
        private string _candidates      = "—";
        private string _confirmed       = "—";
        private string _demoState       = "—";
        private string _publishStatus   = "—";

        // Comm
        private bool  _questConnected  = false;
        private bool  _esp32Connected  = false;
        private float _lastLatencyMs   = -1f;
        private float _burstP95Ms      = -1f;
        private int   _droppedEvents   = 0;

        // Redraw only when dirty
        private bool _dirty = true;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (m_root == null)
                Debug.LogWarning("[VisionRvHudController] m_root is not assigned. " +
                                 "Assign the Canvas/Panel to m_root in the Inspector.");
            SetVisible(m_startVisible);
        }

        private void LateUpdate()
        {
            if (m_root == null || !m_root.activeSelf || !_dirty) return;
            _dirty = false;
            Redraw();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Call every inference frame from SentisInferenceRunManager.
        /// </summary>
        public void SetInferenceStats(int frameId, float fps, float inferenceMs,
                                      int detectionCount, int maxDetections,
                                      float scoreThreshold, float iouThreshold)
        {
            _frameId        = frameId;
            _fps            = fps;
            _inferenceMs    = inferenceMs;
            _detectionCount = detectionCount;
            m_maxDetections = maxDetections;
            _scoreThreshold = scoreThreshold;
            _iouThreshold   = iouThreshold;
            _dirty = true;
        }

        /// <summary>
        /// Call every inference frame with the post-NMS detection list.
        /// </summary>
        public void SetDetections(IReadOnlyList<DetectionResult> detections)
        {
            _detections = detections;
            _dirty = true;
        }

        /// <summary>
        /// Call after any backend round-trip to update planner state.
        /// Pass null to leave current values unchanged.
        /// </summary>
        public void SetBackendState(IReadOnlyList<string> candidates,
                                    IReadOnlyList<string> confirmed,
                                    string demoState,
                                    string publishStatus = "OK")
        {
            if (candidates != null)
                _candidates = candidates.Count > 0 ? string.Join(", ", candidates) : "(none)";
            if (confirmed != null)
                _confirmed = confirmed.Count > 0 ? string.Join(", ", confirmed) : "(none)";
            if (demoState != null)
                _demoState = demoState;
            _publishStatus = publishStatus ?? "OK";
            _dirty = true;
        }

        /// <summary>
        /// Call after any latency measurement or connection-state change.
        /// Use -1 for unknown float values.
        /// </summary>
        public void SetCommStats(bool questConnected, bool esp32Connected,
                                 float lastLatencyMs = -1f, float burstP95Ms = -1f,
                                 int droppedEvents = 0)
        {
            _questConnected = questConnected;
            _esp32Connected = esp32Connected;
            _lastLatencyMs  = lastLatencyMs;
            _burstP95Ms     = burstP95Ms;
            _droppedEvents  = droppedEvents;
            _dirty = true;
        }

        /// <summary>Show or hide the R&amp;V overlay.</summary>
        public void SetVisible(bool visible)
        {
            if (m_root != null) m_root.SetActive(visible);
            _dirty = true;
        }

        /// <summary>Toggle R&amp;V overlay on/off. Bind to a controller button.</summary>
        public void ToggleVisible()
        {
            if (m_root != null) SetVisible(!m_root.activeSelf);
        }

        // ── Rendering ──────────────────────────────────────────────────────────

        private void Redraw()
        {
            RedrawInference();
            RedrawDetections();
            RedrawPlanner();
            RedrawComm();
        }

        private void RedrawInference()
        {
            if (m_inferenceText == null) return;
            var capPass = _detectionCount <= m_maxDetections ? "[PASS]" : "[FAIL]";
            m_inferenceText.text =
                "─ Live Inference ───────────────────\n" +
                $"Frame   : {_frameId}\n" +
                $"FPS     : {_fps:F1}\n" +
                $"Infer   : {_inferenceMs:F0} ms\n" +
                $"Count   : {_detectionCount} / {m_maxDetections}  {capPass}\n" +
                $"Conf thr: {_scoreThreshold:F2}\n" +
                $"NMS IoU : {_iouThreshold:F2}";
        }

        private void RedrawDetections()
        {
            if (m_detectionText == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("─ Detections ───────────────────────");
            if (_detections == null || _detections.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                for (int i = 0; i < _detections.Count; i++)
                {
                    var d  = _detections[i];
                    var bb = d.BoundingBox;      // xyxy
                    int bx = Mathf.RoundToInt(bb.x);
                    int by = Mathf.RoundToInt(bb.y);
                    int bw = Mathf.RoundToInt(bb.z - bb.x);
                    int bh = Mathf.RoundToInt(bb.w - bb.y);
                    sb.AppendLine(
                        $"{i + 1,2}. {d.ClassName,-14} {d.Score:0.00}  [{bx},{by},{bw},{bh}]");
                }
            }
            m_detectionText.text = sb.ToString();
        }

        private void RedrawPlanner()
        {
            if (m_plannerText == null) return;
            m_plannerText.text =
                "─ Planner Interface ─────────────────\n" +
                $"Publish   : {_publishStatus}\n" +
                $"Candidates: {_candidates}\n" +
                $"Confirmed : {_confirmed}\n" +
                $"State     : {_demoState}";
        }

        private void RedrawComm()
        {
            if (m_commText == null) return;
            var questStr = _questConnected ? "connected" : "DISCONNECTED";
            var esp32Str = _esp32Connected ? "connected" : "DISCONNECTED";
            var latStr   = _lastLatencyMs >= 0 ? $"{_lastLatencyMs:F0} ms" : "—";
            var p95Str   = _burstP95Ms    >= 0 ? $"{_burstP95Ms:F0} ms"   : "—";
            var p95Flag  = _burstP95Ms    >= 0
                ? (_burstP95Ms <= 150f ? " [PASS]" : " [FAIL]") : "";
            var dropFlag = _droppedEvents == 0 ? " [PASS]" : " [FAIL]";
            m_commText.text =
                "─ Communication ─────────────────────\n" +
                $"Quest     : {questStr}\n" +
                $"ESP32/BLE : {esp32Str}\n" +
                $"Backend   : connected\n" +
                $"Latency   : {latStr}\n" +
                $"p95 burst : {p95Str}{p95Flag}\n" +
                $"Dropped   : {_droppedEvents}{dropFlag}";
        }
    }
}
