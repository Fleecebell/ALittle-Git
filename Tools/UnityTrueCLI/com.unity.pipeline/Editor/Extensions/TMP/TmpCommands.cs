using System;
using TMPro;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.TMP.Editor
{
    static class TmpCommands
    {
        [CliCommand("inspect_tmp_text", "Read TMP text, sizing, measured overflow, font and input field state without forcing a mesh or layout rebuild.", Tags = new[] { "ui/text" })]
        public static object Inspect([CliArg("target", "TMP_Text or TMP_InputField object.", Required = true)] ObjectRef target)
        {
            var go = ExtensionObjects.Resolve(target);
            var input = go.GetComponent<TMP_InputField>();
            var text = go.GetComponent<TMP_Text>() ?? input?.textComponent;
            if (text == null) throw new ArgumentException("No TMP_Text or TMP_InputField.textComponent.");
            return new { target = ObjectResolver.Describe(text), text.text, text.fontSize, text.enableAutoSizing, text.fontSizeMin, text.fontSizeMax,
                text.preferredWidth, text.preferredHeight, text.renderedWidth, text.renderedHeight, text.isTextOverflowing, text.firstOverflowCharacterIndex,
                text.isTextTruncated, overflowMode = text.overflowMode.ToString(), alignment = text.alignment.ToString(),
                font = ObjectResolver.Describe(text.font), material = ObjectResolver.Describe(text.fontSharedMaterial),
                geometryMayBePending = text.havePropertiesChanged,
                input = input == null ? null : new { input.text, input.readOnly, input.interactable, input.characterLimit, input.isFocused } };
        }

        [CliCommand("set_tmp_text", "Set TMP display text, size and autosizing with Undo; no save or forced layout. Use a subsequent inspect after layout updates.", Tags = new[] { "ui/text" })]
        public static object Set([CliArg("target", "TMP_Text object.", Required = true)] ObjectRef target,
            [CliArg("text", "Omit to preserve text; empty clears it.")] string text = null,
            [CliArg("font_size", "Positive font size; omitted preserves it.")] float? fontSize = null,
            [CliArg("auto_size", "Enable/disable TMP autosizing.")] bool? autoSize = null)
        {
            if (fontSize.HasValue && (ExtensionObjects.Finite(fontSize.Value, "font_size") <= 0)) throw new ArgumentException("font_size must be positive.");
            var component = ExtensionObjects.Require<TMP_Text>(target);
            using (new AuthoringUndoScope("Set TMP Text"))
            {
                Undo.RecordObject(component, "Set TMP Text");
                if (text != null) component.text = text;
                if (fontSize.HasValue) component.fontSize = fontSize.Value;
                if (autoSize.HasValue) component.enableAutoSizing = autoSize.Value;
                ExtensionObjects.Changed(component);
            }
            return Inspect(target);
        }

        [CliCommand("set_tmp_input", "Set TMP_InputField text/read-only state with Undo. Value-change notification defaults off; does not emulate typing or submit.", Tags = new[] { "ui/text" })]
        public static object Input([CliArg("target", "TMP_InputField object.", Required = true)] ObjectRef target,
            [CliArg("text", "New text; omitted preserves, empty clears.")] string text = null,
            [CliArg("read_only", "Optional read-only state.")] bool? readOnly = null,
            [CliArg("notify", "Invoke onValueChanged for the text change.")] bool notify = false)
        {
            var input=ExtensionObjects.Require<TMP_InputField>(target);
            using(new AuthoringUndoScope("Set TMP Input"))
            {
                Undo.RecordObject(input,"Set TMP Input");
                if(text!=null) { if(notify) input.text=text; else input.SetTextWithoutNotify(text); }
                if(readOnly.HasValue) input.readOnly=readOnly.Value;
                ExtensionObjects.Changed(input);
            }
            return Inspect(target);
        }
    }
}
