#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 批量给 Build Settings 里的场景补齐「每个关卡都要有」的预制体实例:
///
///   1. LevelLoader —— 场景切换的圆圈转场。缺了会硬切, 而且 isTransitioning 静态标志没人清回 false,
///      后面某个有 LevelLoader 的场景一进去就会莫名播一次过场动画。
///   2. SetCanvas   —— 设置面板 Canvas(音量滑条 / 计时器开关 / 返回菜单)。缺了该场景就没有设置界面,
///      UIManager 相关的引用也找不到。
///
/// 规则统一: 场景里已经有就跳过, 没有才实例化一份并保存场景。
///
/// 菜单:
///   Tools/关卡工具/批量补齐 LevelLoader 预制体
///   Tools/关卡工具/检查 LevelLoader 覆盖情况        （只读, 不改任何东西）
///   Tools/关卡工具/批量补齐 SetCanvas 预制体
///   Tools/关卡工具/检查 SetCanvas 覆盖情况          （只读, 不改任何东西）
///
/// 建议执行前先 git commit 一次, 方便回退。
/// </summary>
public static class LevelLoaderBatchInstaller
{
    private const string MenuRoot = "Tools/关卡工具/";
    private const string LogTag = "[场景预制体批量] ";

    /// <summary>一个「每关都要有」的预制体描述。</summary>
    private class PrefabTarget
    {
        public string DisplayName;              // 显示名, 如 "LevelLoader"
        public string PrefabPath;               // 固定路径(失效时按文件名兜底搜索)
        public bool ResetTransform;             // 实例化后把位置/旋转归零
        public System.Type RequiredComponent;   // 场景里任意物体已有该组件 → 认为已存在
        public string RootName;                 // 场景里已有同名根物体 → 认为已存在(防手动放的非预制体副本)
    }

    private static readonly PrefabTarget LevelLoaderTarget = new PrefabTarget
    {
        DisplayName = "LevelLoader",
        PrefabPath = "Assets/Art/screen_transition/LevelLoader/LevelLoader.prefab",
        ResetTransform = true,
        RequiredComponent = typeof(LevelLoader),
        RootName = null
    };

    private static readonly PrefabTarget SetCanvasTarget = new PrefabTarget
    {
        DisplayName = "SetCanvas",
        PrefabPath = "Assets/Prefabs/SetCanvas.prefab",
        ResetTransform = true,
        RequiredComponent = null,
        RootName = "SetCanvas"
    };

    // ==================================================================
    //  菜单
    // ==================================================================

    [MenuItem(MenuRoot + "批量补齐 LevelLoader 预制体", false, 100)]
    private static void InstallLevelLoader()
    {
        InstallMissing(LevelLoaderTarget);
    }

    [MenuItem(MenuRoot + "检查 LevelLoader 覆盖情况", false, 101)]
    private static void ReportLevelLoader()
    {
        ReportCoverage(LevelLoaderTarget);
    }

    [MenuItem(MenuRoot + "批量补齐 SetCanvas 预制体", false, 110)]
    private static void InstallSetCanvas()
    {
        InstallMissing(SetCanvasTarget);
    }

    [MenuItem(MenuRoot + "检查 SetCanvas 覆盖情况", false, 111)]
    private static void ReportSetCanvas()
    {
        ReportCoverage(SetCanvasTarget);
    }

    // ==================================================================
    //  批量补齐
    // ==================================================================

