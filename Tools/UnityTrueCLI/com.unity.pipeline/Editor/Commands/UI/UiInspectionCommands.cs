using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.UI
{
    // This core inspection surface deliberately depends only on built-in UI types.
    // uGUI/TMP layout drivers and hit testing belong to their package adapters.
    static class UiInspectionCommands
    {
        [CliCommand("get_ui_snapshot",
            "Read RectTransform geometry and Canvas sorting facts in the current stage. Read-only; does not force layout. Bounds are geometric, not visibility or hit-test results. Coordinates use the screen bottom-left origin.",
            Tags = new[] { "ui/layout" })]
        public static UiSnapshot GetSnapshot(
            [CliArg("root", "Optional loaded GameObject/component handle to restrict the subtree.")] ObjectRef root = null,
            [CliArg("include_inactive", "Include inactive GameObjects (default false).")] bool includeInactive = false,
            [CliArg("offset", "Zero-based page offset.")] int offset = 0,
            [CliArg("limit", "Maximum returned elements, 1..500.")] int limit = 100)
        {
            if (offset < 0 || limit < 1 || limit > 500)
                throw new ArgumentException("offset must be nonnegative and limit must be 1..500.");

            var stage = StageUtility.GetCurrentStageHandle();
            Transform rootTransform = root == null ? null : ResolveTransform(root);
            if (rootTransform != null && !stage.Contains(rootTransform.gameObject))
                throw new ArgumentException("root is outside the current stage.");

            var candidates = rootTransform != null
                ? rootTransform.GetComponentsInChildren<RectTransform>(true)
                : Resources.FindObjectsOfTypeAll<RectTransform>();
            var ordered = candidates.Where(rect => IsSceneObject(rect) && stage.Contains(rect.gameObject)
                    && (includeInactive || rect.gameObject.activeInHierarchy))
                .OrderBy(rect => (int)rect.gameObject.scene.handle)
                .ThenBy(rect => SortPath(rect), StringComparer.Ordinal).ToArray();

            var page = ordered.Skip(offset).Take(limit).Select(Describe).ToList();
            return new UiSnapshot
            {
                frame = Time.frameCount,
                screenSize = new[] { Screen.width, Screen.height },
                total = ordered.Length,
                offset = offset,
                nextOffset = offset + page.Count < ordered.Length ? (int?)(offset + page.Count) : null,
                elements = page,
                limitations = new[]
                {
                    "Current-stage geometry only; no forced layout or screen resize.",
                    "No CanvasScaler/LayoutGroup/TMP adapter, visibility test, raycast or overlap classification in this core snapshot.",
                    "Canvas sorting fields and sibling paths are facts, not one global draw-order ranking."
                }
            };
        }

        [CliCommand("explain_ui_layout",
            "Read a RectTransform and its complete parent chain, including non-UI parents, the recorded driver, Canvas sorting and CanvasGroups. Does not force layout or change objects.",
            Tags = new[] { "ui/layout" })]
        public static UiLayoutExplanation ExplainLayout(
            [CliArg("target", "Loaded RectTransform GameObject/component handle.", Required = true)] ObjectRef target)
        {
            var transform = ResolveTransform(target);
            if (!(transform is RectTransform))
                throw new ArgumentException("target must have a RectTransform.");
            if (!StageUtility.GetCurrentStageHandle().Contains(transform.gameObject))
                throw new ArgumentException("target is outside the current stage.");

            var ancestors = new List<UiElementSnapshot>();
            for (var current = transform; current != null; current = current.parent)
            {
                if (ancestors.Count == 256)
                    throw new InvalidOperationException("Parent chain exceeds the 256-node output bound; no partial explanation returned.");
                ancestors.Add(Describe(current));
            }
            return new UiLayoutExplanation
            {
                frame = Time.frameCount,
                screenSize = new[] { Screen.width, Screen.height },
                targetToRoot = ancestors,
                limitations = new[]
                {
                    "Read-only current values; layout has not been forced to rebuild.",
                    "drivenBy identifies Unity's recorded RectTransform driver; specialized scaler/layout/text explanations need their package adapters.",
                    "CanvasGroup properties are returned per ancestor, not reduced to an assumed effective click/visibility state."
                }
            };
        }

        [CliCommand("find_ui_overlaps",
            "Find screen-space AABB intersection candidates in a bounded RectTransform subtree. Geometry only: not proof of pixel occlusion or blocked clicks. Ancestor/descendant pairs are excluded by default.",
            Tags = new[] { "ui/layout" })]
        public static UiOverlapSnapshot FindOverlaps(
            [CliArg("root", "Optional loaded subtree root. Narrow the root if more than 200 RectTransforms are present.")] ObjectRef root = null,
            [CliArg("limit", "Maximum returned pairs, 1..100.")] int limit = 30,
            [CliArg("include_ancestors", "Include ordinary parent/child containment pairs (default false).")] bool includeAncestors = false)
        {
            if (limit < 1 || limit > 100)
                throw new ArgumentException("limit must be 1..100.");
            var snapshot = GetSnapshot(root, false, 0, 200);
            if (snapshot.total > 200)
                throw new ArgumentException("More than 200 active RectTransforms; supply a narrower root. No partial overlap scan performed.");
            var projected = snapshot.elements.Where(element => element.rectTransform.projection.status == "projected").ToArray();
            var result = new UiOverlapSnapshot
            {
                frame = snapshot.frame, screenSize = snapshot.screenSize,
                examinedElements = snapshot.total, unprojectableElements = snapshot.total - projected.Length,
                candidates = new List<UiOverlapCandidate>(),
                limitations = new[]
                {
                    "AABB geometry candidates only, including rotated/transparent/clipped geometry; no pixel visibility or click-blocking inference.",
                    "Default scan excludes inactive GameObjects and ancestor/descendant containment pairs.",
                    "Different Canvas/camera render paths are not ranked; use the snapshot, actual screenshots and hit testing."
                }
            };
            for (int firstIndex = 0; firstIndex < projected.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < projected.Length; secondIndex++)
                {
                    var first = projected[firstIndex];
                    var second = projected[secondIndex];
                    if (!includeAncestors && (IsAncestor(first, second) || IsAncestor(second, first)))
                        continue;
                    result.examinedPairs++;
                    var a = first.rectTransform.projection.screenAabb;
                    var b = second.rectTransform.projection.screenAabb;
                    float left = Mathf.Max(a[0], b[0]), bottom = Mathf.Max(a[1], b[1]);
                    float right = Mathf.Min(a[0] + a[2], b[0] + b[2]), top = Mathf.Min(a[1] + a[3], b[1] + b[3]);
                    if (right <= left || top <= bottom)
                        continue;
                    result.totalCandidates++;
                    if (result.candidates.Count == limit)
                        continue;
                    var area = (right - left) * (top - bottom);
                    result.candidates.Add(new UiOverlapCandidate
                    {
                        first = first.handle, second = second.handle,
                        intersection = new[] { left, bottom, right - left, top - bottom },
                        firstAabbFraction = area / (a[2] * a[3]), secondAabbFraction = area / (b[2] * b[3])
                    });
                }
            }
            result.truncated = result.totalCandidates > result.candidates.Count;
            return result;
        }

        private static bool IsAncestor(UiElementSnapshot first, UiElementSnapshot second) =>
            first.sceneHandle == second.sceneHandle && second.siblingPath.StartsWith(first.siblingPath + "/", StringComparison.Ordinal);

        private static Transform ResolveTransform(ObjectRef handle)
        {
            if (!ObjectResolver.TryResolve(handle, out var resolved, out var error))
                throw new ArgumentException(error);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null || !IsSceneObject(go.transform))
                throw new ArgumentException("The handle must resolve to a loaded scene GameObject or component, not an asset.");
            return go.transform;
        }

        private static bool IsSceneObject(Transform transform) => transform != null
            && transform.gameObject.scene.IsValid() && transform.gameObject.scene.isLoaded
            && !EditorUtility.IsPersistent(transform);

        private static string SortPath(Transform transform)
        {
            var indices = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                indices.Push(current.GetSiblingIndex().ToString("D10", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("/", indices);
        }

        private static UiElementSnapshot Describe(Transform transform)
        {
            var rect = transform as RectTransform;
            var canvas = FindCanvas(transform);
            var group = transform.GetComponent<CanvasGroup>();
            var result = new UiElementSnapshot
            {
                handle = ObjectResolver.Describe(transform.gameObject),
                name = transform.name,
                scenePath = transform.gameObject.scene.path,
                sceneHandle = transform.gameObject.scene.handle,
                parent = transform.parent == null ? null : ObjectResolver.Describe(transform.parent.gameObject),
                activeSelf = transform.gameObject.activeSelf,
                activeInHierarchy = transform.gameObject.activeInHierarchy,
                siblingIndex = transform.GetSiblingIndex(),
                siblingPath = SortPath(transform),
                localScale = Vec(transform.localScale),
                localPosition = Vec(transform.localPosition),
                localEulerAngles = Vec(transform.localEulerAngles),
                lossyScale = Vec(transform.lossyScale),
                components = transform.GetComponents<Component>().Select(component => component == null
                    ? "<missing-script>" : component.GetType().FullName).ToArray(),
                canvas = DescribeCanvas(canvas),
                canvasGroup = group == null ? null : new UiCanvasGroupSnapshot
                {
                    enabled = group.enabled,
                    alpha = group.alpha,
                    interactable = group.interactable,
                    blocksRaycasts = group.blocksRaycasts,
                    ignoreParentGroups = group.ignoreParentGroups
                }
            };
            if (rect == null)
                return result;

            result.rectTransform = new UiRectSnapshot
            {
                anchorMin = Vec(rect.anchorMin), anchorMax = Vec(rect.anchorMax), pivot = Vec(rect.pivot),
                anchoredPosition = Vec(rect.anchoredPosition), sizeDelta = Vec(rect.sizeDelta),
                anchoredPosition3D = Vec(rect.anchoredPosition3D),
                offsetMin = Vec(rect.offsetMin), offsetMax = Vec(rect.offsetMax),
                rect = Bounds(rect.rect),
                drivenBy = ObjectResolver.Describe(rect.drivenByObject)
            };
            var worldCorners = new Vector3[4];
            rect.GetWorldCorners(worldCorners);
            result.rectTransform.worldCorners = worldCorners.Select(Vec).ToArray();
            result.rectTransform.projection = Project(worldCorners, canvas);
            return result;
        }

        private static Canvas FindCanvas(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                var canvas = current.GetComponent<Canvas>();
                if (canvas != null)
                    return canvas;
            }
            return null;
        }

        private static UiCanvasSnapshot DescribeCanvas(Canvas canvas)
        {
            if (canvas == null)
                return null;
            return new UiCanvasSnapshot
            {
                handle = ObjectResolver.Describe(canvas.gameObject),
                root = ObjectResolver.Describe(canvas.rootCanvas.gameObject),
                enabled = canvas.enabled,
                renderMode = canvas.renderMode.ToString(),
                overrideSorting = canvas.overrideSorting,
                sortingLayerId = canvas.sortingLayerID,
                sortingLayerValue = SortingLayer.GetLayerValueFromID(canvas.sortingLayerID),
                sortingOrder = canvas.sortingOrder,
                renderOrder = canvas.renderOrder,
                scaleFactor = canvas.scaleFactor,
                referencePixelsPerUnit = canvas.referencePixelsPerUnit,
                targetDisplay = canvas.targetDisplay,
                worldCamera = ObjectResolver.Describe(canvas.worldCamera)
            };
        }

        private static UiProjectionSnapshot Project(Vector3[] corners, Canvas canvas)
        {
            if (canvas == null)
                return new UiProjectionSnapshot { status = "no_canvas" };
            var root = canvas.rootCanvas;
            var camera = root.worldCamera;
            if (root.targetDisplay != 0)
                return new UiProjectionSnapshot { status = "unsupported_display", detail = "Only display 0 screen coordinates are supported." };
            bool overlay = root.renderMode == RenderMode.ScreenSpaceOverlay;
            if (!overlay && camera == null)
                return new UiProjectionSnapshot { status = "camera_required", detail = "No root Canvas worldCamera; no fallback camera was guessed." };
            if (!overlay && camera.targetDisplay != 0)
                return new UiProjectionSnapshot { status = "unsupported_display", detail = "The Canvas camera targets another display." };
            if (!overlay && camera.targetTexture != null)
                return new UiProjectionSnapshot { status = "camera_target_texture", detail = "The Canvas camera renders to a texture; its pixels cannot be treated as Game View screen coordinates." };
            if (!overlay && corners.Any(corner => camera.WorldToScreenPoint(corner).z <= camera.nearClipPlane))
                return new UiProjectionSnapshot { status = "near_plane_intersection", detail = "A corner is at or behind the camera near plane; no misleading screen bounds returned." };

            var screen = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(overlay ? null : camera, corner)).ToArray();
            if (screen.Any(point => float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)))
                return new UiProjectionSnapshot { status = "invalid_geometry" };
            var bounds = Rect.MinMaxRect(screen.Min(point => point.x), screen.Min(point => point.y),
                screen.Max(point => point.x), screen.Max(point => point.y));
            if (float.IsInfinity(bounds.width) || float.IsInfinity(bounds.height))
                return new UiProjectionSnapshot { status = "invalid_geometry" };
            return new UiProjectionSnapshot
            {
                status = "projected",
                screenCorners = screen.Select(Vec).ToArray(),
                screenAabb = Bounds(bounds),
                camera = ObjectResolver.Describe(overlay ? null : camera),
                viewport = Bounds(overlay ? new Rect(0, 0, Screen.width, Screen.height) : camera.pixelRect)
            };
        }

        private static float[] Vec(Vector2 vector) => new[] { vector.x, vector.y };
        private static float[] Vec(Vector3 vector) => new[] { vector.x, vector.y, vector.z };
        // [x, y, width, height], not a polygon intersection or an occlusion result.
        private static float[] Bounds(Rect rect) => new[] { rect.x, rect.y, rect.width, rect.height };
    }

    [Serializable]
    class UiSnapshot
    {
        public int frame;
        public int[] screenSize;
        public int total;
        public int offset;
        public int? nextOffset;
        public List<UiElementSnapshot> elements;
        public string[] limitations;
    }

    [Serializable]
    class UiLayoutExplanation
    {
        public int frame;
        public int[] screenSize;
        public List<UiElementSnapshot> targetToRoot;
        public string[] limitations;
    }

    [Serializable]
    class UiElementSnapshot
    {
        public AuthoringResult handle;
        public string name;
        public string scenePath;
        public int sceneHandle;
        public AuthoringResult parent;
        public bool activeSelf;
        public bool activeInHierarchy;
        public int siblingIndex;
        public string siblingPath;
        public float[] localScale;
        public float[] localPosition,localEulerAngles;
        public float[] lossyScale;
        public string[] components;
        public UiRectSnapshot rectTransform;
        public UiCanvasSnapshot canvas;
        public UiCanvasGroupSnapshot canvasGroup;
    }

    [Serializable]
    class UiRectSnapshot
    {
        public float[] anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax, rect;
        public float[] anchoredPosition3D;
        public float[][] worldCorners;
        public AuthoringResult drivenBy;
        public UiProjectionSnapshot projection;
    }

    [Serializable]
    class UiProjectionSnapshot
    {
        public string status;
        public string detail;
        public float[][] screenCorners;
        public float[] screenAabb;
        public float[] viewport;
        public AuthoringResult camera;
    }

    [Serializable]
    class UiCanvasSnapshot
    {
        public AuthoringResult handle, root, worldCamera;
        public bool enabled, overrideSorting;
        public string renderMode;
        public int sortingLayerId, sortingLayerValue, sortingOrder, renderOrder, targetDisplay;
        public float scaleFactor, referencePixelsPerUnit;
    }

    [Serializable]
    class UiCanvasGroupSnapshot
    {
        public bool enabled, interactable, blocksRaycasts, ignoreParentGroups;
        public float alpha;
    }

    [Serializable]
    class UiOverlapSnapshot
    {
        public int frame;
        public int[] screenSize;
        public int examinedElements, unprojectableElements, examinedPairs, totalCandidates;
        public bool truncated;
        public List<UiOverlapCandidate> candidates;
        public string[] limitations;
    }

    [Serializable]
    class UiOverlapCandidate
    {
        public AuthoringResult first, second;
        public float[] intersection;
        public float firstAabbFraction, secondAabbFraction;
    }
}
