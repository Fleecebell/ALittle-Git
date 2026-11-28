using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Serialization;

/// <summary>
/// 玩家视觉特效控制器 - 处理行走粒子、落地特效等
/// </summary>
public class PlayerVFX : MonoBehaviour
{
    // ===== 组件引用 =====
    [Header("组件引用")]
    [Tooltip("脚步粒子系统（挂在Player的子物体上，会自动调整位置）")]
    [FormerlySerializedAs("_footstepParticle")]
    [SerializeField] private ParticleSystem 脚步粒子系统;

    [Tooltip("落地爆发粒子系统（可选）")]
    [FormerlySerializedAs("_landParticle")]
    [SerializeField] private ParticleSystem 落地粒子系统;

    [Tooltip("地面检测触发器（挂在Player子物体上的触发碰撞体，Is Trigger需勾选）\n用于提前检测地面并采样颜色，确保落地时颜色已就绪")]
    [SerializeField] private Collider2D 地面检测触发器;

    // ===== 配置参数 =====
    [Header("粒子配置")]
    [Tooltip("行走时粒子发射间隔（秒）")]
    [FormerlySerializedAs("_emissionInterval")]
    [SerializeField] private float 粒子发射间隔 = 0.15f;

    [Tooltip("实际移动速度阈值（超过此值才发射粒子，单位：米/秒）\n注意：此处检测的是角色真实位移速度，怼墙不动时不会触发")]
    [FormerlySerializedAs("_moveSpeedThreshold")]
    [SerializeField] private float 移动速度阈值 = 0.1f;

    [Tooltip("落地时发射的粒子数量")]
    [FormerlySerializedAs("_landEmissionCount")]
    [SerializeField] private int 落地粒子数量 = 5;

    [Tooltip("粒子发射点距离玩家中心的偏移量")]
    [FormerlySerializedAs("_particleOffset")]
    [SerializeField] private float 粒子偏移量 = 0.3f;

    [Header("落地粒子配置")]
    [Tooltip("落地粒子冷却时间（秒）- 两次落地粒子之间的最小间隔")]
    [FormerlySerializedAs("_landCooldown")]
    [SerializeField] private float 落地冷却时间 = 1.0f;

    [Tooltip("触发落地粒子的最小跳跃高度（单位：米）\n低于此高度的跳跃（如普通小跳）不会触发落地粒子")]
    [FormerlySerializedAs("_minJumpHeightForLand")]
    [SerializeField] private float 最小触发跳跃高度 = 1.5f;

    [Header("地面颜色配置")]
    [Tooltip("默认地面粒子颜色（当无法从地面获取颜色时使用）")]
    [FormerlySerializedAs("_defaultColor")]
    [SerializeField] private Color 默认地面颜色 = Color.gray;

