// AI Nutritious Culinary Assistant — Serializable payloads for /ingredient_review responses.

using System;
using System.Collections.Generic;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [Serializable]
    public class IngredientReviewPayload
    {
        public string demo_state;
        public int review_index;
        public int total;
        public IngredientReviewItem current;
        public List<IngredientReviewItem> ingredients = new();
        public List<IngredientSummaryItem> ingredient_summary = new();
        public float live_weight_g;
        public bool all_complete;
    }

    [Serializable]
    public class IngredientReviewItem
    {
        public string id;
        public string label;
        public string display_name;
        public int instance_index;
        public int count_for_label;
        // NOTE: Unity JsonUtility does not support nullable float.
        // Use is_measured (set by backend) to distinguish null from 0g.
        public float weight_g;
        public bool is_measured;
    }

    [Serializable]
    public class IngredientSummaryItem
    {
        public string label;
        public int count;
        public int measured_count;
        public float total_weight_g;
        public float average_weight_g;
    }
}
