// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        // ── UX Phase ────────────────────────────────────────────────────────
        private enum QuestUxPhase { LiveScan, IngredientReview, RecipeGuidance }

        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("Placement configuration")]
        [SerializeField] private DetectionSpawnMarkerAnim m_spawnMarker;

        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("Ingredient integration")]
        [SerializeField] private IngredientInventoryManager m_ingredientInventory;
        [SerializeField] private BackendClient m_backendClient;
        [SerializeField] private RecipeGuidanceManager m_guidanceManager;

        [Header("Review mode")]
        [SerializeField] private SentisInferenceRunManager m_inferenceRunner;
        [SerializeField] private MarkerReviewRaySelector m_markerReviewRaySelector;
        [SerializeField] private IngredientReviewManager m_ingredientReviewManager;

        [Space(10)]
        public UnityEvent<int> OnObjectsIdentified;

        private readonly List<DetectionSpawnMarkerAnim> m_spawnedEntities = new();

        /// <summary>Read-only view of all currently spawned spatial markers.</summary>
        public IReadOnlyList<DetectionSpawnMarkerAnim> SpawnedMarkers => m_spawnedEntities;

        private bool m_isStarted;
        internal OVRSpatialAnchor m_spatialAnchor;
        private bool m_isHeadsetTracking;
        private QuestUxPhase m_phase = QuestUxPhase.LiveScan;
        // Used to detect the guiding true→false edge so we don't false-positive
        // on the frame Y is pressed (before IsGuiding has been set to true).
        private bool m_wasGuiding;

        private void Awake()
        {
            StartCoroutine(UpdateSpatialAnchor());
            OVRManager.TrackingLost += OnTrackingLost;
            OVRManager.TrackingAcquired += OnTrackingAcquired;
        }

        private void OnDestroy()
        {
            EraseSpatialAnchor();
            OVRManager.TrackingLost -= OnTrackingLost;
            OVRManager.TrackingAcquired -= OnTrackingAcquired;
        }

        private void OnTrackingLost() => m_isHeadsetTracking = false;
        private void OnTrackingAcquired() => m_isHeadsetTracking = true;

        private void Update()
        {
            if (!m_isStarted)
            {
                if (m_cameraAccess.IsPlaying)
                    m_isStarted = true;
                return;
            }

            // ── Return from guidance to LiveScan when guidance ends ──────────
            // Only transition back once guidance has actually *started* (m_wasGuiding
            // went true) and then stopped, to avoid the race where IsGuiding is still
            // false for a frame or two after GenerateRecipe is called.
            bool isGuiding = RecipeGuidanceManager.IsGuiding;
            if (m_phase == QuestUxPhase.RecipeGuidance)
            {
                if (isGuiding)
                    m_wasGuiding = true;

                if (m_wasGuiding && !isGuiding)
                {
                    m_wasGuiding = false;
                    m_inferenceRunner?.SetInferenceEnabled(true);
                    m_phase = QuestUxPhase.LiveScan;
                }
            }

            bool pressedA = InputManager.IsButtonADownOrPinchStarted();
            bool pressedB = InputManager.IsButtonBDownOrMiddleFingerPinchStarted();
            bool pressedX = OVRInput.GetDown(OVRInput.RawButton.X);
            bool pressedY = OVRInput.GetDown(OVRInput.RawButton.Y);

            switch (m_phase)
            {
                // ── LiveScan ─────────────────────────────────────────────────
                case QuestUxPhase.LiveScan:
                {
                    if (pressedA)
                    {
                        SpawnCurrentDetectedObjects();

                        var visibleIngredients = new List<string>();
                        foreach (var box in m_uiInference.m_boxDrawn)
                            if (!string.IsNullOrEmpty(box.ClassName))
                                visibleIngredients.Add(box.ClassName);

                        if (visibleIngredients.Count > 0)
                            m_ingredientInventory?.ConfirmVisibleIngredients(visibleIngredients);
                    }

                    if (pressedB)
                    {
                        CleanMarkers();
                    }

                    if (pressedX)
                    {
                        // Enter review mode: freeze inference, hide boxes, enable ray selector.
                        m_inferenceRunner?.SetInferenceEnabled(false);
                        m_uiInference?.ClearAllBoxes();
                        m_markerReviewRaySelector?.SetActive(true);
                        m_phase = QuestUxPhase.IngredientReview;
                    }
                    break;
                }

                // ── IngredientReview ─────────────────────────────────────────
                case QuestUxPhase.IngredientReview:
                {
                    if (pressedB)
                    {
                        if (m_markerReviewRaySelector != null &&
                            m_markerReviewRaySelector.TryDeleteHoveredMarker(out _))
                        {
                            m_ingredientInventory?.RebuildConfirmedFromMarkers(m_spawnedEntities);
                        }
                    }

                    if (pressedX)
                    {
                        // Return to live scan.
                        m_markerReviewRaySelector?.SetActive(false);
                        m_inferenceRunner?.SetInferenceEnabled(true);
                        m_phase = QuestUxPhase.LiveScan;
                    }

                    if (pressedY)
                    {
                        // Begin ingredient weight review. IngredientReviewManager will
                        // call GenerateRecipeFromConfirmed when all weights are logged.
                        m_markerReviewRaySelector?.SetActive(false);
                        var confirmed = m_ingredientInventory?.GetConfirmedIngredientNames();
                        if (confirmed != null && confirmed.Count > 0)
                        {
                            m_backendClient?.PostConfirmedIngredients(new List<string>(confirmed));
                            m_ingredientReviewManager?.BeginReview();
                        }
                        m_phase = QuestUxPhase.RecipeGuidance;
                    }
                    break;
                }

                // ── RecipeGuidance ───────────────────────────────────────────
                case QuestUxPhase.RecipeGuidance:
                {
                    // Input for step confirmation is handled by RecipeGuidanceManager.
                    // B cancels guidance and the loop at the top of Update() will
                    // restore LiveScan on the next frame.
                    if (pressedB && RecipeGuidanceManager.IsGuiding)
                        m_guidanceManager?.StopGuidance();
                    break;
                }
            }
        }

        private IEnumerator UpdateSpatialAnchor()
        {
            while (true)
            {
                yield return null;
                if (m_spatialAnchor == null)
                {
                    yield return CreateSpatialAnchorAndSave();
                    if (m_spatialAnchor == null)
                    {
                        continue;
                    }
                }

                if (!m_spatialAnchor.IsTracked)
                {
                    yield return RestoreSpatialAnchorTracking();
                }
            }

            IEnumerator CreateSpatialAnchorAndSave()
            {
                m_spatialAnchor = m_uiInference.ContentParent.gameObject.AddComponent<OVRSpatialAnchor>();

                // Wait for localization because SaveAnchorAsync() requires the anchor to be localized first.
                while (true)
                {
                    if (m_spatialAnchor == null)
                    {
                        // Spatial Anchor destroys itself when creation fails.
                        yield break;
                    }
                    if (m_spatialAnchor.Localized)
                    {
                        break;
                    }
                    yield return null;
                }

                // Save the anchor.
                var awaiter = m_spatialAnchor.SaveAnchorAsync().GetAwaiter();
                while (!awaiter.IsCompleted)
                {
                    yield return null;
                }
                var saveAnchorResult = awaiter.GetResult();
                if (!saveAnchorResult.Success)
                {
                    LogSpatialAnchor($"SaveAnchorAsync() failed {saveAnchorResult}", LogType.Error);
                    EraseSpatialAnchor();
                    yield break;
                }
                LogSpatialAnchor("created");
            }

            IEnumerator RestoreSpatialAnchorTracking()
            {
                // Try to restore spatial anchor tracking. If restoration fails, erase it.
                LogSpatialAnchor("tracking was lost, restoring...");
                const int numRetries = 20;
                for (int i = 0; i < numRetries; i++)
                {
                    yield return new WaitForSeconds(1f);
                    if (!m_isHeadsetTracking)
                    {
                        LogSpatialAnchor($"{nameof(m_isHeadsetTracking)} is false, retrying ({i})");
                        continue;
                    }

                    var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>(1);
                    var awaiter = OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]
                    {
                        m_spatialAnchor.Uuid
                    }, unboundAnchors).GetAwaiter();
                    while (!awaiter.IsCompleted)
                    {
                        yield return null;
                    }
                    var loadResult = awaiter.GetResult();
                    if (!loadResult.Success)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() failed {loadResult.Status}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    if (unboundAnchors.Count != 0)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() unexpected count:{unboundAnchors.Count}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    yield return null;
                    if (!m_spatialAnchor.IsTracked)
                    {
                        LogSpatialAnchor($"tracking is not restored, retrying ({i})");
                        continue;
                    }

                    LogSpatialAnchor("tracking was restored successfully");
                    yield break;
                }

                LogSpatialAnchor($"tracking restoration failed after {numRetries} retries", LogType.Warning);
                EraseSpatialAnchor();
            }
        }

        private void EraseSpatialAnchor()
        {
            if (m_spatialAnchor != null)
            {
                LogSpatialAnchor("EraseSpatialAnchor");
                m_spatialAnchor.EraseAnchorAsync();
                DestroyImmediate(m_spatialAnchor);
                m_spatialAnchor = null;

                CleanMarkers();
                m_uiInference.ClearAnnotations();
            }
        }

        private void CleanMarkers()
        {
            foreach (var e in m_spawnedEntities)
            {
                Destroy(e.gameObject);
            }
            m_spawnedEntities.Clear();
            OnObjectsIdentified?.Invoke(-1);

            // Clear Quest-side ingredient inventory when markers are cleared.
            m_ingredientInventory?.ClearAll();
        }

        /// <summary>
        /// Removes a single marker from the spawned list and destroys its GameObject.
        /// Used by <see cref="MarkerReviewRaySelector"/> to delete a hovered marker.
        /// </summary>
        public bool RemoveMarker(DetectionSpawnMarkerAnim marker)
        {
            if (marker == null) return false;
            bool removed = m_spawnedEntities.Remove(marker);
            if (removed)
                Destroy(marker.gameObject);
            return removed;
        }

        private static void LogSpatialAnchor(string message, LogType logType = LogType.Log)
        {
            Debug.unityLogger.Log(logType, $"{nameof(OVRSpatialAnchor)}: {message}");
        }

        /// <summary>
        /// Spwan 3d markers for the detected objects
        /// </summary>
        private void SpawnCurrentDetectedObjects()
        {
            var newCount = 0;
            foreach (SentisInferenceUiManager.BoundingBoxData box in m_uiInference.m_boxDrawn)
            {
                if (!HasExistingMarkerInBoundingBox(box))
                {
                    var marker = Instantiate(m_spawnMarker, box.BoxRectTransform.position, box.BoxRectTransform.rotation, m_uiInference.ContentParent);
                    marker.GetComponent<DetectionSpawnMarkerAnim>().SetYoloClassName(box.ClassName);

                    m_spawnedEntities.Add(marker);
                    newCount++;
                }
            }
            OnObjectsIdentified?.Invoke(newCount);

            bool HasExistingMarkerInBoundingBox(SentisInferenceUiManager.BoundingBoxData box)
            {
                foreach (var marker in m_spawnedEntities)
                {
                    if (marker.GetYoloClassName() == box.ClassName)
                    {
                        var markerWorldPos = marker.transform.position;
                        Vector2 localPos = box.BoxRectTransform.InverseTransformPoint(markerWorldPos);
                        var sizeDelta = box.BoxRectTransform.sizeDelta;
                        var currentBox = new Rect(
                            -sizeDelta.x * 0.5f,
                            -sizeDelta.y * 0.5f,
                            sizeDelta.x,
                            sizeDelta.y
                        );

                        if (currentBox.Contains(localPos))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
