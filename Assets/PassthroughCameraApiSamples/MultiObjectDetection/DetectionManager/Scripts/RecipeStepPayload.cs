// NuChef — Serializable data models for the /current_step backend payload.
// Keep these plain data classes; no MonoBehaviour logic here.
//
// Schema version 1 — matches backend.py _current_step() response.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    // ── Top-level response ─────────────────────────────────────────────────────

    [Serializable]
    public class RecipeStepPayload
    {
        public int schema_version;
        public string step_id;
        public int step_index;
        public int total_steps;
        public string demo_state;

        public string display_text;
        public string voice_text;
        public string action;             // focus | grab | move | season | mix | wait | complete

        public List<SelectorTarget> targets = new();
        public SelectorTarget destination; // null if not a move step

        public string completion_mode;    // user_confirm | dispense_done | timer_done | auto
        public float duration_s;          // >0 only for wait steps
        public DispenseInfo dispense;     // null if no dispense

        public RenderPlan render_plan;
        public StepStatus step_status;

        // Helper: true when the payload has actual step data (not a null/no-recipe response)
        public bool IsValid => !string.IsNullOrEmpty(step_id);
    }

    // ── Selector target (label-based for now) ──────────────────────────────────

    [Serializable]
    public class SelectorTarget
    {
        public string selector; // "label"
        public string value;    // e.g. "onion"

        public bool IsEmpty => string.IsNullOrEmpty(value);
    }

    // ── Dispense info ──────────────────────────────────────────────────────────

    [Serializable]
    public class DispenseInfo
    {
        public string spice;
        public float grams;
    }

    // ── Render plan ────────────────────────────────────────────────────────────

    [Serializable]
    public class RenderPlan
    {
        public string focus_preset;              // e.g. "soft_highlight"
        public List<string> assist_presets = new(); // e.g. ["arrow_dispenser_to_target"]
        public HudConfig hud;
    }

    [Serializable]
    public class HudConfig
    {
        public bool show_text;
        public bool show_spice;
        public bool show_target_grams;
        public bool show_live_progress;
        public bool show_timer;
    }

    // ── Live step status ───────────────────────────────────────────────────────

    [Serializable]
    public class StepStatus
    {
        public string dispense_status;    // null | idle | dispensing | done | error
        public float target_grams;
        public float current_grams;
        public float actual_grams;
        public float timer_remaining_s;
    }

    // ── Null-step wrapper (backend returns {"step": null} when no recipe) ──────

    [Serializable]
    internal class NullStepResponse
    {
        public string step_id;    // will be null/empty when step is null
        public string demo_state;
    }
}
