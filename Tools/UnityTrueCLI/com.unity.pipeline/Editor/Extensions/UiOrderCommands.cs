using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    static class UiOrderCommands
    {
        [CliCommand("configure_ui_order", "Set sibling order and/or an explicit Canvas sorting layer/order with Undo. No inferred global draw ranking, no save. Sorting fields require a Canvas on the target itself.", Tags = new[] { "ui/layout" })]
        public static object Set([CliArg("target", "UI GameObject/component.", Required = true)] ObjectRef target,
            [CliArg("sibling_index", "New index among the current siblings.")] int? siblingIndex = null,
            [CliArg("sorting_order", "Optional Canvas sorting order (-32768..32767).")] int? sortingOrder = null,
            [CliArg("sorting_layer", "Existing Sorting Layer name.")] string sortingLayer = null,
            [CliArg("override_sorting", "Optional nested Canvas overrideSorting.")] bool? overrideSorting = null)
        {
            var go = ExtensionObjects.Resolve(target); var canvas = go.GetComponent<Canvas>();
            if (!(go.transform is RectTransform)) throw new ArgumentException("Target needs RectTransform.");
            int siblingCount = go.transform.parent == null ? go.scene.rootCount : go.transform.parent.childCount;
            if (siblingIndex.HasValue && (siblingIndex.Value < 0 || siblingIndex.Value >= siblingCount)) throw new ArgumentException("sibling_index is outside the current sibling range.");
            if ((sortingOrder.HasValue || sortingLayer != null || overrideSorting.HasValue) && canvas == null) throw new ArgumentException("Sorting options require a Canvas on this target.");
            if (sortingOrder.HasValue && (sortingOrder.Value < short.MinValue || sortingOrder.Value > short.MaxValue)) throw new ArgumentException("sorting_order exceeds Canvas's range.");
            if (sortingLayer != null && !SortingLayer.layers.Any(layer => layer.name == sortingLayer)) throw new ArgumentException("Unknown sorting layer.");
            using (new AuthoringUndoScope("Configure UI Order"))
            {
                if (siblingIndex.HasValue) { Undo.RegisterFullObjectHierarchyUndo(go.transform.parent == null ? go : go.transform.parent.gameObject,"Configure UI Order"); go.transform.SetSiblingIndex(siblingIndex.Value); ExtensionObjects.Changed(go.transform); }
                if (canvas != null)
                {
                    Undo.RecordObject(canvas,"Configure UI Order");
                    if (sortingOrder.HasValue) canvas.sortingOrder=sortingOrder.Value;
                    if (sortingLayer != null) canvas.sortingLayerID=SortingLayer.NameToID(sortingLayer);
                    if (overrideSorting.HasValue) canvas.overrideSorting=overrideSorting.Value;
                    ExtensionObjects.Changed(canvas);
                }
            }
            return new { target=ObjectResolver.Describe(go), siblingIndex=go.transform.GetSiblingIndex(),
                canvas=canvas==null?null:new { canvas.sortingOrder,canvas.sortingLayerName,canvas.overrideSorting } };
        }
    }
}