    private static void InstallMissing(PrefabTarget target)
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("补齐 " + target.DisplayName, "请先退出播放模式再执行。", "好");
            return;
        }

        GameObject prefab = FindPrefab(target);
        if (prefab == null)
        {
            Debug.LogError(LogTag + "找不到 " + target.DisplayName + " 预制体, 原路径: " + target.PrefabPath);
            EditorUtility.DisplayDialog("补齐 " + target.DisplayName,
                "找不到 " + target.DisplayName + " 预制体。\n\n原路径:\n" + target.PrefabPath +
                "\n\n请确认该预制体是否被移动或改名。", "好");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

        List<string> targets = new List<string>();
        List<string> missingFiles = new List<string>();
        int alreadyCount = 0;

        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene == null || !buildScene.enabled || string.IsNullOrEmpty(buildScene.path))
                continue;

            if (!File.Exists(buildScene.path))
            {
                missingFiles.Add(buildScene.path);
                continue;
            }

            if (SceneFileContainsGuid(buildScene.path, prefabGuid))
                alreadyCount++;
            else
                targets.Add(buildScene.path);
        }

        if (targets.Count == 0)
        {
            Debug.Log(LogTag + "Build Settings 里所有启用场景都已有 " + target.DisplayName + ", 无需处理。");
            EditorUtility.DisplayDialog("补齐 " + target.DisplayName,
                "Build Settings 里所有启用场景都已有 " + target.DisplayName + ", 无需处理。\n\n已有: " +
                alreadyCount + " 个场景。", "好");
            return;
        }

        // ---- 确认对话框 ----
        StringBuilder prompt = new StringBuilder();
        prompt.AppendLine("将给下面 " + targets.Count + " 个场景各实例化一份 " + target.DisplayName + " 预制体并保存场景:");
        prompt.AppendLine();
        for (int i = 0; i < targets.Count; i++)
            prompt.AppendLine("  " + targets[i]);

        if (missingFiles.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("下列场景文件在磁盘上不存在, 会跳过:");
            for (int i = 0; i < missingFiles.Count; i++)
                prompt.AppendLine("  " + missingFiles[i]);
        }

        prompt.AppendLine();
        prompt.AppendLine("已有 " + target.DisplayName + " 的场景: " + alreadyCount + " 个（不动）");
        prompt.AppendLine("当前打开的场景如果有未保存修改, 会先弹窗询问。");
        prompt.AppendLine("建议先 git commit 一次, 方便回退。");

        if (!EditorUtility.DisplayDialog("补齐 " + target.DisplayName,
                prompt.ToString(), "开始处理 (" + targets.Count + " 个场景)", "取消"))
        {
            Debug.Log(LogTag + "用户取消, 未做任何修改。");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log(LogTag + "未保存的场景处理被取消, 已中止。");
            return;
        }

        // 记下处理前的场景布局, 结束后恢复
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

        List<string> succeeded = new List<string>();
        List<string> skipped = new List<string>();
        List<string> failed = new List<string>();
        bool userCancelled = false;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                string path = targets[i];

                if (EditorUtility.DisplayCancelableProgressBar("补齐 " + target.DisplayName,
                        "(" + (i + 1) + "/" + targets.Count + ")  " + path +
                        "\n\n按取消可安全停止: 已处理的场景已保存, 未处理的保持原样。",
                        (float)i / targets.Count))
                {
                    userCancelled = true;
                    break;
                }

                try
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (!scene.IsValid())
                        throw new System.Exception("OpenScene 返回了无效场景");

                    // 打开后再确认一次（含未激活对象）, 防止文字预检漏判导致重复添加
                    if (SceneHasTarget(scene, target, prefabPath))
                    {
                        skipped.Add(path);
                        continue;
                    }

                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    if (instance == null)
                        throw new System.Exception("InstantiatePrefab 返回 null");

                    if (target.ResetTransform)
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new System.Exception("SaveScene 返回 false");

                    succeeded.Add(path);
                }
                catch (System.Exception e)
                {
                    failed.Add(path + "   →   " + e.Message);
                    Debug.LogError(LogTag + path + " 处理失败: " + e);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // 恢复处理前的场景布局
        try
        {
            if (previousSetup != null && previousSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning(LogTag + "恢复原场景布局失败（不影响结果）: " + e.Message);
        }

        AssetDatabase.SaveAssets();

        // ---- 汇总 ----
        StringBuilder result = new StringBuilder();
        if (userCancelled)
            result.AppendLine("已被取消, 下面是取消前已完成的部分。");

        result.AppendLine("已补齐 " + target.DisplayName + ": " + succeeded.Count + " 个场景");
        for (int i = 0; i < succeeded.Count; i++)
            result.AppendLine("  [OK]   " + succeeded[i]);

        if (skipped.Count > 0)
        {
            result.AppendLine();
            result.AppendLine("打开后发现已有, 跳过: " + skipped.Count + " 个");
            for (int i = 0; i < skipped.Count; i++)
                result.AppendLine("  [跳过] " + skipped[i]);
        }

        if (failed.Count > 0)
        {
            result.AppendLine();
            result.AppendLine("失败: " + failed.Count + " 个（详见 Console）");
            for (int i = 0; i < failed.Count; i++)
                result.AppendLine("  [失败] " + failed[i]);
        }

        Debug.Log(LogTag + target.DisplayName + " 处理完成\n" + result);
        EditorUtility.DisplayDialog("补齐 " + target.DisplayName, result.ToString(), "好");
    }

    // ==================================================================
    //  只读检查
    // ==================================================================

    private static void ReportCoverage(PrefabTarget target)
    {
        GameObject prefab = FindPrefab(target);
        if (prefab == null)
        {
            Debug.LogError(LogTag + "找不到 " + target.DisplayName + " 预制体, 原路径: " + target.PrefabPath);
            return;
        }

        string prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));

        List<string> withPrefab = new List<string>();
        List<string> withoutPrefab = new List<string>();
        List<string> missingFiles = new List<string>();

        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene == null || !buildScene.enabled || string.IsNullOrEmpty(buildScene.path))
                continue;

            if (!File.Exists(buildScene.path))
                missingFiles.Add(buildScene.path);
            else if (SceneFileContainsGuid(buildScene.path, prefabGuid))
                withPrefab.Add(buildScene.path);
            else
                withoutPrefab.Add(buildScene.path);
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(target.DisplayName + " 覆盖情况（Build Settings 启用场景共 " +
                      (withPrefab.Count + withoutPrefab.Count) + " 个）");
        sb.AppendLine();

        sb.AppendLine("== 缺少 " + target.DisplayName + ", 共 " + withoutPrefab.Count + " 个 ==");
        if (withoutPrefab.Count == 0)
            sb.AppendLine("  （无）");
        else
            for (int i = 0; i < withoutPrefab.Count; i++)
                sb.AppendLine("  [缺] " + withoutPrefab[i]);

        sb.AppendLine();
        sb.AppendLine("== 已有 " + target.DisplayName + ", 共 " + withPrefab.Count + " 个 ==");
        for (int i = 0; i < withPrefab.Count; i++)
            sb.AppendLine("  [有] " + withPrefab[i]);

        if (missingFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("== 文件缺失, 共 " + missingFiles.Count + " 个 ==");
            for (int i = 0; i < missingFiles.Count; i++)
                sb.AppendLine("  [丢] " + missingFiles[i]);
        }

        Debug.Log(LogTag + sb);
    }

    // ==================================================================
    //  工具方法
    // ==================================================================

    /// <summary>按固定路径找预制体; 路径失效时退化为按名字+类型搜索。</summary>
    private static GameObject FindPrefab(PrefabTarget target)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabPath);
        if (prefab != null && MatchesTargetPrefab(prefab, target))
            return prefab;

        string fileName = Path.GetFileNameWithoutExtension(target.PrefabPath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        string[] guids = AssetDatabase.FindAssets(fileName + " t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (Path.GetFileNameWithoutExtension(path) != fileName)
                continue;

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate != null && MatchesTargetPrefab(candidate, target))
            {
                if (path != target.PrefabPath)
                    Debug.LogWarning(LogTag + "原路径失效, 改用: " + path);
                return candidate;
            }
        }

        return null;
    }

    /// <summary>预制体本身是否就是我们要的那个（防止同名但内容不对的文件）。</summary>
    private static bool MatchesTargetPrefab(GameObject prefab, PrefabTarget target)
    {
        if (target.RequiredComponent == null)
            return true;

        return prefab.GetComponentInChildren(target.RequiredComponent, true) != null;
    }

    /// <summary>
    /// 场景里是否已经有了目标预制体（含未激活对象）。
    /// 三种判定, 满足其一即算有: ① 有必需组件 ② 有同名根物体 ③ 根物体是这份预制体的实例。
    /// </summary>
    private static bool SceneHasTarget(Scene scene, PrefabTarget target, string prefabPath)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null)
                continue;

            if (target.RequiredComponent != null &&
                root.GetComponentInChildren(target.RequiredComponent, true) != null)
                return true;

            if (!string.IsNullOrEmpty(target.RootName) && root.name == target.RootName)
                return true;

            if (!string.IsNullOrEmpty(prefabPath) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == prefabPath)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 不打开场景的快速预检: 场景文件里是否出现该预制体的 guid。
    /// 只用于确认对话框的清单和只读报告; 真正改动前还会用 SceneHasTarget 再确认一次。
    /// </summary>
    private static bool SceneFileContainsGuid(string scenePath, string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return false;

        try
        {
            return File.ReadAllText(scenePath).Contains(guid);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning(LogTag + "读取场景文件失败, 按「缺少」处理: " + scenePath + " (" + e.Message + ")");
            return false;
        }
    }
}
#endif
