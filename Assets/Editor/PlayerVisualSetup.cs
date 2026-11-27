using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 PlayerControl.prefab 里两个角色的视觉拆成两层节点，并挂上 PlayerSquashStretch（落地 Q 弹）。
/// 菜单：Tools/角色工具/玩家视觉层级（拆出 Visual / Art / 挂 Q弹）
///
/// 目标层级：
///   Player(Player2)                ← 物理体：Rigidbody2D + BoxCollider2D + 全部游戏脚本
///   ├─ Visual                      ← 缩放节点：运行时旋转被抵消，缩放轴 = 世界轴
///   │  └─ Art                      ← 画面节点：运行时旋转设回方块自身，贴图继续翻滚
///   │     ├─ SpriteRenderer
///   │     └─ Animator
///   └─ VFX ...                     ← 粒子留在外面，不跟着变形
///
/// 为什么必须分两层：
///   方块会被物理转起来（角阻尼很低、旋转没冻结）。"落地压扁"要贴地面（世界竖直）、
///   "被踩拉宽"要贴地面往两边摊（世界水平），这两个都是世界轴；
///   而贴图本身还要继续跟着方块翻滚 —— 一个节点的 localScale 只能表达一个坐标系，所以必须分开。
///
/// 为什么必须把 SpriteRenderer 从根节点搬走：
///   根节点上同时挂着 BoxCollider2D 和渲染器，直接缩放根节点会连碰撞体一起缩放，
///   而工程里 WallAhead、单向平台脚底判定、GroundCarry 脚底探测、PlayerVFX 都在读 bounds。
///
/// 为什么 Animator 必须跟 SpriteRenderer 在一起：
///   动画 clip 的绑定是 classID 212 / attribute m_Sprite / path 空，
///   path 空 = 绑定在 Animator 自己所在的物体上。只搬渲染器会让跳跃换帧静默失效。
///
/// 这个工具是幂等的：已经拆过就只补缺的节点/组件，不会重复建。
/// 改动前后都能用 git 回滚（.prefab 是 Force Text）。
/// </summary>
public static class PlayerVisualSetup
{
    private const string PrefabPath = "Assets/Prefabs/PlayerControl.prefab";
    private const string VisualName = "Visual";
    private const string ArtName = "Art";

    [MenuItem("Tools/角色工具/玩家视觉层级（拆出 Visual / Art / 挂 Q弹）", false, 20)]
    public static void SplitVisualNodes()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError($"[PlayerVisualSetup] 找不到 prefab：{PrefabPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        int changed = 0;
        int found = 0;

        try
        {
            // 主角根 = 带 Rigidbody2D、并且"有渲染器或有 Visual 子物体"的节点。
            // 注意两点：
            //   · 不能只看"有 SpriteRenderer"：拆过之后渲染器在 Art 上，再跑一次会找不到主角根；
            //   · Art / Visual 自己没有 Rigidbody2D，天然不会被当成主角根。
            List<Transform> players = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                if (t.GetComponent<Rigidbody2D>() == null) continue;
                if (t.GetComponent<SpriteRenderer>() == null && t.Find(VisualName) == null) continue;
                players.Add(t);
            }

            found = players.Count;
            if (found == 0)
            {
                Debug.Log("[PlayerVisualSetup] 没有找到需要处理的主角节点（可能已经拆好了）");
            }

            foreach (Transform player in players)
            {
                if (SplitOne(player)) changed++;
            }

            if (changed > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[PlayerVisualSetup] 完成：处理了 {changed} 个角色，已保存 {PrefabPath}");
                VerifySaved();
            }
            else
            {
                Debug.Log("[PlayerVisualSetup] 无需改动（层级已经拆好，组件也已挂上）");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 处理一个角色：建/找 Visual、Art → 搬 SpriteRenderer + Animator 到 Art →
    /// 重指 jumpAnimator → 挂 Q 弹组件并接好 visual / art / feelSettings。
    /// </summary>
    private static bool SplitOne(Transform player)
    {
        bool didSomething = false;

        // ---- 1. 缩放节点 Visual ----
        Transform visual = player.Find(VisualName);
        if (visual == null)
        {
            visual = CreateEmptyChild(player, VisualName);
            didSomething = true;
        }

        // ---- 2. 画面节点 Art ----
        Transform art = visual.Find(ArtName);
        if (art == null)
        {
            art = CreateEmptyChild(visual, ArtName);
            didSomething = true;
        }

        // ---- 3. 搬 SpriteRenderer（可能还挂在 player，也可能在 Visual 上） ----
        SpriteRenderer srcRenderer = player.GetComponent<SpriteRenderer>();
        if (srcRenderer == null) srcRenderer = visual.GetComponent<SpriteRenderer>();

        if (srcRenderer != null)
        {
            SpriteRenderer dstRenderer = art.GetComponent<SpriteRenderer>();
            if (dstRenderer == null)
            {
                dstRenderer = art.gameObject.AddComponent<SpriteRenderer>();
                // CopySerialized = 编辑器里的"复制/粘贴组件值"：精灵、材质、排序层、
                // 排序序号、翻转、遮罩交互全部原样带过去。
                EditorUtility.CopySerialized(srcRenderer, dstRenderer);
            }
            Object.DestroyImmediate(srcRenderer);
            didSomething = true;
        }

        // ---- 4. 搬 Animator（必须和渲染器在一起） ----
        Animator srcAnimator = player.GetComponent<Animator>();
        if (srcAnimator == null) srcAnimator = visual.GetComponent<Animator>();

        Animator artAnimator = art.GetComponent<Animator>();
        if (srcAnimator != null)
        {
            if (artAnimator == null)
            {
                artAnimator = art.gameObject.AddComponent<Animator>();
                EditorUtility.CopySerialized(srcAnimator, artAnimator);
            }
            Object.DestroyImmediate(srcAnimator);
            didSomething = true;
        }

        // ---- 5. 把 MoveFirst / MoveLate 的 jumpAnimator 重指到 Art 上的 Animator ----
        if (artAnimator != null)
        {
            foreach (MonoBehaviour mb in player.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                SerializedObject so = new SerializedObject(mb);
                SerializedProperty prop = so.FindProperty("jumpAnimator");
                if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (prop.objectReferenceValue == artAnimator) continue;
                prop.objectReferenceValue = artAnimator;
                so.ApplyModifiedPropertiesWithoutUndo();
                didSomething = true;
            }
        }

        // ---- 6. 挂 PlayerSquashStretch 并接好引用 ----
        PlayerSquashStretch squash = player.GetComponent<PlayerSquashStretch>();
        if (squash == null)
        {
            squash = player.gameObject.AddComponent<PlayerSquashStretch>();
            didSomething = true;
        }

        PlayerFeelSettings feel = null;
        MoveFirst mf = player.GetComponent<MoveFirst>();
        MoveLate ml = player.GetComponent<MoveLate>();
        if (mf != null) feel = mf.feelSettings;
        else if (ml != null) feel = ml.feelSettings;

        SerializedObject squashSO = new SerializedObject(squash);
        didSomething |= AssignRef(squashSO, "visual", visual);
        didSomething |= AssignRef(squashSO, "art", art);
        didSomething |= AssignRef(squashSO, "feelSettings", feel);
        squashSO.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[PlayerVisualSetup] {player.name}：" +
                  $"Visual={(visual != null ? "OK" : "缺失")}，" +
                  $"Art={(art != null ? "OK" : "缺失")}，" +
                  $"SpriteRenderer={(art.GetComponent<SpriteRenderer>() != null ? "OK" : "缺失")}，" +
                  $"Animator={(art.GetComponent<Animator>() != null ? "OK" : "无")}");
        return didSomething;
    }

    private static Transform CreateEmptyChild(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    // 只在引用没指向目标时才写，避免无意义的标脏
    private static bool AssignRef(SerializedObject so, string field, Object target)
    {
        if (target == null) return false;
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null) return false;
        if (prop.objectReferenceValue == target) return false;
        prop.objectReferenceValue = target;
        return true;
    }

    /// <summary>
    /// 保存后回读 prefab，确认改动真的落盘了（新建的子物体必须确实进了 prefab 资产，
    /// 否则运行时找不到 Art，变形会退回老结构/静默失效）。
    /// </summary>
    private static void VerifySaved()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null)
        {
            Debug.LogError("[PlayerVisualSetup] 回读失败：prefab 加载不出来");
            return;
        }

