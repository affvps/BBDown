namespace BBDown.Core;

/// <summary>
/// 配置门面：<see cref="Current"/> 读当前异步流快照，<see cref="Apply"/> 写"流 + 全局"。
/// REVIEW_PLAN I11：原 13 个 SCREAMING_CASE/小写门面成员中 9 个
/// （COOKIE/TOKEN/DEBUG_LOG/HOST/EPHOST/TVHOST/AREA/SKIP_SSL_CHECK/qualitys）全库零引用，
/// 已按 H7/I17「死代码逐条删除」先例删除；存活成员统一 PascalCase。
/// 读写配置字段请直接用 <see cref="Current"/>，不要再新增门面成员。
/// </summary>
public static class Config
{
    private static AppSettings _settings = new();
    private static readonly object _lock = new();

    /// <summary>
    /// 每个异步流自己的配置快照。Apply 在当前 async 流内写入它，Current 优先读取它。
    /// serve 模式下每个 /add-task 的下载流各自 SetUpWork 覆盖全局 Config，
    /// 若只有全局 _settings，并发任务会互相读到对方刚写入的 Cookie/Token/Host，
    /// 表现为跨账号串号下载。AsyncLocal 随 ExecutionContext 在 async 流与派生子任务间
    /// 自动传递，把"全局单例"收敛为"每任务流单例"。
    /// </summary>
    private static readonly AsyncLocal<AppSettings?> _contextSettings = new();

    public static AppSettings Current
    {
        get
        {
            AppSettings? local = _contextSettings.Value;
            if (local is not null) return local;
            lock (_lock) { return _settings; }
        }
    }

    public static void Apply(AppSettings settings)
    {
        // 同时写上下文与全局：CLI 顶层（无 async 隔离需求）与静态初始化路径保持原行为，
        // serve 并发任务流内则通过上下文隔离
        _contextSettings.Value = settings;
        lock (_lock) { _settings = settings; }
    }

    /// <summary>
    /// 只更新当前异步流的配置快照，不触碰全局 <see cref="_settings"/>。
    /// 用于子方法在下载流程中产生的新配置（如 wbi 密钥、buvid3 cookie）：
    /// 这些更新应只对当前任务的流程生效——同时写全局会被并发 serve 任务的
    /// 后写者覆盖，造成跨任务污染（一个任务拿到的配置被另一个任务的尾部写入破坏）。
    /// 子方法仍返回新值由父流程经 <see cref="Apply"/> 传播到全局的场景，走 Apply。
    /// </summary>
    public static void ApplyToCurrentAsyncFlow(AppSettings settings)
        => _contextSettings.Value = settings;

    /// <summary>
    /// 写入服务器时钟偏移（秒），同时作用于当前异步流与全局。
    /// 双写理由与 Apply 相同（serve 任务流的 WbiSign 需要读校准值），但偏移是服务器
    /// UTC 时钟的物理属性而非账号凭据：与 Cookie/Wbi 不同，serve 并发任务间共享不会
    /// 造成串号等安全后果（偏移语义上应全任务一致）。流内未设时 Config.Current 回落
    /// 全局（见 <see cref="Current"/>），任何签名流都能读到最近校准值。
    /// 由 HTTPUtil.CalibrateClock 在每次响应头 Date 校准后调用。
    /// </summary>
    public static void SetClockOffset(long offsetSeconds)
    {
        _contextSettings.Value = Current with { ServerClockOffsetSeconds = offsetSeconds };
        lock (_lock) { _settings = _settings with { ServerClockOffsetSeconds = offsetSeconds }; }
    }

    /// <summary>
    /// 全局写入 Wbi（同时作用于当前异步流与全局）。仅 CLI 顶层与测试使用；
    /// serve 并发任务流内改用 <see cref="WbiFlow"/>，避免跨任务互相覆盖签名材料。
    /// </summary>
    public static string Wbi { get => Current.Wbi; set => Apply(Current with { Wbi = value }); }

    /// <summary>
    /// 只更新当前异步流的 Cookie（不写全局）。用于下载流程中注入 buvid3 等设备标识：
    /// serve 并发任务下，把注入结果写进全局会让后写者覆盖先写者的凭据，
    /// 造成跨账号串号。改动只对本任务流程生效，与 <see cref="ApplyToCurrentAsyncFlow"/> 同义。
    /// </summary>
    public static string CookieFlow { get => Current.Cookie; set => ApplyToCurrentAsyncFlow(Current with { Cookie = value }); }

    /// <summary>
    /// 只更新当前异步流的 Wbi（不写全局）。用于下载流程中提取的 wbi 密钥：
    /// 与 <see cref="ApplyToCurrentAsyncFlow"/> 同义，见其说明——serve 并发任务下
    /// 不应让一个任务的 wbi 覆盖全局后被其它任务读到。
    /// </summary>
    public static string WbiFlow { get => Current.Wbi; set => ApplyToCurrentAsyncFlow(Current with { Wbi = value }); }
}