    // ===== 私有变量 =====
    private Rigidbody2D _rb2D;           // 玩家的刚体组件，用于获取移动速度
    private MoveFirst _moveFirst;         // 引用MoveFirst脚本，获取地面状态
    private MoveLate _moveLate;           // 引用MoveLate脚本（如果是P2）
    private Color _currentGroundColor;    // 当前地面的粒子颜色
    private ParticleSystem.MainModule _footstepMain; // 脚步粒子的主模块，用于修改颜色
    private ParticleSystem.MainModule _landMain; // 落地粒子的主模块
    private float _lastEmissionTime = 0;  // 上次发射脚步粒子的时间
    private float _lastLandTime = 0;      // 上次播放落地粒子的时间
    private bool _wasGrounded = false;    // 记录上一帧是否在地面，用于检测落地瞬间
    private Vector2 _groundNormal = Vector2.down; // 当前地面的法线方向（向下为默认）
    private Vector3 _particlePosition;    // 当前粒子发射位置
    private float _jumpStartY = 0;        // 跳跃开始时的Y坐标
    private float _jumpMaxHeight = 0;     // 本次跳跃的最大高度
    private Vector3 _lastPosition;        // 上一帧的位置（用于计算实际位移，判断是否真正在移动）
    private Vector3Int _lastTileCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue); // 上次采样的瓦片坐标（缓存优化，避免每帧重复采样）
    private ContactPoint2D[] _contactBuffer = new ContactPoint2D[8]; // 碰撞接触点缓冲区（复用，避免每帧分配数组）
    private Collider2D[] _overlapBuffer = new Collider2D[8]; // 触发器重叠检测缓冲区（复用，避免每帧分配数组）
    private ContactFilter2D _groundFilter; // 地面检测过滤器

    // ===== 初始化 =====
    private void Awake()
    {
        // 获取玩家的Rigidbody2D组件
        _rb2D = GetComponent<Rigidbody2D>();

        // 获取MoveFirst或MoveLate组件（用于获取地面状态）
        _moveFirst = GetComponent<MoveFirst>();
        _moveLate = GetComponent<MoveLate>();

        // 如果没有手动指定脚步粒子，自动查找子物体
        if (脚步粒子系统 == null)
        {
            Transform footstepTransform = transform.Find("VFX_Footstep");
            if (footstepTransform != null)
            {
                脚步粒子系统 = footstepTransform.GetComponent<ParticleSystem>();
            }
        }

        // ★ 无条件初始化当前地面颜色为默认颜色 ★
        // 之前这句被关在"脚步粒子系统 != null"条件里，导致如果脚步粒子没赋值，
        // _currentGroundColor 就保持默认的 Color(0,0,0,0)（全透明黑），
        // 落地粒子用这个透明色发射，就会显示成白色。
        _currentGroundColor = 默认地面颜色;

        // 初始化粒子主模块引用
        if (脚步粒子系统 != null)
        {
            _footstepMain = 脚步粒子系统.main;

            // 确保粒子系统一开始是停止状态
            脚步粒子系统.Stop();
        }
        else
        {
            Debug.LogWarning("PlayerVFX: 脚步粒子系统未设置！", this);
        }

        // 初始化落地粒子主模块引用
        if (落地粒子系统 != null)
        {
            _landMain = 落地粒子系统.main;
            // ★ 把落地粒子的 main 模块颜色也初始化为默认地面颜色 ★
            // 这样即使游戏刚开始还没采样到地面颜色，落地粒子也不会是 Inspector 里的白色
            _landMain.startColor = 默认地面颜色;
        }

        // 初始化地面检测过滤器（不过滤层级，只排除触发器自身）
        _groundFilter = new ContactFilter2D();
        _groundFilter.useTriggers = false; // 不检测其他触发器，只检测实体碰撞体

        // 初始化粒子位置
        _particlePosition = transform.position;

        // 初始化跳跃高度记录
        _jumpStartY = transform.position.y;
        _jumpMaxHeight = transform.position.y;

        // 初始化上一帧位置记录（避免第一帧产生巨大位移误判）
        _lastPosition = transform.position;
    }

    // ===== 更新逻辑 =====
    private void Update()
    {
        // 如果没有脚步粒子系统，直接返回
        if (脚步粒子系统 == null) return;

        // 更新粒子发射位置（跟随地面接触方向）
        UpdateParticlePosition();

        // 用触发器提前检测地面并采样颜色（在落地检测之前）
        CheckTriggerGround();

        // 检查是否应该发射脚步粒子
        CheckFootstepEmission();

        // 检测落地瞬间，播放落地爆发粒子
        CheckLandEffect();

        // 更新跳跃高度记录
        UpdateJumpHeight();

        // 记录本帧位置，供下一帧计算实际位移使用（放在最后，确保每帧都更新）
        _lastPosition = transform.position;
    }

    /// <summary>
    /// 更新粒子发射位置，让粒子从接触地面的一侧发射
    /// </summary>
    private void UpdateParticlePosition()
    {
        // 计算粒子发射位置：玩家中心 + 地面法线方向 * 偏移量
        // 地面法线是从地面指向玩家的方向，所以取反就是从玩家指向地面的方向
        Vector3 offset = -_groundNormal * 粒子偏移量;
        _particlePosition = transform.position + new Vector3(offset.x, offset.y, 0f);

        // 更新粒子系统的位置
        脚步粒子系统.transform.position = _particlePosition;

        // 如果有落地粒子系统，也更新它的位置
        if (落地粒子系统 != null)
        {
            落地粒子系统.transform.position = _particlePosition;
        }
    }

    /// <summary>
    /// 更新跳跃高度记录
    /// </summary>
    private void UpdateJumpHeight()
    {
        bool isGrounded = IsGrounded();

        // 如果在地面上，记录当前位置作为跳跃起始点
        if (isGrounded)
        {
            _jumpStartY = transform.position.y;
            _jumpMaxHeight = transform.position.y;
        }
        else
        {
            // 如果在空中，更新最大跳跃高度
            if (transform.position.y > _jumpMaxHeight)
            {
                _jumpMaxHeight = transform.position.y;
            }
        }
    }

    /// <summary>
    /// 检查并发射脚步粒子
    /// 注意：使用"实际位移"来判断是否在移动，而非Rigidbody速度。
    /// 这样角色怼墙时（有速度但无位移）不会触发行走粒子。
    /// </summary>
    private void CheckFootstepEmission()
    {
        // 条件1：必须在地面上（通过MoveFirst/MoveLate获取状态）
        if (!IsGrounded()) return;

        // 条件2：必须有Rigidbody2D组件
        if (_rb2D == null) return;

        // 条件3：计算"实际水平移动速度"
        // 用本帧位置减去上一帧位置，得到实际位移，再除以帧时间得到实际速度
        // 这样即使移动代码设置了速度，只要角色被墙挡住没有实际位移，就不会触发粒子
        float horizontalDisplacement = transform.position.x - _lastPosition.x;
        float actualSpeed = Time.deltaTime > 0 ? Mathf.Abs(horizontalDisplacement) / Time.deltaTime : 0f;

        // 实际速度必须超过阈值才发射粒子
        if (actualSpeed <= 移动速度阈值) return;

        // 条件4：距离上次发射的时间必须超过间隔
        if (Time.time - _lastEmissionTime >= 粒子发射间隔)
        {
            // 发射粒子
            PlayFootstepParticle();

            // 更新上次发射时间
            _lastEmissionTime = Time.time;
        }
    }

    /// <summary>
    /// 检测落地瞬间并播放落地粒子
    /// </summary>
    private void CheckLandEffect()
    {
        bool isGrounded = IsGrounded();

        // 如果上一帧不在地面，这一帧在地面，说明刚落地
        if (isGrounded && !_wasGrounded)
        {
            // 计算本次跳跃的高度
            float jumpHeight = _jumpMaxHeight - _jumpStartY;

            // 条件1：跳跃高度必须超过阈值（普通小跳不触发，只有较高跳跃才触发）
            // 条件2：距离上次播放落地粒子必须超过冷却时间
            if (jumpHeight >= 最小触发跳跃高度 &&
                Time.time - _lastLandTime >= 落地冷却时间)
            {
                PlayLandParticle();
                _lastLandTime = Time.time;
            }
        }

        // 更新落地状态记录
        _wasGrounded = isGrounded;
    }

    /// <summary>
    /// 获取当前是否在地面上（通过MoveFirst或MoveLate脚本）
    /// </summary>
    /// <returns>是否在地面上</returns>
    private bool IsGrounded()
    {
        // 如果有MoveFirst组件，使用它的地面状态
        if (_moveFirst != null)
        {
            return _moveFirst.isGrounded;
        }

        // 如果有MoveLate组件，使用它的地面状态
        if (_moveLate != null)
        {
            return _moveLate.isGrounded;
        }

        // 默认返回false
        return false;
    }

    /// <summary>
    /// 播放脚步粒子
    /// </summary>
    private void PlayFootstepParticle()
    {
        // 使用 EmitParams 直接指定本次发射粒子的颜色
        // 这比先设 main.startColor 再 Emit 更可靠，不存在同帧时序问题
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
        emitParams.startColor = _currentGroundColor;

        // 发射1个粒子，颜色由 emitParams 直接指定
        脚步粒子系统.Emit(emitParams, 1);
    }

    /// <summary>
    /// 播放落地爆发粒子
    /// </summary>
    private void PlayLandParticle()
    {
        // 如果没有落地粒子系统，直接返回
        if (落地粒子系统 == null) return;

        // 双保险1：main 模块颜色
        _landMain.startColor = _currentGroundColor;

        // ★ 双保险2：同步 Color over Lifetime 模块颜色 ★
        // 如果落地粒子系统勾选了 Color over Lifetime(生命周期颜色)模块，
        // 它的渐变颜色会无视 startColor / EmitParams 直接覆盖粒子颜色，导致粒子变白。
        // 这里把渐变颜色同步成地面色，alpha 保留用户原来的淡出曲线（只换RGB不破坏淡出）。
        var colorOverLifetime = 落地粒子系统.colorOverLifetime;
        if (colorOverLifetime.enabled)
        {
            ParticleSystem.MinMaxGradient mmg = colorOverLifetime.color;
            GradientAlphaKey[] alphaKeys;

            // 尝试保留用户原来的 alpha 淡出曲线（只换颜色，不破坏淡出效果）
            if (mmg.gradient != null && mmg.gradient.alphaKeys != null && mmg.gradient.alphaKeys.Length > 0)
            {
                alphaKeys = mmg.gradient.alphaKeys;
            }
            else
            {
                // 原来没有渐变alpha，用默认淡出（从1到0）
                alphaKeys = new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                };
            }

            // 用地面颜色构造新渐变（颜色全用地面色，alpha用原来的曲线）
            Gradient newGradient = new Gradient();
            newGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(_currentGroundColor, 0f),
                    new GradientColorKey(_currentGroundColor, 1f)
                },
                alphaKeys
            );
            colorOverLifetime.color = newGradient;
        }

        // 双保险3：EmitParams 按粒子指定颜色（最可靠）
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
        emitParams.startColor = _currentGroundColor;
        落地粒子系统.Emit(emitParams, 落地粒子数量);
    }

    // ===== 碰撞检测（用于检测地面类型和接触方向）=====
    /// <summary>
    /// 当碰撞开始时触发（用于更新地面颜色和接触方向）
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 检查碰撞对象是否是地面
        if (collision.gameObject.CompareTag("Ground"))
        {
            // 使用 GetContacts 填充缓冲区（比 collision.contacts 更高效，不产生 GC）
            int count = collision.GetContacts(_contactBuffer);

            // 更新地面接触方向
            UpdateGroundNormal(count);

            // 更新地面颜色（从地面物体获取，传入接触点用于精确采样瓦片颜色）
            if (count > 0)
            {
                UpdateGroundColor(collision.gameObject, _contactBuffer[0].point);
            }
        }
    }

    /// <summary>
    /// 当碰撞持续时触发（用于持续检测地面类型和接触方向）
    /// </summary>
    private void OnCollisionStay2D(Collision2D collision)
    {
        // 检查碰撞对象是否是地面
        if (collision.gameObject.CompareTag("Ground"))
        {
            // 使用 GetContacts 填充缓冲区（比 collision.contacts 更高效，不产生 GC）
            int count = collision.GetContacts(_contactBuffer);

            // 更新地面接触方向
            UpdateGroundNormal(count);

            // 更新地面颜色（从地面物体获取，传入接触点用于精确采样瓦片颜色）
            if (count > 0)
            {
                UpdateGroundColor(collision.gameObject, _contactBuffer[0].point);
            }
        }
    }

    /// <summary>
    /// 当碰撞结束时触发（重置瓦片缓存，确保下次接触时重新采样颜色）
    /// </summary>
    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            // 重置瓦片坐标缓存，确保下次踩到地面时重新采样颜色
            _lastTileCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        }
    }

    // ===== 触发器检测（提前采样地面颜色）=====
    /// <summary>
    /// 手动检测触发器是否与地面重叠
    /// ★ 关键作用：触发器比碰撞体先接触到地面，可以提前采样颜色 ★
    /// 这样玩家落地（OnCollisionEnter2D）之前，颜色就已经准备好了
    /// 
    /// 注意：因为触发器在子物体上，不能用 OnTriggerStay2D（收不到回调），
    /// 所以改为每帧手动用 OverlapCollider 检测触发器范围内的地面。
    /// </summary>
    private void CheckTriggerGround()
    {
        if (地面检测触发器 == null) return;

        // 检测触发器范围内重叠的所有碰撞体
        int count = 地面检测触发器.OverlapCollider(_groundFilter, _overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            if (_overlapBuffer[i] != null && _overlapBuffer[i].CompareTag("Ground"))
            {
                // 找到地面碰撞体上离玩家最近的点，作为颜色采样点
                Vector2 samplePoint = _overlapBuffer[i].ClosestPoint(transform.position);
                UpdateGroundColor(_overlapBuffer[i].gameObject, samplePoint);
                return;
            }
        }
    }

    /// <summary>
    /// 根据碰撞信息更新地面法线方向
    /// </summary>
    /// <param name="contactCount">接触点数量</param>
    private void UpdateGroundNormal(int contactCount)
    {
        // 获取第一个接触点的法线
        if (contactCount > 0)
        {
            // 法线方向是从地面指向玩家的方向
            _groundNormal = _contactBuffer[0].normal;

            // 确保法线是单位向量
            _groundNormal.Normalize();
        }
    }

    /// <summary>
    /// 将当前地面颜色同步到两个粒子系统的 Main 模块
    /// （作为后备保障：如果以后开启粒子自动发射，颜色也能正确；
    ///   手动 Emit 时已通过 EmitParams 直接指定颜色，不依赖此处）
    /// </summary>
    private void ApplyColorToParticleSystems()
    {
        if (脚步粒子系统 != null)
        {
            _footstepMain.startColor = _currentGroundColor;
        }
        if (落地粒子系统 != null)
        {
            _landMain.startColor = _currentGroundColor;
        }
    }

    /// <summary>
    /// 从地面物体获取颜色（支持Tilemap和普通SpriteRenderer）
    ///
    /// ★ 关于瓦片地图颜色冲突的解决方案 ★
    /// 旧方案直接读取 tilemap.color，这会受瓦片地图整体着色影响，
    /// 用户若手动改了 tilemap.color 来"吸取"颜色，会导致整张地图被染色。
    ///
    /// 新方案：完全不读取 tilemap.color，而是直接采样瓦片贴图上
    /// 玩家接触点位置的像素颜色。这样：
    /// 1. 瓦片地图的 color 保持白色（默认），不会覆盖原始图片颜色
    /// 2. 粒子颜色直接来自贴图像素，更精确
    ///
    /// ★ 关于 RuleTile（规则瓦片）兼容性 ★
    /// 使用 tilemap.GetSprite() 获取瓦片精灵，兼容所有瓦片类型：
    /// 普通 Tile、RuleTile、AnimatedTile 等都能正确获取精灵并采样颜色。
    /// </summary>
    /// <param name="groundObject">地面游戏对象</param>
    /// <param name="contactPoint">碰撞接触点（世界坐标）</param>
    private void UpdateGroundColor(GameObject groundObject, Vector2 contactPoint)
    {
        // 方法1：尝试从Tilemap获取颜色（通过采样瓦片贴图像素，不修改瓦片地图颜色）
        Tilemap tilemap = groundObject.GetComponent<Tilemap>();
        if (tilemap != null)
        {
            // 将接触点转换为瓦片单元格坐标
            Vector3Int tileCell = tilemap.WorldToCell(contactPoint);

            // ★ 缓存优化：如果瓦片坐标没变，说明还站在同一块瓦片上，不需要重新采样
            if (tileCell == _lastTileCell)
            {
                return;
            }
            _lastTileCell = tileCell;

            // ★ 使用 GetSprite 获取瓦片精灵，兼容所有瓦片类型（包括 RuleTile）
            // 旧的 tileBase is Tile 检查对 RuleTile 不生效，会导致染色失败
            Sprite sprite = tilemap.GetSprite(tileCell);

            if (sprite != null)
            {
                // 获取瓦片的Sprite纹理（用 as 安全转换，防止类型不匹配）
                Texture2D texture = sprite.texture as Texture2D;

                if (texture != null && texture.isReadable)
                {
                    // ★ 用 try-catch 兜底：某些纹理格式即使 isReadable=true 也可能抛异常
                    try
                    {
                        // ===== 计算接触点在瓦片贴图上的UV坐标 =====
                        // 1. 获取瓦片单元格中心的世界坐标
                        Vector3 cellCenterWorld = tilemap.GetCellCenterWorld(tileCell);
                        // 2. 计算接触点相对于瓦片中心的偏移量
                        Vector2 localOffset = contactPoint - (Vector2)cellCenterWorld;
                        // 3. 将偏移量归一化到 0~1 范围（瓦片单元格尺寸为 tilemap.cellSize）
                        Vector2 cellSize = tilemap.cellSize;
                        float u = Mathf.Clamp01((localOffset.x / cellSize.x) + 0.5f);
                        float v = Mathf.Clamp01((localOffset.y / cellSize.y) + 0.5f);

                        // 4. 如果Sprite是图集的一部分，需要将瓦片内UV映射到完整纹理的UV
                        Rect texRect = sprite.textureRect;
                        float finalU = (texRect.x + u * texRect.width) / texture.width;
                        float finalV = (texRect.y + v * texRect.height) / texture.height;

                        // 5. 在接触点位置采样颜色（双线性插值，更平滑）
                        Color sampledColor = texture.GetPixelBilinear(finalU, finalV);

                        // 6. 如果接触点是透明的（比如瓦片边缘透明区域），回退到瓦片中心采样
                        if (sampledColor.a < 0.5f)
                        {
                            float centerU = (texRect.x + 0.5f * texRect.width) / texture.width;
                            float centerV = (texRect.y + 0.5f * texRect.height) / texture.height;
                            sampledColor = texture.GetPixelBilinear(centerU, centerV);
                        }

                        // 7. 确保颜色不透明（粒子颜色不应该透明）
                        sampledColor.a = 1.0f;

                        _currentGroundColor = sampledColor;
                    }
                    catch (System.Exception)
                    {
                        // 采样失败（纹理格式不支持等），使用默认颜色，不抛异常避免刷屏
                        _currentGroundColor = 默认地面颜色;
                    }
                }
                else
                {
                    // 纹理不可读，使用默认颜色（只警告一次，避免刷屏）
                    _currentGroundColor = 默认地面颜色;
                }
            }
            else
            {
                // 瓦片没有Sprite，使用默认颜色
                _currentGroundColor = 默认地面颜色;
            }

            // 更新粒子颜色
            ApplyColorToParticleSystems();
            return;
        }

        // 方法2：尝试从SpriteRenderer获取颜色（普通精灵地面）
        SpriteRenderer spriteRenderer = groundObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            _currentGroundColor = spriteRenderer.color;
            ApplyColorToParticleSystems();
            return;
        }

        // 方法3：尝试从Renderer/Material获取颜色
        Renderer renderer = groundObject.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            _currentGroundColor = renderer.material.color;
            ApplyColorToParticleSystems();
            return;
        }

        // 如果都获取不到，保持默认颜色
        _currentGroundColor = 默认地面颜色;
        ApplyColorToParticleSystems();
    }

    // ===== Gizmos绘制（辅助调试）=====
    private void OnDrawGizmosSelected()
    {
        // 绘制地面法线方向（方便调试）
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + new Vector3(_groundNormal.x, _groundNormal.y, 0f) * 0.5f);

        // 绘制粒子发射位置（方便调试）
        if (脚步粒子系统 != null)
        {
            Gizmos.color = _currentGroundColor;
            Gizmos.DrawWireSphere(_particlePosition, 0.15f);
        }

        // 绘制跳跃高度范围（方便调试）
        if (!IsGrounded())
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(transform.position.x - 0.5f, _jumpStartY, 0),
                           new Vector3(transform.position.x + 0.5f, _jumpStartY, 0));
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(transform.position.x - 0.5f, _jumpMaxHeight, 0),
                           new Vector3(transform.position.x + 0.5f, _jumpMaxHeight, 0));
        }
    }
}
