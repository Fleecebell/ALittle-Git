#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 批量给 Build Settings 里的场景补齐 LevelLoader 预制体。
///
/// 背景（为什么要这么做）:
///   1. LevelLoader 不是单例, 也没有 DontDestroyOnLoad, 它是每个场景各放一份的预制体实例。
///      NextLevel.GoToNextLevel() 用 FindObjectOfType&lt;LevelLoader&gt;() 只在"当前已加载场景"里找它,
///      找不到就报 "[NextLevel] 未找到 LevelLoader，直接加载场景 N" 并直接 SceneManager.LoadScene。
///   2. LevelLoader.LoadLevel() 只是把静态标志 isTransitioning 置为 true 再 LoadScene;
///      真正播放 "CircleWipe_02" 揭示动画的是"新场景"里那份 LevelLoader 的 Start()。
///      所以目标场景也必须有一份, 否则连揭示动画都不会播, 是硬切。
///   3. 更隐蔽的坑: 目标场景缺少 LevelLoader 时, 没有任何人把 isTransitioning 清回 false,
///      于是后面某个"有" LevelLoader 的场景一进去就会莫名播一次过场动画。
///
/// 用法: 菜单 Tools/关卡工具/批量补齐 LevelLoader 预制体
///      菜单 Tools/关卡工具/检查 LevelLoader 覆盖情况（只读, 不改任何东西）
///
/// 建议执行前先 git commit 一次, 方便回退。
/// </summary>
public static class LevelLoaderBatchInstaller
{
    private const string PrefabPath = "Assets/Art/screen_transition/LevelLoader/LevelLoader.prefab";
    private const string MenuRoot = "Tools/关卡工具/";
    private const string LogTag = "[LevelLoader批量] ";

    // ==================================================================
    //  菜单 1: 批量补齐
    // ==================================================================

    [MenuItem(MenuRoot + "批量补齐 LevelLoader 预制体", false, 100)]
    private static void InstallMissingLoaders()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("补齐 LevelLoader", "请先退出播放模式再执行。", "好");
            return;
        }

        GameObject prefab = FindLoaderPrefab();
        if (prefab == null)
        {
            Debug.LogError(LogTag + "找不到 LevelLoader 预制体, 原路径: " + PrefabPath);
            EditorUtility.DisplayDialog("补齐 LevelLoader",
                "找不到 LevelLoader 预制体。\n\n原路径:\n" + PrefabPath +
                "\n\n请确认 Assets/Art/screen_transition/LevelLoader/LevelLoader.prefab 是否被移动或改名。", "好");
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
            Debug.Log(LogTag + "Build Settings 里所有启用场景都已有 LevelLoader, 无需处理。");
            EditorUtility.DisplayDialog("补齐 LevelLoader",
                "Build Settings 里所有启用场景都已有 LevelLoader, 无需处理。\n\n已有: " + alreadyCount + " 个场景。", "好");
            return;
        }

        // ---- 确认对话框 ----
        StringBuilder prompt = new StringBuilder();
        prompt.AppendLine("将给下面 " + targets.Count + " 个场景各实例化一份 LevelLoader 预制体并保存场景:");
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
        prompt.AppendLine("已有 LevelLoader 的场景: " + alreadyCount + " 个（不动）");
        prompt.AppendLine("当前打开的场景如果有未保存修改, 会先弹窗询问。");
        prompt.AppendLine("建议先 git commit 一次, 方便回退。");

        if (!EditorUtility.DisplayDialog("补齐 LevelLoader",
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

                if (EditorUtility.DisplayCancelableProgressBar("补齐 LevelLoader",
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
                    if (SceneHasLoader(scene))
                    {
                        skipped.Add(path);
                        continue;
                    }

                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    if (instance == null)
                        throw new System.Exception("InstantiatePrefab 返回 null");

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

        result.AppendLine("已补齐: " + succeeded.Count + " 个场景");
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

        Debug.Log(LogTag + "处理完成\n" + result);
        EditorUtility.DisplayDialog("补齐 LevelLoader", result.ToString(), "好");
    }

    // ==================================================================
    //  菜单 2: 只读检查
    // ==================================================================

    [MenuItem(MenuRoot + "检查 LevelLoader 覆盖情况", false, 101)]
    private static void ReportCoverage()
    {
        GameObject prefab = FindLoaderPrefab();
        if (prefab == null)
        {
            Debug.LogError(LogTag + "找不到 LevelLoader 预制体, 原路径: " + PrefabPath);
            return;
        }

        string prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));

        List<string> withLoader = new List<string>();
        List<string> withoutLoader = new List<string>();
        List<string> missingFiles = new List<string>();

        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene == null || !buildScene.enabled || string.IsNullOrEmpty(buildScene.path))
                continue;

            if (!File.Exists(buildScene.path))
                missingFiles.Add(buildScene.path);
            else if (SceneFileContainsGuid(buildScene.path, prefabGuid))
                withLoader.Add(buildScene.path);
            else
                withoutLoader.Add(buildScene.path);
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Build Settings 启用场景共 " + (withLoader.Count + withoutLoader.Count) + " 个");
        sb.AppendLine();

        sb.AppendLine("== 缺少 LevelLoader, 共 " + withoutLoader.Count + " 个 ==");
        if (withoutLoader.Count == 0)
            sb.AppendLine("  （无）");
        else
            for (int i = 0; i < withoutLoader.Count; i++)
                sb.AppendLine("  [缺] " + withoutLoader[i]);

        sb.AppendLine();
        sb.AppendLine("== 已有 LevelLoader, 共 " + withLoader.Count + " 个 ==");
        for (int i = 0; i < withLoader.Count; i++)
            sb.AppendLine("  [有] " + withLoader[i]);

        if (missingFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("== 文件缺失, 共 " + missingFiles.Count + " 个 ==");
            for (int i = 0; i < missingFiles.Count; i++)
                sb.AppendLine("  [丢] " + missingFiles[i]);
        }

        Debug.Log(LogTag + "覆盖情况\n" + sb);
    }

    // ==================================================================
    //  工具方法
    // ==================================================================

    /// <summary>按固定路径找预制体; 路径失效时退化为按名字+类型搜索。</summary>
    private static GameObject FindLoaderPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null && prefab.GetComponentInChildren<LevelLoader>(true) != null)
            return prefab;

        string[] guids = AssetDatabase.FindAssets("LevelLoader t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (Path.GetFileNameWithoutExtension(path) != "LevelLoader")
                continue;

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate != null && candidate.GetComponentInChildren<LevelLoader>(true) != null)
            {
                if (path != PrefabPath)
                    Debug.LogWarning(LogTag + "原路径失效, 改用: " + path);
                return candidate;
            }
        }

        return null;
    }

    /// <summary>场景里是否已有 LevelLoader（含未激活对象）。</summary>
    private static bool SceneHasLoader(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].GetComponentInChildren<LevelLoader>(true) != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 不打开场景的快速预检: 场景文件里是否出现该预制体的 guid。
    /// 只用于确认对话框的清单和只读报告; 真正改动前还会用 SceneHasLoader 再确认一次。
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
