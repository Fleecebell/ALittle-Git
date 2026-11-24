using System;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Pipeline.UGUI.Editor
{
    static class UiAuthoringCommands
    {
        [CliCommand("set_ui_control", "Set common uGUI control state with Undo. Callback notification is off by default; Button clicks require the input path. Does not save.", Tags = new[] { "ui/components" })]
        public static object Control([CliArg("target", "Control GameObject/component.", Required = true)] ObjectRef target,
            [CliArg("interactable", "Optional Selectable.interactable.")] bool? interactable = null,
            [CliArg("toggle", "Optional Toggle value.")] bool? toggle = null,
            [CliArg("slider", "Optional Slider value within its current bounds.")] float? slider = null,
            [CliArg("text", "Optional legacy InputField/Text value; empty clears.")] string text = null,
            [CliArg("scroll", "Optional ScrollRect normalized position [x,y].")] float[] scroll = null,
            [CliArg("notify", "Invoke normal value-change callbacks for Toggle/Slider/InputField.")] bool notify = false)
        {
            var go = ExtensionObjects.Resolve(target);
            var selectable = go.GetComponent<Selectable>(); var t = go.GetComponent<Toggle>();
            var s = go.GetComponent<Slider>(); var input = go.GetComponent<InputField>(); var label = go.GetComponent<Text>(); var sc = go.GetComponent<ScrollRect>();
            if (interactable.HasValue && selectable == null) throw new ArgumentException("No Selectable.");
            if (toggle.HasValue && t == null) throw new ArgumentException("No Toggle.");
            if (slider.HasValue && (s == null || ExtensionObjects.Finite(slider.Value, "slider") < s.minValue || slider.Value > s.maxValue)) throw new ArgumentException("Slider missing or value outside its bounds.");
            if (text != null && input == null && label == null) throw new ArgumentException("No legacy InputField/Text; use set_tmp_input or set_tmp_text for TMP.");
            if (scroll != null)
            {
                if (sc == null || scroll.Length != 2) throw new ArgumentException("scroll requires ScrollRect and [x,y].");
                foreach (var value in scroll) if (ExtensionObjects.Finite(value, "scroll") < 0 || value > 1) throw new ArgumentException("scroll must be in 0..1.");
            }
            using (new AuthoringUndoScope("Set UI Control"))
            {
                var components = new Component[] { selectable, t, s, input, label, sc };
                foreach (var component in components) if (component != null) Undo.RecordObject(component, "Set UI Control");
                if (interactable.HasValue) selectable.interactable = interactable.Value;
                if (toggle.HasValue) { if (notify) t.isOn = toggle.Value; else t.SetIsOnWithoutNotify(toggle.Value); }
                if (slider.HasValue) { if (notify) s.value = slider.Value; else s.SetValueWithoutNotify(slider.Value); }
                if (text != null) { if (input != null) { if (notify) input.text = text; else input.SetTextWithoutNotify(text); } else label.text = text; }
                if (scroll != null) { if (sc.content != null) Undo.RecordObject(sc.content, "Set UI Scroll"); sc.normalizedPosition = new Vector2(scroll[0], scroll[1]); if(sc.content != null) ExtensionObjects.Changed(sc.content); }
                foreach (var component in components) if (component != null) ExtensionObjects.Changed(component);
            }
            return UiComponentCommands.Inspect(target, false);
        }

        [CliCommand("configure_ui_scaler", "Configure an existing CanvasScaler using typed API with Undo. Does not resize Game View or save. Query geometry after Unity's next layout pass.", Tags = new[] { "ui/layout" })]
        public static object Scaler([CliArg("target", "CanvasScaler object.", Required = true)] ObjectRef target,
            [CliArg("mode", "ConstantPixelSize, ScaleWithScreenSize or ConstantPhysicalSize.")] string mode = null,
            [CliArg("reference_resolution", "Positive [width,height].")] float[] resolution = null,
            [CliArg("match", "Match width/height in 0..1.")] float? match = null,
            [CliArg("scale_factor", "Positive constant scale factor.")] float? factor = null)
        {
            var scaler = ExtensionObjects.Require<CanvasScaler>(target);
            CanvasScaler.ScaleMode parsed = scaler.uiScaleMode;
            if (mode != null && (!Enum.TryParse(mode, true, out parsed) || !Enum.IsDefined(typeof(CanvasScaler.ScaleMode), parsed))) throw new ArgumentException("Invalid scaler mode.");
            if (resolution != null) { if (resolution.Length != 2) throw new ArgumentException("reference_resolution requires [width,height]."); foreach (var value in resolution) if (ExtensionObjects.Finite(value, "reference_resolution") <= 0) throw new ArgumentException("Resolution must be positive."); }
            if (match.HasValue && (ExtensionObjects.Finite(match.Value,"match") < 0 || match.Value > 1)) throw new ArgumentException("match must be in 0..1.");
            if (factor.HasValue && ExtensionObjects.Finite(factor.Value,"scale_factor") <= 0) throw new ArgumentException("scale_factor must be positive.");
            using (new AuthoringUndoScope("Configure UI Scaler"))
            {
                Undo.RecordObject(scaler,"Configure UI Scaler");
                scaler.uiScaleMode=parsed;
                if (resolution != null) scaler.referenceResolution=new Vector2(resolution[0],resolution[1]);
                if (match.HasValue) scaler.matchWidthOrHeight=match.Value;
                if (factor.HasValue) scaler.scaleFactor=factor.Value;
                ExtensionObjects.Changed(scaler);
            }
            return UiComponentCommands.Inspect(target,false);
        }
    }
}
