using UnityEngine;

/// <summary>
/// 角色手感统一配置(单点调参)。
///
/// 用法:
///   1. Project 窗口右键 → Create → Game → Player Feel Settings, 生成一个资产(asset);
///   2. 把该资产拖到 P1(MoveFirst) 和 P2(MoveLate) 的 feelSettings 字段上;
///   3. 之后只在这个资产里调手感, 运行时以这里的值为准(覆盖组件上的旧字段)。
///
/// 留空 feelSettings 时, 角色退回用组件自身的字段(检查器上那些)。
/// </summary>
[CreateAssetMenu(fileName = "PlayerFeelSettings", menuName = "Game/Player Feel Settings")]
public class PlayerFeelSettings : ScriptableObject
{
    [Header("=== 移动 ===")]
    [Tooltip("水平移动速度(单位/秒)。瞬时起停、零惯性")]
    public float moveSpeed = 8f;

    [Tooltip("勾选后水平移动带渐变加速; 解谜向建议不勾(瞬时到位)")]
    public bool useAcceleration = false;

    [Tooltip("水平加速度(仅 useAcceleration 勾选时生效)")]
    public float acceleration = 30f;

    [Header("=== 跳跃 ===")]
    [Tooltip("跳跃顶点高度(世界单位)。起跳初速度会自动按它和重力反推")]
    public float jumpHeight = 2.5f;

    [Tooltip("竖直重力倍数(1 = 真实重力 9.81 m/s²)。越大下落越快、跳跃越脆、滞空越短")]
    public float gravityScale = 4f;

    [Tooltip("跳跃缓冲(秒): 落地前这么短时间内按跳, 落地瞬间自动起跳")]
    public float jumpBufferDuration = 0.15f;

    [Tooltip("起跳冷却(秒): 跳起后这么短时间内不能再跳")]
    public float jumpLockDuration = 0.25f;

    [Header("=== 冲刺 ===")]
    [Tooltip("冲刺水平速度")]
    public float dashForce = 20f;

    [Tooltip("冲刺持续时长(秒)")]
    public float dashDuration = 0.1f;

    [Tooltip("冲刺冷却(秒)")]
    public float dashCooldown = 1f;

    [Header("=== 爬梯 ===")]
    [Tooltip("爬梯速度")]
    public float climbSpeed = 5f;

    [Header("=== 落地 / 碰撞手感 ===")]
    [Tooltip("接触法线相对竖直向上小于该角度就判为着地(度)")]
    public float groundAngleThreshold = 45f;

    [Tooltip("单向平台着地容差(角色脚底比平台顶面低多少以内算踩到)")]
    public float oneWayPlatformSurfaceTolerance = 0.1f;

    [Tooltip("按 S 从单向平台下穿的持续时间(秒)")]
    public float dropThroughDuration = 0.3f;

    [Header("=== 变大变小 ===")]
    [Tooltip("变大目标缩放倍数(1 = 原大小, 2 = 两倍)")]
    public float bigScale = 2f;

    [Tooltip("变大/变小过渡速度")]
    public float growShrinkSpeed = 5f;

    /// <summary>根据 jumpHeight 和 gravityScale 反推出起跳初速度(m/s)</summary>
    public float ComputeJumpForce()
    {
        float g = gravityScale * 9.81f;
        return Mathf.Sqrt(2f * g * Mathf.Max(0.01f, jumpHeight));
    }
}
