using System.Text.Json.Serialization.Metadata;
using BBDown.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace BBDown;

/// <summary>
/// serve 模式 HTTP API 服务器。H1 拆解：按职责拆为 6 个 partial 文件（成员原样搬运，
/// 逐字保留实现），它们共享同一份实例状态（任务列表 / 锁 / 闸门）——抽成独立类型需要
/// 把状态外置或引入门面，属行为风险改动，不在本次范围。
/// <list type="bullet">
/// <item>本文件：生命周期与状态——构造、<see cref="SetupServer"/> 编排、监听前置校验、<see cref="RunAsync"/></item>
/// <item><c>BBDownApiServer.Security.cs</c>：认证 / 限速 / 回环与 Host 判定 / CSRF 与 Content-Type 闸 / 日志与请求体净化</item>
/// <item><c>BBDownApiServer.Routes.cs</c>：端点映射（查询 / 入队 / 取消 / 清理）</item>
/// <item><c>BBDownApiServer.Tasks.cs</c>：任务注册表、并发与接受闸门、入队与下载执行</item>
/// <item><c>BBDownApiServer.TaskFileStore.cs</c>：任务持久化（写入 / 裁剪 / 加载）</item>
/// <item><c>BBDownApiServer.Callback.cs</c>：完成回调投递与 SSRF 防护</item>
/// </list>
/// </summary>
public partial class BBDownApiServer
{
    /// <summary>关停时等待在途任务收尾的上限（超时后枚举残留 JobId 供运维定位）。</summary>
    private static readonly TimeSpan ServeShutdownDrainTimeout = TimeSpan.FromSeconds(30);
    private WebApplication? app;

    /// <summary>
    /// 服务器就绪信号：Kestrel 开始监听后触发。测试用它等待服务器真正可连，
    /// 避免 WebApplication 启动慢（如 CI 首次运行）时测试立即发请求撞上
    /// Connection refused 竞态。生产代码不依赖此信号，仅作同步点。
    /// </summary>
    internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 下载任务的生命周期 token。必须独立于 HTTP 请求：
    /// Minimal API 注入的 CancellationToken 是 HttpContext.RequestAborted，
    /// 它在响应写完后即失效，会让后台下载在客户端拿到 200 的瞬间被取消。
    /// 该 token 只在服务器关停时触发。
    /// </summary>
    private readonly CancellationTokenSource _serverLifetimeCts = new();

    public BBDownApiServer(int maxConcurrent = 3, string? serveToken = null, string? taskFilePath = null, string? notifyWebhook = null, bool trustProxy = false)
    {
        // 防御：maxConcurrent <= 0 会让 SemaphoreSlim 构造抛 ArgumentOutOfRangeException，
        // serve 作为长驻进程应以可读错误退出而非崩溃
        _concurrencyLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
        _queryLimiter = new SemaphoreSlim(MaxConcurrentQueryHandlers, MaxConcurrentQueryHandlers);
        // 接受队列：并发执行 + 排队等待，上限 = maxConcurrent + maxConcurrent*8（可排队等待的数量）。
        // 上限不随请求数增长，serve 长驻进程的任务/CTS 堆积被限制在一个固定数量级内。
        int acceptCap = Math.Max(1, maxConcurrent) * (1 + MaxQueuedPerConcurrent);
        _acceptLimiter = new SemaphoreSlim(acceptCap, acceptCap);
        _serveToken = serveToken;
        _notifyWebhook = notifyWebhook;
        _trustProxy = trustProxy;
        _taskFile = taskFilePath ?? Path.Combine(Environment.CurrentDirectory, "bbdown-tasks.json");
    }

