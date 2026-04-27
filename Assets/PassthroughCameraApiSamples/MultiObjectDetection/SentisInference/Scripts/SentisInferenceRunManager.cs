// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Meta.XR;
using Meta.XR.Samples;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceRunManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;
        [SerializeField] private DetectionManager m_detectionManager;

        [Header("Sentis Model config")]
        [SerializeField] private BackendType m_backend = BackendType.CPU;
        [SerializeField] private ModelAsset m_sentisModel;
        [SerializeField] private TextAsset m_labelsAsset;
        [SerializeField, Range(0, 1)] private float m_iouThreshold = 0.5f;
        [SerializeField, Range(0, 1)] private float m_scoreThreshold = 0.25f;

        [Header("UI display references")]
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("Ingredient tracking")]
        [SerializeField] private IngredientInventoryManager m_ingredientInventory;

        [Header("Vision R&V HUD (optional)")]
        [SerializeField] private VisionRvHudController m_rvHud;

        [Header("Backend reporting")]
        [Tooltip("BackendClient used to POST /vision_frame for dashboard R&V logging.")]
        [SerializeField] private BackendClient m_backendClient;
        [Tooltip("Seconds between /vision_frame POSTs.  1.0 = 1 Hz, reduces bandwidth.")]
        [SerializeField, Range(0.1f, 5f)] private float m_visionFramePostInterval = 1.0f;

        [Header("[Editor Only] Convert to Sentis")]
        public ModelAsset OnnxModel;
        [Space(40)]

        // ── Detection cap (R&V requirement: <= 20 regions per frame) ───────────
        private const int MAX_DETECTIONS = 20;

        private Worker m_engine;
        private Vector2Int m_inputSize;
        private string[] m_labels;
        private readonly List<DetectionResult> m_detections = new List<DetectionResult>();

        // ── Telemetry ──────────────────────────────────────────────────────────
        private int   m_frameId;
        private float m_lastInferenceMs;
        private float m_fps;
        private float m_lastFrameStartTime = -1f;
        private float m_visionFramePostTimer = 0f;  // counts up; posts when >= interval

        private bool m_inferenceEnabled = true;
        public bool InferenceEnabled => m_inferenceEnabled;

        // ── Public telemetry accessors for external scripts ────────────────────
        public int   FrameId        => m_frameId;
        public float LastInferenceMs => m_lastInferenceMs;
        public float Fps            => m_fps;
        public int   DetectionCount => m_detections.Count;
        public int   MaxDetections  => MAX_DETECTIONS;
        public float ScoreThreshold => m_scoreThreshold;
        public float IouThreshold   => m_iouThreshold;

        public void SetInferenceEnabled(bool enabled)
        {
            m_inferenceEnabled = enabled;
        }

        private void Awake()
        {
            var model = ModelLoader.Load(m_sentisModel);
            var inputShape = model.inputs[0].shape;
            m_inputSize = new Vector2Int(inputShape.Get(2), inputShape.Get(3));
            m_engine = new Worker(model, m_backend);
        }

        private IEnumerator Start()
        {
            m_uiInference.SetLabels(m_labelsAsset);
            m_labels = m_labelsAsset.text.Split('\n');

            while (true)
            {
                while (m_uiMenuManager.IsPaused || !m_inferenceEnabled)
                {
                    yield return null;
                }
                yield return RunInference();
            }
        }

        private void OnDestroy()
        {
            m_engine.PeekOutput(0)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(1)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(2)?.CompleteAllPendingOperations();
            m_engine.Dispose();
        }

        internal static void PreloadModel(ModelAsset modelAsset)
        {
            // Load model
            var model = ModelLoader.Load(modelAsset);
            var inputShape = model.inputs[0].shape;

            // Create engine to run model
            using var worker = new Worker(model, BackendType.CPU);

            // Run inference with an empty image to load the model in the memory. The first inference blocks the main thread for a long time, so we're doing it on the app launch
            Texture tempTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var textureTransform = new TextureTransform();
            using var input = new Tensor<float>(new TensorShape(1, 3, inputShape.Get(2), inputShape.Get(3)));
            TextureConverter.ToTensor(tempTexture, input, textureTransform);
            worker.Schedule(input);

            // Complete the inference immediately and destroy the temporary texture
            worker.PeekOutput(0).CompleteAllPendingOperations();
            worker.PeekOutput(1).CompleteAllPendingOperations();
            worker.PeekOutput(2).CompleteAllPendingOperations();
            Destroy(tempTexture);
        }

        private IEnumerator RunInference()
        {
            if (!m_cameraAccess.IsPlaying)
            {
                yield break;
            }

            float inferenceStartTime = Time.realtimeSinceStartup;

            [DllImport("OVRPlugin", CallingConvention = CallingConvention.Cdecl)]
            static extern OVRPlugin.Result ovrp_GetNodePoseStateAtTime(double time, OVRPlugin.Node nodeId, out OVRPlugin.PoseStatef nodePoseState);
            if (!ovrp_GetNodePoseStateAtTime(OVRPlugin.GetTimeInSeconds(), OVRPlugin.Node.Head, out _).IsSuccess())
            {
                Debug.Log("ovrp_GetNodePoseStateAtTime failed, which means 'm_cameraAccess.GetCameraPose()' is not reliable, skipping.");
                yield break;
            }

            var cachedCameraPose = m_cameraAccess.GetCameraPose();

            // Update Capture data
            Texture targetTexture = m_cameraAccess.GetTexture();

            // Convert the texture to a Tensor and schedule the inference
            var textureTransform = new TextureTransform();
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.x, m_inputSize.y));
            TextureConverter.ToTensor(targetTexture, input, textureTransform);

            // Schedule all model layers
            m_engine.Schedule(input);

            // Get the results. ReadbackAndCloneAsync waits for all layers to complete before returning the result
            var boxesAwaiter = (m_engine.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            while (!boxesAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var boxes = boxesAwaiter.GetResult();
            if (boxes.shape[0] == 0)
            {
                yield break;
            }

            var classIDsAwaiter = (m_engine.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync().GetAwaiter();
            while (!classIDsAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var classIDs = classIDsAwaiter.GetResult();
            if (classIDs.shape[0] == 0)
            {
                Debug.LogError("classIDs.shape[0] == 0");
                yield break;
            }

            var scoresAwaiter = (m_engine.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            while (!scoresAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var scores = scoresAwaiter.GetResult();
            if (scores.shape[0] == 0)
            {
                Debug.LogError("scores.shape[0] == 0");
                yield break;
            }

            NonMaxSuppression(m_detections, boxes, classIDs, scores, m_iouThreshold, m_scoreThreshold);

            // ── Telemetry ────────────────────────────────────────────────────
            m_frameId++;
            m_lastInferenceMs = (Time.realtimeSinceStartup - inferenceStartTime) * 1000f;
            if (m_lastFrameStartTime > 0f)
                m_fps = 1f / Mathf.Max(0.001f, Time.realtimeSinceStartup - m_lastFrameStartTime);
            m_lastFrameStartTime = Time.realtimeSinceStartup;

            // Push stats to the optional R&V HUD.
            if (m_rvHud != null)
            {
                m_rvHud.SetInferenceStats(m_frameId, m_fps, m_lastInferenceMs,
                                          m_detections.Count, MAX_DETECTIONS,
                                          m_scoreThreshold, m_iouThreshold);
                m_rvHud.SetDetections(m_detections);
            }

            // Throttled POST to /vision_frame so the web dashboard stays live.
            m_visionFramePostTimer += Time.deltaTime;
            if (m_backendClient != null && m_visionFramePostTimer >= m_visionFramePostInterval)
            {
                m_visionFramePostTimer = 0f;
                m_backendClient.PostVisionFrame(m_detections, m_frameId, m_fps, m_lastInferenceMs);
                m_backendClient.PostRvEvent("vision_frame");
            }

            // Update ingredient inventory with latest detections.
            m_ingredientInventory?.UpdateCandidates(m_detections);

            // Checking if spatial anchor is tracked ensures bounding boxes are placed at correct world space positIons.
            if (!m_cameraAccess.IsPlaying || m_detectionManager.m_spatialAnchor == null || !m_detectionManager.m_spatialAnchor.IsTracked)
            {
                yield break;
            }

            // Update UI.
            m_uiInference.DrawUIBoxes(m_detections, m_inputSize, cachedCameraPose);
        }

        private void NonMaxSuppression(List<DetectionResult> outDetections, Tensor<float> boxes, Tensor<int> classIDs, Tensor<float> scores, float iouThreshold, float scoreThreshold)
        {
            outDetections.Clear();

            // Filter by score threshold first
            List<int> filteredIndices = new List<int>();
            NativeArray<float>.ReadOnly scoresArray = scores.AsReadOnlyNativeArray();
            for (int i = 0; i < scoresArray.Length; i++)
            {
                if (scoresArray[i] >= scoreThreshold)
                {
                    filteredIndices.Add(i);
                }
            }

            if (filteredIndices.Count == 0)
            {
                return;
            }

            // Sort filtered indices by scores in descending order
            filteredIndices.Sort((a, b) => scoresArray[b].CompareTo(scoresArray[a]));

            // Apply NMS algorithm
            bool[] suppressed = new bool[filteredIndices.Count];
            for (int i = 0; i < filteredIndices.Count; i++)
            {
                if (suppressed[i])
                    continue;

                int idx = filteredIndices[i];

                // Add this detection to results (preserving score and class name).
                int classId = classIDs[idx];
                string className = (m_labels != null && classId < m_labels.Length)
                    ? m_labels[classId].Trim()
                    : classId.ToString();
                outDetections.Add(new DetectionResult(classId, className, scoresArray[idx], GetBox(idx)));

                // Hard cap — R&V requirement: never exceed MAX_DETECTIONS.
                if (outDetections.Count >= MAX_DETECTIONS)
                    break;

                // Suppress overlapping boxes regardless of class
                for (int j = i + 1; j < filteredIndices.Count; j++)
                {
                    if (suppressed[j])
                        continue;

                    int jdx = filteredIndices[j];

                    float iou = CalculateIoU(GetBox(idx), GetBox(jdx));
                    if (iou > iouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            Vector4 GetBox(int i) => new Vector4(boxes[i, 0], boxes[i, 1], boxes[i, 2], boxes[i, 3]);
        }

        internal static float CalculateIoU(Vector4 boxA, Vector4 boxB)
        {
            // Boxes are in format (topLeftX, topLeftY, bottomRightX, bottomRightY)
            // Calculate intersection coordinates
            float x1 = Mathf.Max(boxA.x, boxB.x);
            float y1 = Mathf.Max(boxA.y, boxB.y);
            float x2 = Mathf.Min(boxA.z, boxB.z);
            float y2 = Mathf.Min(boxA.w, boxB.w);

            // Calculate intersection area
            float intersectionWidth = Mathf.Max(0, x2 - x1);
            float intersectionHeight = Mathf.Max(0, y2 - y1);
            float intersectionArea = intersectionWidth * intersectionHeight;

            // Calculate individual box areas
            float boxAArea = (boxA.z - boxA.x) * (boxA.w - boxA.y);
            float boxBArea = (boxB.z - boxB.x) * (boxB.w - boxB.y);

            // Calculate union area
            float unionArea = boxAArea + boxBArea - intersectionArea;

            // Return IoU (Intersection over Union)
            if (unionArea == 0)
                return 0;

            return intersectionArea / unionArea;
        }
    }
}
