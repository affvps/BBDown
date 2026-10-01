using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BBDown.Core;
using BBDown.Core.Util;
namespace BBDown;

/// <summary>
/// 任务完成回调（H1 拆解：webhook 投递 + SSRF 防护）。回调地址只来自服务端启动配置，
/// 客户端请求体中的回调字段在 <c>BBDownApiServer.Security.cs</c> 的 SanitizeUntrustedOptions 被清零。
/// </summary>
public partial class BBDownApiServer
{
    /// <summary>
    /// 服务端固定的任务完成回调地址（serve 启动时经 --notify-webhook 配置）。
    /// 只接受管理员在启动参数里显式配置的地址；客户端请求体中的回调字段一律忽略，
    /// 防止任意客户端让本机服务器向攻击者指定的地址 POST 任务数据（SSRF 横向面）。
    /// </summary>
    private readonly string? _notifyWebhook;

    /// <summary>
    /// 回调地址 SSRF 防护：仅允许 http/https 绝对地址，
    /// 且禁止指向回环（127.0.0.1/::1/localhost）、链路本地（169.254.x / fe80::）与
    /// 云元数据面（169.254.0.0/16）。RFC1918 私网段仅在回调 URL 是**字面 IP** 时放行
    /// （操作者直接配置的局域网回调是 serve 正常用法）；经域名解析出的内网地址一律拒绝，
    /// 防止攻击者用解析到内网的域名（DNS 重绑定）把回调打向内网。
    /// dnsResolver 供测试注入（生产用系统 DNS）。
    /// 注意：域名在"配置时校验"与"回调时刻连接"之间存在 DNS 重绑定窗口（短 TTL 域名
    /// 可先解析为公网通过校验、回调时改指 169.254.169.254/内网）。因此每次回调前
    /// （NotifyCompletionCallbackAsync）都会再次调用本方法复查，并在 SendCallbackAsync 中
    /// 用 ConnectCallback 把连接绑定到本次校验解析出的 IP，把窗口压缩到连接前瞬间。
    /// 异步解析（GetHostAddressesAsync）：回调路径在 async 流程中，同步 DNS 会阻塞线程池线程。
    /// </summary>
    internal static async Task<bool> IsSafeCallbackUrlAsync(string? url, Func<string, Task<IPAddress[]>>? dnsResolver = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return true; // 未配置回调视为合法
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        bool hostIsLiteralIp = IPAddress.TryParse(uri.DnsSafeHost, out var literalIp) && literalIp is not null;

        if (hostIsLiteralIp)
        {
            // IPv4-mapped IPv6（如 [::ffff:169.254.169.254]）会把下方的 169.254 检查绕过
            // （其 AddressFamily 是 InterNetworkV6），统一映射回 IPv4 后再做检查。
            if (literalIp!.IsIPv4MappedToIPv6) literalIp = literalIp.MapToIPv4();
            // 字面 IP 是操作者显式配置的地址：仅拦回环/链路本地/云元数据。
            // RFC1918 内网字面 IP 放行（局域网回调是 serve 正常用法），
            // 因为字面 IP 不涉及 DNS 重绑定，攻击者无法借它打内网——攻击者构造的
            // "域名回调"永远走下方 DNS 解析分支（RFC1918 已拒绝）。若需进一步收紧，
            // 局域网回调用户应配置 --serve-token 或前置反向代理。
            if (IPAddress.IsLoopback(literalIp)) return false;
            if (literalIp.IsIPv6LinkLocal) return false;
            if (literalIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = literalIp.GetAddressBytes();
                if (b.Length == 4 && b[0] == 169 && b[1] == 254) return false; // 169.254.0.0/16 云元数据面
                if (b.All(x => x == 0)) return false; // 0.0.0.0：连接时绑定到回环
            }
            else if (literalIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                var b6 = literalIp.GetAddressBytes();
                if (b6.Length == 16 && b6.All(x => x == 0)) return false; // [::]
            }
            return true;
        }

        // DnsSafeHost：IPv6 字面量不带方括号（uri.Host 对 [::1] 会带方括号，IPAddress.TryParse
        // 必然失败 → 落到下方 DNS 分支被当成主机名解析，字面 IP 检查从未真正执行
        //（测试假阳性）；且 Dns.GetHostAddressesAsync("[::1]") 解析失败会把管理员配置的
        // 公网 IPv6 webhook 永久判为不合法，与 SendCallbackAsync（已用 DnsSafeHost）矛盾。
        var host = uri.DnsSafeHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        // 域名回调的 DNS 重绑定缺口：攻击者注册解析到 169.254.169.254 / 内网地址的域名
        // （如 metadata.google.internal），仅比对字符串会放行，任务完成时 HttpClient 才解析 DNS。
        // 这里提前解析并校验全部地址；域名解析出的任一地址命中回环/链路本地/169.254/RFC1918/ULA
        // 即拒绝——内网地址只允许"字面 IP"形式的显式配置，域名一律要求公网可达。
        try
        {
            var addresses = dnsResolver is not null ? await dnsResolver(host) : await Dns.GetHostAddressesAsync(host);
            // RF-55：部分 DNS 应答形态可返回零地址——空数组零次迭代会"校验空过"放行，
            // 而连接侧（SendCallbackAsync）对空数组记 Warn 跳过，两侧语义必须一致：
            // 解析不出任何地址的回调必然失败，按不安全处理。
            if (addresses.Length == 0) return false;
            foreach (var addr in addresses)
            {
                var resolvedIp = addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr;
                if (IsBlockedAddress(resolvedIp)) return false;
            }
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // 域名无法解析：回调必然失败，按不安全处理
            return false;
        }
    }