        int ok = 0, bad = 0;
        foreach (Transform t in asset.GetComponentsInChildren<Transform>(true))
        {
            if (t == asset.transform) continue;
            if (t.GetComponent<Rigidbody2D>() == null) continue;

            Transform visual = t.Find(VisualName);
            Transform art = visual != null ? visual.Find(ArtName) : null;

            bool artOk = art != null
                         && art.GetComponent<SpriteRenderer>() != null
                         && art.GetComponent<Animator>() != null;
            bool clean = t.GetComponent<SpriteRenderer>() == null
                         && t.GetComponent<Animator>() == null
                         && (visual == null || visual.GetComponent<SpriteRenderer>() == null);
            bool hasSquash = t.GetComponent<PlayerSquashStretch>() != null;

            if (artOk && clean && hasSquash) ok++;
            else
            {
                bad++;
                Debug.LogError($"[PlayerVisualSetup] {t.name} 回读校验失败：" +
                               $"Art={artOk} 上层已清空={clean} 已挂Q弹={hasSquash}");
            }
        }
        Debug.Log($"[PlayerVisualSetup] 回读校验：{ok} 个角色正常，{bad} 个异常");
    }

    /// <summary>
    /// 只读检查：把 PlayerControl.prefab 的当前层级打到 Console（不写任何东西）。
    /// </summary>
    [MenuItem("Tools/角色工具/检查玩家视觉层级（只读）", false, 21)]
    public static void DumpHierarchy()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[PlayerVisualSetup] 当前层级：");
            DumpRecursive(root.transform, 0, sb);
            Debug.Log(sb.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void DumpRecursive(Transform t, int depth, System.Text.StringBuilder sb)
    {
        if (depth > 4) return;
        sb.Append(' ', depth * 2).Append("- ").Append(string.IsNullOrEmpty(t.name) ? "(空名)" : t.name);
        if (t.GetComponent<Rigidbody2D>() != null) sb.Append(" [Rigidbody2D]");
        if (t.GetComponent<Collider2D>() != null) sb.Append(" [Collider]");
        if (t.GetComponent<SpriteRenderer>() != null) sb.Append(" [SpriteRenderer]");
        if (t.GetComponent<Animator>() != null) sb.Append(" [Animator]");
        if (t.GetComponent<PlayerSquashStretch>() != null) sb.Append(" [SquashStretch]");
        sb.AppendLine();
        foreach (Transform child in t) DumpRecursive(child, depth + 1, sb);
    }
}
