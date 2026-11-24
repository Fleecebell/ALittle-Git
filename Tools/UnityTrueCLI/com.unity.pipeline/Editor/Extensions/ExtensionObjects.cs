using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    public static class ExtensionObjects
    {
        public static GameObject Resolve(ObjectRef handle)
        {
            if (!ObjectResolver.TryResolve(handle, out var value, out var error))
                throw new ArgumentException(error);
            var go = value as GameObject ?? (value as Component)?.gameObject;
            if (go == null || EditorUtility.IsPersistent(go) || !go.scene.IsValid()
                || !go.scene.isLoaded || !StageUtility.GetCurrentStageHandle().Contains(go))
                throw new ArgumentException("Expected a loaded GameObject/component in the current stage.");
            return go;
        }

        public static T Require<T>(ObjectRef handle) where T : Component
        {
            var go = Resolve(handle);
            var component = go.GetComponent<T>();
            if (component == null) throw new ArgumentException("Target has no " + typeof(T).FullName);
            return component;
        }

        public static T[] Find<T>() where T : Component => Resources.FindObjectsOfTypeAll<T>()
            .Where(component => component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded
                && !EditorUtility.IsPersistent(component)
                && StageUtility.GetCurrentStageHandle().Contains(component.gameObject)).ToArray();

        public static void Changed(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        public static float Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(name + " must be finite.");
            return value;
        }

        [CliCommand("get_cli_extensions", "List installed extension packages and actually registered command families. Missing packages do not imply commands are available.", Tags = new[] { "editor/extensions" })]
        public static object Capabilities()
        {
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            var commands = CommandRegistry.DiscoverCommands().ToArray();
            return new
            {
                unityVersion = Application.unityVersion,
                packages = packages.Where(package => new[] { "com.unity.pipeline", "com.unity.cinemachine", "com.unity.inputsystem", "com.unity.ugui", "com.unity.textmeshpro", "com.unity.timeline", "com.unity.render-pipelines.universal" }.Contains(package.name))
                    .Select(package => new { package.name, package.version }).ToArray(),
                commands = commands.Where(command => command.Tags.Any(tag => tag.StartsWith("ui/") || tag.StartsWith("cinemachine") || tag.StartsWith("input/") || tag.StartsWith("capture/observation") || tag.StartsWith("rendering/")))
                    .Select(command => new { name = command.Name, tags = command.Tags }).OrderBy(command => command.name).ToArray(),
                playerVerification = "not_claimed_by_editor_extensions"
            };
        }
    }
}
