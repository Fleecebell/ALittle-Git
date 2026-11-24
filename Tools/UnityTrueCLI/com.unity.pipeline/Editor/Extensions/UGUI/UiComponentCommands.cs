using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unity.Pipeline.UGUI.Editor
{
    static class UiComponentCommands
    {
        [CliCommand("inspect_ui_components", "Read uGUI scaler, layout, clipping, graphics and common controls. Can include the ancestor chain. No layout rebuild or state change.", Tags = new[] { "ui/components" })]
        public static object Inspect([CliArg("target", "Loaded UI object.", Required = true)] ObjectRef target,
            [CliArg("ancestors", "Include parents, at most 256 nodes.")] bool ancestors = true)
        {
            var go = ExtensionObjects.Resolve(target);
            var result = new List<object>();
            for (var current = go.transform; current != null; current = ancestors ? current.parent : null)
            {
                if (result.Count == 256) throw new ArgumentException("Ancestor chain exceeds 256 nodes.");
                result.Add(Describe(current.gameObject));
            }
            return new { frame = Time.frameCount, targetToRoot = result };
        }

        private static object Describe(GameObject go)
        {
            var scaler = go.GetComponent<CanvasScaler>();
            var group = go.GetComponent<LayoutGroup>();
            var horizontal = group as HorizontalOrVerticalLayoutGroup;
            var grid = group as GridLayoutGroup;
            var fitter = go.GetComponent<ContentSizeFitter>();
            var aspect = go.GetComponent<AspectRatioFitter>();
            var graphic = go.GetComponent<Graphic>();
            var selectable = go.GetComponent<Selectable>();
            var scroll = go.GetComponent<ScrollRect>();
            var raycaster = go.GetComponent<GraphicRaycaster>();
            var image = go.GetComponent<Image>();
            var element = go.GetComponent<LayoutElement>();
            return new
            {
                handle = ObjectResolver.Describe(go),
                scaler = scaler == null ? null : new { scaler.enabled, mode = scaler.uiScaleMode.ToString(), scaler.scaleFactor,
                    referenceResolution = new[] { scaler.referenceResolution.x, scaler.referenceResolution.y },
                    screenMatchMode = scaler.screenMatchMode.ToString(), scaler.matchWidthOrHeight, scaler.referencePixelsPerUnit, scaler.dynamicPixelsPerUnit },
                layout = group == null ? null : new { type = group.GetType().Name, group.enabled, alignment = group.childAlignment.ToString(),
                    padding = new[] { group.padding.left, group.padding.right, group.padding.top, group.padding.bottom } },
                linearLayout = horizontal == null ? null : new { horizontal.spacing, horizontal.childControlWidth, horizontal.childControlHeight,
                    horizontal.childForceExpandWidth, horizontal.childForceExpandHeight, horizontal.childScaleWidth, horizontal.childScaleHeight },
                gridLayout = grid == null ? null : new { cellSize = new[] { grid.cellSize.x, grid.cellSize.y }, spacing = new[] { grid.spacing.x, grid.spacing.y }, constraint = grid.constraint.ToString(), grid.constraintCount },
                contentSizeFitter = fitter == null ? null : new { fitter.enabled, horizontal = fitter.horizontalFit.ToString(), vertical = fitter.verticalFit.ToString() },
                layoutElement = element == null ? null : new { element.enabled, element.ignoreLayout, element.minWidth,element.minHeight,element.preferredWidth,element.preferredHeight,element.flexibleWidth,element.flexibleHeight,element.layoutPriority },
                aspectRatioFitter = aspect == null ? null : new { aspect.enabled, aspect.aspectRatio, mode = aspect.aspectMode.ToString() },
                graphic = graphic == null ? null : new { graphic.enabled, graphic.depth, graphic.raycastTarget,
                    color = new[] { graphic.color.r, graphic.color.g, graphic.color.b, graphic.color.a },
                    culled = graphic.canvasRenderer.cull, material = ObjectResolver.Describe(graphic.material) },
                image = image == null ? null : new { type = image.type.ToString(), sprite = ObjectResolver.Describe(image.sprite), image.preserveAspect, image.fillAmount, image.alphaHitTestMinimumThreshold },
                selectable = selectable == null ? null : new { type = selectable.GetType().Name, selectable.enabled, selectable.interactable, effectiveInteractable = selectable.IsInteractable(), navigation = selectable.navigation.mode.ToString() },
                toggle = go.GetComponent<Toggle>() == null ? (bool?)null : go.GetComponent<Toggle>().isOn,
                slider = go.GetComponent<Slider>() == null ? null : new { go.GetComponent<Slider>().value, go.GetComponent<Slider>().minValue, go.GetComponent<Slider>().maxValue },
                scroll = scroll == null ? null : new { scroll.enabled, scroll.horizontal, scroll.vertical, position = new[] { scroll.normalizedPosition.x, scroll.normalizedPosition.y }, content = ObjectResolver.Describe(scroll.content), viewport = ObjectResolver.Describe(scroll.viewport) },
                mask = go.GetComponent<Mask>() == null ? null : new { go.GetComponent<Mask>().enabled, go.GetComponent<Mask>().showMaskGraphic },
                rectMask = go.GetComponent<RectMask2D>() == null ? null : new { go.GetComponent<RectMask2D>().enabled },
                raycaster = raycaster == null ? null : new { raycaster.enabled, raycaster.ignoreReversedGraphics, blockingObjects = raycaster.blockingObjects.ToString() }
            };
        }

        [CliCommand("inspect_ui_point", "Run EventSystem.RaycastAll at a screen point and return its actual ordered hit stack. No click is sent. Draw visibility and event-handler consumption are separate facts.", Tags = new[] { "ui/hit_test" })]
        public static object Point([CliArg("x", "Screen x, bottom-left origin.", Required = true)] float x,
            [CliArg("y", "Screen y, bottom-left origin.", Required = true)] float y,
            [CliArg("event_system", "Optional EventSystem target; otherwise current.")] ObjectRef eventSystem = null,
            [CliArg("limit", "Maximum returned hits, 1..200.")] int limit = 50)
        {
            ExtensionObjects.Finite(x, "x"); ExtensionObjects.Finite(y, "y");
            if (limit < 1 || limit > 200) throw new ArgumentException("limit must be 1..200.");
            var system = eventSystem == null ? EventSystem.current : ExtensionObjects.Require<EventSystem>(eventSystem);
            if (system == null && eventSystem == null)
            {
                var available = ExtensionObjects.Find<EventSystem>().Where(item => item.isActiveAndEnabled).ToArray();
                if (available.Length == 1) system = available[0];
                else if (available.Length > 1) throw new InvalidOperationException("Multiple EventSystems and no current system; specify event_system.");
            }
            if (system == null) throw new InvalidOperationException("No EventSystem; no receiver inferred.");
            var hits = new List<RaycastResult>();
            system.RaycastAll(new PointerEventData(system) { position = new Vector2(x, y) }, hits);
            return new
            {
                frame = Time.frameCount, eventSystem = ObjectResolver.Describe(system), system.enabled,
                inputModule = ObjectResolver.Describe(system.currentInputModule), total = hits.Count,
                hits = hits.Take(limit).Select((hit, index) => new
                {
                    index, target = ObjectResolver.Describe(hit.gameObject), module = ObjectResolver.Describe(hit.module),
                    hit.sortingLayer, sortingLayerValue = SortingLayer.GetLayerValueFromID(hit.sortingLayer), hit.sortingOrder,
                    hit.depth, hit.distance, hit.displayIndex,
                    clickHandler = ObjectResolver.Describe(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject)),
                    pointerDownHandler = ObjectResolver.Describe(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject))
                }).ToArray(),
                firstHit = hits.Count == 0 ? null : ObjectResolver.Describe(hits[0].gameObject),
                note = "Hit order is raycaster order, not proof a user callback will consume a click. Transparent graphics may still block."
            };
        }

        [CliCommand("inspect_ui_text", "Inspect legacy uGUI Text and InputField. TMP has inspect_tmp_text with its own schema.", Tags = new[] { "ui/text" })]
        public static object TextInfo([CliArg("target", "Text/InputField object.", Required = true)] ObjectRef target)
        {
            var go = ExtensionObjects.Resolve(target);
            var text = go.GetComponent<Text>(); var input = go.GetComponent<InputField>();
            if (text == null && input == null) throw new ArgumentException("No legacy Text/InputField. Use inspect_tmp_text for TMP.");
            return new { target = ObjectResolver.Describe(go),
                text = text == null ? null : new { text.text, text.fontSize, font = ObjectResolver.Describe(text.font), text.resizeTextForBestFit,
                    text.resizeTextMinSize, text.resizeTextMaxSize, text.preferredWidth, text.preferredHeight,
                    horizontalOverflow = text.horizontalOverflow.ToString(), verticalOverflow = text.verticalOverflow.ToString() },
                input = input == null ? null : new { input.text, input.interactable, input.readOnly, input.characterLimit, input.isFocused } };
        }
    }
}
