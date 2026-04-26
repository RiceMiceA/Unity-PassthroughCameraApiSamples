// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionSpawnMarkerAnim : MonoBehaviour
    {
        [SerializeField] private Vector3 m_anglesSpeed = new(20.0f, 40.0f, 60.0f);
        [SerializeField] private Transform m_model;
        [SerializeField] private TextMesh m_textModel;
        [SerializeField] private Transform m_textEntity;

        private Vector3 m_angles;
        private OVRCameraRig m_camera;
        private Vector3 m_modelOriginalScale;

        private void Awake()
        {
            m_camera = FindFirstObjectByType<OVRCameraRig>();
            m_modelOriginalScale = m_model != null ? m_model.localScale : Vector3.one;

            // Ensure the selection collider is big enough to be comfortable to point at.
            if (TryGetComponent<SphereCollider>(out var col))
                col.radius = 0.16f;
        }

        private void LateUpdate()
        {
            m_angles.x = AddAngle(m_angles.x, m_anglesSpeed.x * Time.deltaTime);
            m_angles.y = AddAngle(m_angles.y, m_anglesSpeed.y * Time.deltaTime);
            m_angles.z = AddAngle(m_angles.z, m_anglesSpeed.z * Time.deltaTime);

            m_model.rotation = Quaternion.Euler(m_angles);
            m_textEntity.gameObject.transform.LookAt(m_camera.centerEyeAnchor);
        }

        private static float AddAngle(float value, float toAdd)
        {
            value += toAdd;
            if (value > 360.0f)
            {
                value -= 360.0f;
            }

            if (value < 0.0f)
            {
                value = 360.0f - value;
            }

            return value;
        }

        public void SetYoloClassName(string name)
        {
            m_textModel.text = name;
        }

        public string GetYoloClassName()
        {
            return m_textModel != null ? m_textModel.text : string.Empty;
        }

        /// <summary>
        /// Visually highlight this marker when hovered by the review-mode ray selector.
        /// Only scales the inner <c>m_model</c> visual — never the root — to avoid
        /// world-space scale issues with OVRSpatialAnchor and the LookAt text billboard.
        /// </summary>
        public void SetHovered(bool hovered)
        {
            if (m_model == null) return;
            m_model.localScale = hovered ? m_modelOriginalScale * 1.15f : m_modelOriginalScale;
        }
    }
}