    /// <summary>
    /// 判定地址是否属于应拒绝的敏感/内网段：回环、链路本地、云元数据（169.254/16）、
    /// RFC1918 私网段（10/8、172.16/12、192.168/16）、CGNAT（100.64/10）与 IPv6 ULA（fc00::/7）。
    /// </summary>
    private static bool IsBlockedAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            // 169.254.0.0/16 云元数据面
            if (b[0] == 169 && b[1] == 254) return true;
            // RFC1918：10.0.0.0/8、172.16.0.0/12、192.168.0.0/16
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            // CGNAT 100.64.0.0/10
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            return false;
        }
        // IPv6 ULA fc00::/7
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 16 && (b[0] & 0xfe) == 0xfc;
        }
        return false;
    }

    /// <summary>
    /// 按服务端启动配置（--notify-webhook）发送任务完成回调。客户端请求体中的回调
    /// 字段已被 SanitizeUntrustedOptions 清零，这里只使用管理员显式配置的固定地址。
    /// </summary>
    private async Task NotifyCompletionCallbackAsync(DownloadTask task, string? notifyWebhook)
    {
        if (string.IsNullOrEmpty(notifyWebhook)) return;
        // 回调连接前复查 SSRF：--notify-webhook 是管理员显式配置的固定地址（服务端
        // allowlist），但仍需拦截回环/链路本地/云元数据等敏感目标。DNS 重绑定风险说明：
        // 域名回调在"启动时校验"与"回调连接时刻"之间可能存在解析结果变化（短 TTL 域名），
        // 因此每次回调前都重新校验；并在 SendCallbackAsync 中用 ConnectCallback 把连接
        // 绑定到本次校验解析出的 IP，避免 HttpClient 再次做 DNS 解析（消除重绑定窗口）。
        if (!await IsSafeCallbackUrlAsync(notifyWebhook))
        {
            Logger.LogWarn($"回调地址不合法，已跳过本次回调: {notifyWebhook}");
            return;
        }
        // 序列化共享对象前用 Snapshot() 深拷贝：与 /get-tasks 查询端点一致，
        // 避免未来任何并发写者在序列化期间修改 SavePaths 引发竞态。
        string jsonContent = JsonSerializer.Serialize(task.Snapshot(), AppJsonSerializerContext.Default.DownloadTask);
        await SendCallbackAsync(notifyWebhook, jsonContent);
    }

    /// <summary>
    /// 向固定回调地址 POST 任务快照。用 SocketsHttpHandler.ConnectCallback 把 TCP 连接
    /// 绑定到 IsSafeCallbackUrlAsync 本次校验解析出的 IP：HTTP 请求的 Host（SNI）仍是原域名，
    /// 但实际连到的地址来自已校验的解析结果，杜绝"校验用一套 DNS、连接用另一套 DNS"
    /// 的 TOCTOU 重绑定窗口。每个回调请求独立创建 handler（连接不复用），代价最小。
    /// 校验语义与 <see cref="IsSafeCallbackUrlAsync"/> 保持一致：字面 IP（管理员配置的局域网
    /// 回调）仅拦回环/链路本地/云元数据，域名解析出的内网地址一律拒绝。
    /// </summary>
    private static async Task SendCallbackAsync(string webhook, string jsonContent)
    {
        try
        {
            var uri = new Uri(webhook);
            // DnsSafeHost：IPv6 字面量不带方括号（uri.Host 对 [::1] 会带方括号，无法解析）
            var host = uri.DnsSafeHost;
            bool hostIsLiteralIp = IPAddress.TryParse(host, out var literalIp) && literalIp is not null;
            IPAddress target;
            if (hostIsLiteralIp)
            {
                // 字面 IP 是管理员显式配置的地址：直接绑定到该 IP（与 IsSafeCallbackUrlAsync 的
                // 字面 IP 分支一致，RFC1918 局域网回调放行）。此处不再解析 DNS。
                literalIp = literalIp!.IsIPv4MappedToIPv6 ? literalIp.MapToIPv4() : literalIp;
                if (IsUnsafeLiteralIpAddress(literalIp))
                {
                    Logger.LogWarn($"回调地址是敏感字面 IP，已跳过本次回调: {webhook}");
                    return;
                }
                target = literalIp;
            }
            else
            {
                // 域名回调：解析一次并校验全部地址；任一地址命中敏感/内网段即跳过。
                // 与 IsSafeCallbackUrlAsync 使用同一套 DNS 解析逻辑（域名重绑定校验在此完成）。
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host);
                }
                catch (SocketException)
                {
                    Logger.LogWarn($"回调地址 DNS 解析失败，已跳过本次回调: {webhook}");
                    return;
                }
                // RF-55：部分 DNS 应答形态可返回零地址——此时 addresses[0] 会抛
                // IndexOutOfRangeException（不在回调过滤器白名单），把已成功且已持久化的
                // 任务打成误导性的"任务异常终止"。与校验侧（IsSafeCallbackUrlAsync 对空数组
                // 返回 false）对齐：记 Warn 跳过本次回调。
                if (addresses.Length == 0)
                {
                    Logger.LogWarn($"回调地址解析结果为空，已跳过本次回调: {webhook}");
                    return;
                }
                foreach (var addr in addresses)
                {
                    var resolvedIp = addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr;
                    if (IsBlockedAddress(resolvedIp))
                    {
                        Logger.LogWarn($"回调地址解析到敏感地址，已跳过本次回调: {webhook}");
                        return;
                    }
                }
                // 解析结果可能含多个地址（已全部校验通过），取第一个连接
                target = addresses[0].IsIPv4MappedToIPv6 ? addresses[0].MapToIPv4() : addresses[0];
            }

            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(target, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
            // ConnectCallback 已绑定目标 IP；SNI 由 HttpClient 依据请求 URI 的原 host 设置，
            // 因此请求 URI 保留原 webhook 而不是替换成 IP
            using var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(webhook, content);
            if (!resp.IsSuccessStatusCode)
            {
                // 升 Warn：回调失败意味着通知静默丢失，此前仅 LogDebug（默认抑制）无痕。
                Logger.LogWarn($"回调返回 HTTP {(int)resp.StatusCode}: {webhook}");
            }
        }
        // RF-55：放宽为 catch (Exception)——本 catch 的目的只是"回调失败不影响任务"，
        // 无需类型白名单（此前 IndexOutOfRangeException 等未列类型会冒泡到
        // RunAcceptedTaskAsync 的 catch(Exception)，对已成功且已持久化的任务打印
        // 误导性的"任务异常终止"）。
        catch (Exception e)
        {
            // 升 Warn：回调失败意味着通知静默丢失（任务本身已成功），需可观测。
            Logger.LogWarn($"回调失败: {e.Message}");
        }
    }

    /// <summary>
    /// 字面 IP 回调的敏感地址判定：与 <see cref="IsSafeCallbackUrlAsync"/> 的字面 IP 分支一致，
    /// 仅拦回环、链路本地、云元数据面（169.254/16）与全零地址；RFC1918 私网放行
    /// （管理员显式配置的局域网回调是 serve 正常用法）。
    /// </summary>
    private static bool IsUnsafeLiteralIpAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 169 && b[1] == 254) return true; // 169.254.0.0/16 云元数据面
            if (b.All(x => x == 0)) return true;         // 0.0.0.0：连接时绑定到回环
            return false;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            return b6.Length == 16 && b6.All(x => x == 0); // [::]
        }
        return false;
    }
}
