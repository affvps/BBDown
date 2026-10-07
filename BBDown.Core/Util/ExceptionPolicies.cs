namespace BBDown.Core.Util;

/// <summary>
/// 异常过滤策略（I7）：把散落在各 <c>catch</c> 站点上的
/// <c>when (ex is A or B or …)</c> 类型集合收敛为**具名策略**。
///
/// 背景：全库 <c>catch (Exception ex) when (…)</c> 的重复族收口（I7）。
/// **计数口径（2026-10-01 统一）**：全仓 <c>catch (Exception …) when (…)</c> 正则统计
/// （排除 bin/obj），"具名" = 谓词含 <c>ExceptionPolicies.</c>，其余为内联集合——
/// 收口后实测 **104 处（具名 67 / 内联 37）**；文档里此前的 94 / 102 / 66 三组数字
/// 口径互不一致且未标注方法，已作废。
/// 重复族的成因是同一策略被逐字复制到多个站点，且历史上因手工同步而漂移
/// （如 `SubCommand` 与下载页过滤器各自维护一份）。
///
/// 三条纪律（本类**不是**通用异常工具，禁止在此放与"具体站点策略"无关的判断）：
/// <list type="number">
/// <item>每个谓词的集合与迁移前**逐字一致**——本批只做等价收口，不改变任何站点的捕获面；</item>
/// <item>站点自有的附加条件（`cancellationToken.IsCancellationRequested` 守卫、
/// <c>ex is not …</c>、`&&` 组合）保留在站点上，**不并入**谓词；</item>
/// <item>集合等价性由 <c>ExceptionPolicyTests</c> 的真值表逐类型钉住——增删类型会立即失败。</item>
/// </list>
///
/// 例外：集合唯一（仅一处使用）的站点**不收口**——2~4 个类型的条件本身就是最优表达，
/// 收口反而把策略藏进间接层（实测 28 种唯一集合保持原样）。
///
/// **同样不收口**：过滤器级带附加条件的站点（如 `Download.cs` 页面重试的
/// `ex is TaskCanceledException && !cancellationToken.IsCancellationRequested`）——
/// 把它的类型集合抽成无条件谓词会吞掉用户取消；此类站点保留原条件（本批共 2 处）。
/// </summary>
public static class ExceptionPolicies
{
    /// <summary>
    /// best-effort 操作的失败：清理临时文件/分片、写日志文件、尽力而为的收尾动作。
    /// 失败不影响主流程，站点通常空捕获或降级为 Debug 日志。**（44 处，最大同型族）**
    /// </summary>
    public static bool IsBestEffortFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException;

    /// <summary>
    /// 本地 JSON 载体的 IO / 解析失败：续传清单、订阅清单/历史等自有文件的读写。
    /// 站点各自决定处置（订阅存储隔离为 .corrupt 并抛专用异常；下载工具降级返回失败）。
    /// </summary>
    public static bool IsJsonOrIoFailure(Exception ex)
        => ex is IOException or System.Text.Json.JsonException;

    /// <summary>
    /// 字幕接口抓取失败：返回 null 降级为"无字幕"，不阻断下载。（SubUtil 三处 API 变体）
    /// </summary>
    public static bool IsSubtitleFetchFailure(Exception ex)
        => ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException or TimeoutException;

    /// <summary>
    /// 传输层失败：请求/分片下载的资源获取失败（含 HttpClient 超时抛出的 TaskCanceledException）。
    /// <b>注意</b>：用户取消（token 已取消）的判定由站点自行守卫，不在此谓词内。
    /// </summary>
    public static bool IsTransportFailure(Exception ex)
        => ex is HttpRequestException or IOException or TaskCanceledException;

    /// <summary>
    /// 响应缺少预期节点：wbi 密钥提取、DRM license 信息读取等"结构不符"的容错降级。
    /// </summary>
    public static bool IsMissingResponseNodeFailure(Exception ex)
        => ex is KeyNotFoundException or InvalidOperationException;

    /// <summary>
    /// serve 任务记录文件（任务表持久化）的落盘/加载失败：磁盘、权限、JSON 损坏。
    /// </summary>
    public static bool IsTaskStoreFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException;

    /// <summary>
    /// 解析降级路径的失败：免二压重取、最高清晰度重发失败时沿用首轮结果（不阻断解析）。
    /// </summary>
    public static bool IsParseDowngradeFailure(Exception ex)
        => ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException
            or TimeoutException or TaskCanceledException;

    /// <summary>
    /// 辅助探测请求失败：buvid3 获取、登录状态检测等"拿不到就降级"的旁路请求。
    /// </summary>
    public static bool IsProbeRequestFailure(Exception ex)
        => ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException
            or InvalidOperationException or TimeoutException;

    /// <summary>
    /// 单条目级故障的**类型核心集**（不含取消类）：传输失败、响应解析与结构缺失、
    /// 本地文件与权限、服务端可控数据畸形（Format/Overflow/InvalidData/Aggregate）。
    /// 下面两个派生谓词共用它，因此"可跳过"与"可重试"的捕获面不会再各自漂移。
    /// </summary>
    private static bool IsItemFailureCore(Exception ex)
        => ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException
            or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException
            or TimeoutException or InvalidDataException or AggregateException or FormatException or OverflowException;

    /// <summary>
    /// 单条目（单 aid / 单视频 / 单 P）失败可跳过：失败计入失败数并继续下一个。
    /// 站点：<c>sub check</c> 逐 aid 与逐订阅、<c>watchlater</c> 逐视频、下载编排逐 P、
    /// 页面附加资源（评论）的 best-effort 抓取。
    /// 含 <see cref="TaskCanceledException"/>（HttpClient 超时的抛型）：使用本谓词的站点
    /// **必须**已有用户取消守卫（<c>catch (OperationCanceledException) when (ct.IsCancellationRequested) throw;</c>
    /// 或体内 <c>if (ct.IsCancellationRequested) throw;</c>），否则会把主动取消吞成"条目失败"。
    /// </summary>
    public static bool IsSkippableItemFailure(Exception ex)
        => IsItemFailureCore(ex) || ex is TaskCanceledException;

    /// <summary>
    /// 页面级重试的可重试集：与核心集一致，但**不含** <see cref="TaskCanceledException"/>——
    /// 该站点把"超时（token 未取消 → 重试）"与"用户取消（立即上抛）"分开表达，
    /// 谓词里带上取消类型会吞掉取消，取消条件由站点自行附加。
    /// </summary>
    public static bool IsRetryablePageFailure(Exception ex) => IsItemFailureCore(ex);
}
