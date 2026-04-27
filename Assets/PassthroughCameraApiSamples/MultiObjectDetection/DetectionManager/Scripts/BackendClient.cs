// Copyright (c) Meta Platforms, Inc. and affiliates.
// AI Nutritious Culinary Assistant — Backend HTTP client for Quest.
//
// Centralizes all backend communication.
// All methods fire-and-forget coroutines; errors are logged but never crash the app.
//
// For Quest device testing set m_baseUrl to the LAN IP of the machine running uvicorn,
// e.g. "http://192.168.1.42:8000"  (not localhost — Quest can't reach that).

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    public class BackendClient : MonoBehaviour
    {
        // [SerializeField] private string m_baseUrl = "http://192.168.4.28:8000";    // 508 E John
        [SerializeField] private string m_baseUrl = "http://192.168.197.86:8000";   // Triangle
        // [SerializeField] private string m_baseUrl = "http://10.193.0.17:8000";      // ECEB IllinoisNet
        // [SerializeField] private string m_baseUrl = "http://172.20.10.2:8000";      // Phone hotspot
        [SerializeField] private bool m_logRequests = true;

        // ── Shared JSON payload models ─────────────────────────────────────────

        [System.Serializable]
        private class IngredientListPayload
        {
            public List<string> ingredients;
        }

        [System.Serializable]
        private class VisionDetectionJson
        {
            public string label;
            public int    class_id;
            public float  confidence;
            public float[] bbox_xywh;  // [x, y, w, h] in pixels
        }

        [System.Serializable]
        private class VisionFrameJson
        {
            public int   frame_id;
            public float quest_send_ms;
            public float inference_ms;
            public float fps;
            public VisionDetectionJson[] detections;
        }

        [System.Serializable]
        private class RvEventJson
        {
            public string event_id;
            public string event_type;
            public float  quest_send_ms;
        }

        [System.Serializable]
        private class ReviewIndexPayload
        {
            public int index;
        }

        [System.Serializable]
        private class RecordWeightPayload
        {
            public int index;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>POST /candidate_ingredients — live (non-confirmed) detections.</summary>
        public void PostCandidateIngredients(List<string> ingredientNames)
            => StartCoroutine(Post("/candidate_ingredients",
                new IngredientListPayload { ingredients = ingredientNames }));

        /// <summary>POST /ingredients_confirmed — user-confirmed ingredient list (preserves duplicates).</summary>
        public void PostConfirmedIngredients(List<string> ingredientNames)
            => StartCoroutine(Post("/ingredients_confirmed",
                new IngredientListPayload { ingredients = ingredientNames }));

        /// <summary>POST /generate_recipe — trigger recipe generation from confirmed list (legacy).</summary>
        public void GenerateRecipe(IReadOnlyCollection<string> confirmedIngredients)
            => StartCoroutine(Post("/generate_recipe",
                new IngredientListPayload { ingredients = new List<string>(confirmedIngredients) }));

        /// <summary>POST /generate_recipe — trigger recipe generation from backend-stateful confirmed list.</summary>
        public void GenerateRecipeFromConfirmed()
            => StartCoroutine(Post("/generate_recipe", null));

        // ── Ingredient review flow ─────────────────────────────────────────────

        /// <summary>POST /start_ingredient_review — begin the per-ingredient weigh-in phase.</summary>
        public void StartIngredientReview()
            => StartCoroutine(Post("/start_ingredient_review", null));

        /// <summary>GET /ingredient_review — poll current review state.</summary>
        public void GetIngredientReview(System.Action<string> onSuccess)
            => StartCoroutine(Get("/ingredient_review", onSuccess));

        /// <summary>POST /ingredient_review_index — navigate to a specific ingredient index.</summary>
        public void SetIngredientReviewIndex(int index)
            => StartCoroutine(Post("/ingredient_review_index", new ReviewIndexPayload { index = index }));

        /// <summary>POST /record_ingredient_weight — record live scale weight for the indexed ingredient.</summary>
        public void RecordIngredientWeight(int index)
            => StartCoroutine(Post("/record_ingredient_weight", new RecordWeightPayload { index = index }));

        /// <summary>POST /tare — tare the load cell.</summary>
        public void TareScale()
            => StartCoroutine(Post("/tare", null));

        /// <summary>GET /current_step — returns the active recipe step as JSON string.</summary>
        public void GetCurrentStep(System.Action<string> onSuccess)
            => StartCoroutine(Get("/current_step", onSuccess));

        /// <summary>POST /advance_step — move to the next recipe step.</summary>
        public void AdvanceStep()
            => StartCoroutine(Post("/advance_step", null));

        /// <summary>POST /dispense_step — trigger ESP32 dispense for current step.</summary>
        public void DispenseStep()
            => StartCoroutine(Post("/dispense_step", null));

        /// <summary>POST /reset — reset backend to idle state.</summary>
        public void ResetState()
            => StartCoroutine(Post("/reset", null));

        /// <summary>
        /// POST /vision_frame — send full structured YOLO output to the backend.
        /// Call this from SentisInferenceRunManager after NMS, throttled (e.g. 1 Hz).
        /// BoundingBox is xyxy; this method converts to xywh for the API.
        /// </summary>
        public void PostVisionFrame(IReadOnlyList<DetectionResult> detections,
                                    int frameId, float fps, float inferenceMs)
        {
            var dJson = new VisionDetectionJson[detections.Count];
            for (int i = 0; i < detections.Count; i++)
            {
                var d  = detections[i];
                var bb = d.BoundingBox;  // xyxy
                dJson[i] = new VisionDetectionJson
                {
                    label      = d.ClassName,
                    class_id   = d.ClassId,
                    confidence = d.Score,
                    bbox_xywh  = new float[]
                    {
                        bb.x,
                        bb.y,
                        bb.z - bb.x,   // width
                        bb.w - bb.y,   // height
                    },
                };
            }

            var frame = new VisionFrameJson
            {
                frame_id      = frameId,
                quest_send_ms = (float)(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                inference_ms  = inferenceMs,
                fps           = fps,
                detections    = dJson,
            };
            StartCoroutine(Post("/vision_frame", frame));
        }

        /// <summary>
        /// POST /rv_event — send a timestamped latency probe to the backend.
        /// Measures Quest-send → backend-receive round-trip for R&V requirement 1.
        /// </summary>
        public void PostRvEvent(string eventType)
        {
            var ev = new RvEventJson
            {
                event_id      = $"quest_evt_{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                event_type    = eventType,
                quest_send_ms = (float)(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            };
            StartCoroutine(Post("/rv_event", ev));
        }

        // ── HTTP helpers ────────────────────────────────────────────────────────

        private IEnumerator Post(string path, object payload)
        {
            string url = m_baseUrl.TrimEnd('/') + path;
            string body = payload != null ? JsonUtility.ToJson(payload) : "{}";

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (m_logRequests)
                Debug.Log($"[BackendClient] POST {url}  body={body}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[BackendClient] POST {path} failed: {req.error}");
            else if (m_logRequests)
                Debug.Log($"[BackendClient] POST {path} → {req.responseCode}");
        }

        private IEnumerator Get(string path, System.Action<string> onSuccess)
        {
            string url = m_baseUrl.TrimEnd('/') + path;

            using var req = UnityWebRequest.Get(url);

            if (m_logRequests)
                Debug.Log($"[BackendClient] GET {url}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[BackendClient] GET {path} failed: {req.error}");
            else
            {
                if (m_logRequests)
                    Debug.Log($"[BackendClient] GET {path} → {req.responseCode}");
                onSuccess?.Invoke(req.downloadHandler.text);
            }
        }
    }
}
