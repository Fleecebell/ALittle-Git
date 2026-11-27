using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class MobileBuildSettings : IActiveBuildTargetChanged, IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    [MenuItem("Tools/移动端/应用当前平台设置")]
    private static void ApplyCurrent()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        Apply(target);
        Debug.Log(target == BuildTarget.Android || target == BuildTarget.iOS
            ? $"已应用 {target} 横屏与 64 位构建设置。"
            : $"当前平台 {target} 使用原有桌面设置。");
    }

    public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => Apply(newTarget);

    public void OnPreprocessBuild(BuildReport report) => Apply(report.summary.platform);

    private static void Apply(BuildTarget target)
    {
        if (target != BuildTarget.Android && target != BuildTarget.iOS) return;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        var platform = target == BuildTarget.Android ? NamedBuildTarget.Android : NamedBuildTarget.iOS;
        PlayerSettings.SetScriptingBackend(platform, ScriptingImplementation.IL2CPP);

        // 保留已有的合法包名。工程名 A-Little 自动派生的标识在 Android 上不合法。
        string id = PlayerSettings.GetApplicationIdentifier(platform);
        string pattern = target == BuildTarget.Android
            ? @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$"
            : @"^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$";
        if (!Regex.IsMatch(id ?? "", pattern))
            PlayerSettings.SetApplicationIdentifier(platform, "com.DefaultCompany.ALittle");

        if (target == BuildTarget.Android)
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // Unity 2022 的 iOS 真机构建目标使用 ARM64，模拟器目标单独区分。
        else
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.requiresFullScreen = true;
        }
    }
}