    /// <summary>
    /// 构建 WebApplication、安装安全中间件并注册全部端点。拆分后本方法只做编排：
    /// 中间件与四组端点分别在 Security / Routes 两个 partial 文件中实现（H1）。
    /// </summary>
    public void SetupServer()
    {
        if (app is not null) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions((options) =>
        {
            options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerOptions.TypeInfoResolver, AppJsonSerializerContext.Default);
        });
        app = builder.Build();
        // 服务器关停时取消仍在进行的下载，避免进程挂在未完成的任务上
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (!_serverLifetimeCts.IsCancellationRequested) _serverLifetimeCts.Cancel();
        });
        // 服务器就绪信号：Kestrel 实际开始监听后触发，测试据此等待可连接
        app.Lifetime.ApplicationStarted.Register(() => Ready.TrySetResult());
        // 端点按职责分组注册（H1 拆解）：安全中间件 → 任务查询 → 入队 → 取消 → 清理
        UseServeSecurityMiddleware(app);
        MapTaskQueryRoutes(app);
        MapAddTaskRoute(app);
        MapCancelRoute(app);
        MapFinishedRemovalRoutes(app);
    }

    /// <summary>
    /// 监听地址前置校验（同步）：URL 合法性与"非回环必须带 token"安全边界。
    /// 独立成同步方法：RunAsync 是 async 方法，校验异常会进入返回的 Task 而非
    /// 同步抛出，测试无法用 Assert.Throws 语义直接断言；ServeCommand 的同步
    /// 预检失败路径也依赖同步异常。RunAsync 启动前仍调用本方法兜底。
    /// </summary>
    public void ValidateListenUrl(string url)
    {
        if (app is null) return;
        bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp;
        if (!result)
        {
            Logger.LogError($"{url} 不是合法的 http URL，url 示例：http://0.0.0.0:5000");
            Logger.LogWarn("如果您需要 https，请额外配置反向代理");
            // 抛异常而非仅设置 ExitCode：Environment.ExitCode 会被 Main 的返回值覆盖，
            // 导致监听地址无效时进程仍以 0 退出。ServeCommand 捕获后返回非零退出码。
            throw new ArgumentException($"{url} 不是合法的 http URL");
        }
        // 默认安全边界：非回环监听（0.0.0.0 / :: / 具体网卡 IP 等）会把任务提交/查询/取消
        // 端点暴露到局域网甚至公网，未配置 --serve-token 时任意来源都能提交下载任务并
        // 触碰本机磁盘。这里在启动前强制要求 token，拒绝以不安全配置启动。
        // 回环（127.0.0.1 / localhost / [::1] / ::1）仍是受信任的本地边界，保持向后兼容。
        if (!IsLoopbackListenAddress(uriResult!) && string.IsNullOrEmpty(_serveToken))
        {
            throw new InvalidOperationException(
                $"监听地址 {url} 不是回环地址（0.0.0.0 / :: / 具体网卡 IP 等），必须配置 --serve-token 才能启动，否则任意客户端都能提交任务并访问本机文件。");
        }
    }

    public async Task RunAsync(string url, CancellationToken cancellationToken = default)
    {
        ValidateListenUrl(url);
        if (app is null) return;
        app.Urls.Add(url);
        try
        {
            await LoadFinishedTasksAsync(cancellationToken);
            // RF-2：命令层已迁移 AsyncCommand，serve 全程 await——不再用
            // Task.Run + GetAwaiter().GetResult() 让一个线程池线程阻塞整个服务生命周期。
            await ((IHost)app).RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 收到取消信号（如 Ctrl+C），正常退出
        }
        // 关停收尾：Kestrel 已停止接收新请求。取消共享令牌已在 ApplicationStopping
        // 触发（见 SetupServer 的注册）。这里限时等待所有在途后台任务完成
        // "取消 → 终止外部进程 → 持久化"，避免退出进程时遗留孤儿 ffmpeg/aria2c
        // 或丢失未落盘的任务记录。等待超时（例如下载器在取消后仍卡在外部进程上）
        // 则放弃等待，不无限阻塞退出。
        // Task.WaitAll 是有界同步等待且仅发生在进程退出路径（≤30s），与 RF-2 消除的
        // "整个服务生命周期占线程阻塞"不同，保留同步形式以避免 WhenAll+WaitAsync
        // 的异常类型变化（TimeoutException/首个业务异常）改变退出语义。
        Task[] inflight;
        lock (_inFlightLock) { inflight = [.. _inFlightTasks]; }
        if (inflight.Length > 0)
        {
            Logger.LogWarn($"正在等待 {inflight.Length} 个在途任务取消并收尾...");
            try
            {
                if (!Task.WaitAll(inflight, ServeShutdownDrainTimeout))
                {
                    // 升 Error 并枚举具体 JobId：此前仅 Warn 无明细，孤儿 ffmpeg/aria2c 无法事后定位
                    string[] stuckJobIds;
                    lock (_inFlightLock) { stuckJobIds = [.. _inFlightJobIds]; }
                    Logger.LogError($"部分在途任务未在 30 秒内完成收尾，已强制退出（可能遗留孤儿外部进程）: {string.Join(", ", stuckJobIds)}");
                }
            }
            catch (AggregateException)
            {
                // 个别任务取消路径抛出的异常不影响整体退出
            }
        }
        // 最后一次持久化：确保取消前已完成的任务记录落盘。若启动时取消发生在历史
        // 文件恢复完成前，必须保留磁盘上的旧记录，不能用尚未加载的空列表覆盖。
        if (_finishedTasksLoaded) PersistFinishedTasks();
    }
}
