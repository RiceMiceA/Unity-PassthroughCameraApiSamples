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
        // [SerializeField] private string m_baseUrl = "http://192.168.197.86:8000";   // Triangle
        // [SerializeField] private string m_baseUrl = "http://10.193.0.17:8000";      // ECEB IllinoisNet
        [SerializeField] private string m_baseUrl = "http://172.20.10.2:8000";      // Phone hotspot
        [SerializeField] private bool m_logRequests = true;

        // ── Shared JSON payload models ─────────────────────────────────────────

        [System.Serializable]
        private class IngredientListPayload
        {
            public List<string> ingredients;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>POST /candidate_ingredients — live (non-confirmed) detections.</summary>
        public void PostCandidateIngredients(List<string> ingredientNames)
            => StartCoroutine(Post("/candidate_ingredients",
                new IngredientListPayload { ingredients = ingredientNames }));

        /// <summary>POST /ingredients_confirmed — user-confirmed ingredient list.</summary>
        public void PostConfirmedIngredients(List<string> ingredientNames)
            => StartCoroutine(Post("/ingredients_confirmed",
                new IngredientListPayload { ingredients = ingredientNames }));

        /// <summary>POST /generate_recipe — trigger recipe generation from confirmed list.</summary>
        public void GenerateRecipe(IReadOnlyCollection<string> confirmedIngredients)
            => StartCoroutine(Post("/generate_recipe",
                new IngredientListPayload { ingredients = new List<string>(confirmedIngredients) }));

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
