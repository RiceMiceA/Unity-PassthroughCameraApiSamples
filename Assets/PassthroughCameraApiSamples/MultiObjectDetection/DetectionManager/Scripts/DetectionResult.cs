// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Structured output of one YOLO detection after NMS.
    /// Replaces the (classId, boundingBox) tuple so that score and class name
    /// are preserved and can be used by IngredientInventoryManager.
    /// </summary>
    [System.Serializable]
    public class DetectionResult
    {
        public int ClassId;
        public string ClassName;
        public float Score;
        public Vector4 BoundingBox;

        public DetectionResult(int classId, string className, float score, Vector4 boundingBox)
        {
            ClassId = classId;
            ClassName = className;
            Score = score;
            BoundingBox = boundingBox;
        }
    }
}
