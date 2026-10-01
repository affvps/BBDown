using QRCoder;
using BBDown.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using BBDown.Core.Util;

namespace BBDown;

internal static class BBDownLoginUtil
{
    /// <summary>二维码 PNG 输出的像素缩放倍数（GetGraphic 参数；7 保证手机可扫）。</summary>
    private const int QrCodePngScale = 7;

    /// <summary>
    /// 扫码登录轮询状态码（B 站 passport 接口，I6）。WEB 端点的 <c>data.code</c> 是 JSON 数字；
    /// TV 端点按**字符串**语义解析（TV 轮询响应的 code 用 <c>GetStringSafe</c> 取会得空串，
    /// 见 LoginTV 内注释），因此同值常量在两套端点各有一份、类型不同、不可互换。
    /// </summary>
    private static class QrPollCode
    {
        /// <summary>WEB：二维码已过期。</summary>
        public const int WebExpired = 86038;
        /// <summary>WEB：等待用户扫码。</summary>
        public const int WebWaitScan = 86101;
        /// <summary>WEB：已扫码，等待用户在手机上确认。</summary>
        public const int WebWaitConfirm = 86090;
        /// <summary>TV：二维码已过期（与 <see cref="WebExpired"/> 同值，字符串形态）。</summary>
        public const string TvExpired = "86038";
        /// <summary>TV：等待用户扫码（TV 端点没有"等待确认"阶段）。</summary>
        public const string TvWaitScan = "86039";
    }

    /// <summary>
    /// 轮询扫码登录状态，并透出 poll 响应的 Set-Cookie 头。
    /// B 站新版登录（2026）将 SESSDATA 等凭证经 Set-Cookie 下发，必须保留响应头。
    /// </summary>
    public static async Task<(string Body, List<string> SetCookies)> GetLoginStatusAsync(string qrcodeKey, CancellationToken cancellationToken = default)
    {
        string queryUrl = $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}&source=main-fe-header";
        return await HTTPUtil.GetWebSourceWithSetCookiesAsync(queryUrl, token: cancellationToken);
    }

    /// <summary>Set-Cookie 头中应丢弃的 cookie 属性名（不是实际 cookie 字段）。</summary>
    private static readonly HashSet<string> CookieAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "Domain", "Expires", "Max-Age", "Secure", "HttpOnly", "SameSite", "Priority", "Partitioned", "Size",
    };

    /// <summary>
    /// 合并扫码登录凭证：url query（旧协议，SESSDATA 等直接放参数）+
    /// Set-Cookie 字段（新协议，HttpOnly 下发）。返回以 ; 连接的 cookie 串。
    /// Set-Cookie 条目取 name=value 部分，丢弃 Path/Domain/Expires 等属性。
    /// </summary>
    internal static string MergeLoginCookies(string urlQuery, IEnumerable<string> setCookies)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in urlQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) fields[pair[..eq]] = pair[(eq + 1)..];
        }
        foreach (var setCookie in setCookies)
        {
            var kv = setCookie.Split(';')[0].Trim();
            var eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            var name = kv[..eq].Trim();
            // Path=/、Expires=... 等是 cookie 属性而非凭证字段，混入会污染凭据文件
            if (CookieAttributeNames.Contains(name)) continue;
            fields[name] = kv[(eq + 1)..].Trim();
        }
        return string.Join(";", fields.Select(kv => $"{kv.Key}={kv.Value.Replace(",", "%2C")}"));
    }

    /// <summary>
    /// 从 ; 连接的 cookie 串中按名取第一个值（大小写不敏感），无则返回空串。
    /// </summary>
    internal static string GetCookieValue(string cookieString, string name)
    {
        var prefix = name + "=";
        return cookieString.Split(';')
            .FirstOrDefault(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?.Substring(prefix.Length) ?? "";
    }

    /// <summary>
    /// 生成二维码并在控制台打印，同时尽力写出 qrcode.png 供手机扫描。WEB/TV 两个登录流程
    /// 共用（I6，此前两处逐字复制"生成 → 落盘 → 失败降级 → 打印"四步）。
    /// 图片写入失败属良性降级：控制台二维码仍可扫，只记 Debug。
    /// </summary>
    private static async Task RenderQrCodeAsync(string url)
    {
        Logger.Log("生成二维码...");
        QRCodeGenerator qrGenerator = new();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        PngByteQRCode pngByteCode = new(qrCodeData);
        try
        {
            await File.WriteAllBytesAsync("qrcode.png", pngByteCode.GetGraphic(QrCodePngScale));
            Logger.Log("生成二维码成功: qrcode.png, 请打开并扫描, 或扫描打印的二维码");
        }
        catch (Exception ex) when (ExceptionPolicies.IsBestEffortFailure(ex))
        {
            Logger.LogDebug("无法写入本地二维码图片文件: {0}", ex.Message);
            Logger.Log("请扫描下方打印的控制台二维码");
        }
        var consoleQRCode = new ConsoleQRCode(qrCodeData);
        consoleQRCode.GetGraphic();
    }

    /// <summary>
    /// 以"仅属主可读写"的权限写出登录凭据文件（WEB 写 cookie 串 / TV 写 access_token）并收紧
    /// 权限。两个登录流程共用（I6）。创建文件时即以 owner 读写权限打开：Unix 上避免先以
    /// umask 默认权限落盘、再 chmod 收紧的两步窗口。
    /// </summary>
    private static async Task WriteOwnerOnlyFileAsync(string path, string content)
    {
        var opts = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows())
            opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var fs = new FileStream(path, opts))
        using (var writer = new StreamWriter(fs))
            await writer.WriteAsync(content);
        SetOwnerOnlyPermission(path);
    }

    public static async Task<bool> LoginWEB(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Log("获取登录地址...");
            cancellationToken.ThrowIfCancellationRequested();
            string loginUrl = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate?source=main-fe-header";
            using var loginDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl, token: cancellationToken));
            string url = loginDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
            string qrcodeKey = BBDownUtil.GetQueryString("qrcode_key", url);
            bool flag = false;
            await RenderQrCodeAsync(url);

            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var (w, setCookies) = await GetLoginStatusAsync(qrcodeKey, cancellationToken);
                using var pollDoc = JsonDocument.Parse(w);
                long topCode = pollDoc.RootElement.GetInt64Safe("code");
                if (topCode != 0)
                {
                    var msg = pollDoc.RootElement.GetValueAsStringSafe("message");
                    Logger.LogError($"登录请求失败: {msg} (code={topCode})");
                    return false;
                }
                var dataElem = pollDoc.RootElement.GetPropertySafe("data");
                if (dataElem.ValueKind == JsonValueKind.Undefined)
                {
                    Logger.LogError("登录轮询响应格式异常（缺少 data 节点）");
                    return false;
                }
                int code = dataElem.GetInt32Safe("code");
                if (code == QrPollCode.WebExpired)
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    return false;
                }
                else if (code == QrPollCode.WebWaitScan)
                {
                    continue;
                }
                else if (code == QrPollCode.WebWaitConfirm)
                {
                    if (!flag)
                    {
                        Logger.Log("扫码成功, 请确认...");
                        flag = !flag;
                    }
                }
                else if (code == 0)
                {
                    using var successDoc = JsonDocument.Parse(w);
                    string cc = successDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
                    // 导出cookie, 转义英文逗号 否则部分场景会出问题
                    // URL 不含 ? 时 IndexOf 返回 -1，会误把整条 URL 当 cookie 写入凭据文件
                    var queryIdx = cc.IndexOf('?');
                    var cookieQuery = queryIdx >= 0 ? cc[(queryIdx + 1)..] : "";
                    // B 站新版扫码登录：SESSDATA/bili_jct/DedeUserID 等凭证经 poll 响应头
                    // Set-Cookie（HttpOnly）下发，data.url 仅剩 crossDomain 跳转参数。
                    // 合并两类来源，保证新老协议都能写入完整登录态。
                    var merged = MergeLoginCookies(cookieQuery, setCookies);
                    var sessdata = GetCookieValue(merged, "SESSDATA");
                    Logger.Log("登录成功: SESSDATA=" + SensitiveDataMasker.MaskValue(sessdata));
                    if (merged == "" || sessdata == "")
                    {
                        // 防御性检查：url query 总是含 ticket/gourl 等跳转参数，merged 非空
                        // 不代表拿到了有效凭证。B 站新版登录的 SESSDATA 经 Set-Cookie 下发，
                        // 若被中间层剥离/风控拦截，此处应拒绝写入而不是落盘无效 cookie。
                        Logger.LogError("登录成功但未取得有效凭证（SESSDATA 缺失），登录结果未保存；请重试或检查网络环境");
                        return false;
                    }
                    var cookiePath = Path.Combine(Program.APP_DIR, "BBDown.data");
                    await WriteOwnerOnlyFileAsync(cookiePath, merged);
                    return true;
                }
                else
                {
                    string msg = dataElem.GetValueAsStringSafe("message");
                    Logger.LogError($"登录失败: {msg} (code={code})");
                    return false;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Logger.LogError($"WEB 登录失败: {e.Message}");
            Logger.LogError("请检查网络连接；若二维码已过期请重新执行 BBDown login。");
            return false;
        }
        catch (OperationCanceledException)
        {
            // 区分主动取消与 HttpClient 超时：超时抛的 TaskCanceledException 其 token 未取消，
            // 应报告网络超时而非"已取消"，避免掩盖真实失败原因（与 serve 侧
            // ClassifyCancellation 的判别逻辑一致）。
            if (cancellationToken.IsCancellationRequested)
                Logger.LogWarn("WEB 登录已取消。");
            else
                Logger.LogError("WEB 登录超时或被中断，请检查网络连接。");
            return false;
        }
        finally
        {
            CleanUpQrCodeFile();
        }
    }

    public static async Task<bool> LoginTV(CancellationToken cancellationToken = default)
    {
        try
        {
            string loginUrl = "https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code";
            string pollUrl = "https://passport.bilibili.com/x/passport-tv-login/qrcode/poll";
            var parameters = BBDownUtil.GetTVLoginParms();
            Logger.Log("获取登录地址...");
            cancellationToken.ThrowIfCancellationRequested();
            // 登录两个端点的 POST 体都携带按 appsecret 签名的参数（auth_code/sign/ts）：
            // 改用禁自动跳转客户端 + 3xx 显式拦截——签名体不随 307/308 重放到非预期主机
            //（RF-37，与 WEB 登录轮询 RF-13 / gRPC POST B3-F2 / Widevine 许可证 RF-4
            // 的凭据收口同构）。
            using var authResponse = await HTTPUtil.NoRedirectClient.PostAsync(loginUrl, new FormUrlEncodedContent(parameters.ToDictionary()), cancellationToken);
            if ((int)authResponse.StatusCode is >= 300 and <= 399)
                throw new InvalidOperationException($"TV 登录端点返回重定向({(int)authResponse.StatusCode})，已拒绝跟随");
            // RF-79：有界读取（TV 端点响应无上限时被攻破端点可打满内存）
            byte[] responseArray = await HTTPUtil.ReadContentBoundedAsync(authResponse.Content, cancellationToken);
            string web = Encoding.UTF8.GetString(responseArray);
            using var authDoc = JsonDocument.Parse(web);
            string url = authDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
            string authCode = authDoc.RootElement.GetPropertySafe("data").GetStringSafe("auth_code")!;
            await RenderQrCodeAsync(url);
            parameters.Set("auth_code", authCode);
            parameters.Set("ts", BiliApiKeys.GetTimeStamp(true));
            parameters.Remove("sign");
            parameters.Add("sign", BiliApiKeys.GetSign(BBDownUtil.ToQueryString(parameters), BiliApiKeys.TvSignSalt));
            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // TV 登录轮询的响应更是新下发 access_token 的通道：同款禁跳转 + 3xx
                // 显式拦截（RF-37）。顺带以 using 释放响应（原实现不释放响应对象）。
                using var pollResponse = await HTTPUtil.NoRedirectClient.PostAsync(pollUrl, new FormUrlEncodedContent(parameters.ToDictionary()), cancellationToken);
                if ((int)pollResponse.StatusCode is >= 300 and <= 399)
                    throw new InvalidOperationException($"TV 登录轮询遇到重定向({(int)pollResponse.StatusCode})，已拒绝跟随");
                responseArray = await HTTPUtil.ReadContentBoundedAsync(pollResponse.Content, cancellationToken);
                web = Encoding.UTF8.GetString(responseArray);
                using var pollDoc2 = JsonDocument.Parse(web);
                // 该轮询接口的 code 是 JSON 数字，而 GetStringSafe 只接受字符串类型、
                // 对数字一律返回空串，导致 code 恒为 "" 永远匹配不到 86038/86039，
                // 每次轮询都误入成功分支。GetValueAsStringSafe 用 ToString() 兼容数字与字符串。
                string code = pollDoc2.RootElement.GetValueAsStringSafe("code");
                if (code == QrPollCode.TvExpired)
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    return false;
                }
                else if (code == QrPollCode.TvWaitScan)
                {
                    continue;
                }
                else if (code == "0")
                {
                    using var successDoc2 = JsonDocument.Parse(web);
                    string cc = successDoc2.RootElement.GetPropertySafe("data").GetStringSafe("access_token") ?? "";
                    if (string.IsNullOrEmpty(cc))
                    {
                        Logger.LogError("TV 登录成功但未取得有效凭证（access_token 缺失），登录结果未保存；请重试或检查网络环境");
                        return false;
                    }
                    Logger.Log("登录成功: AccessToken=" + SensitiveDataMasker.MaskValue(cc));
                    //导出cookie
                    var tvTokenPath = Path.Combine(Program.APP_DIR, "BBDownTV.data");
                    await WriteOwnerOnlyFileAsync(tvTokenPath, "access_token=" + cc);
                    return true;
                }
                else
                {
                    string msg = pollDoc2.RootElement.GetValueAsStringSafe("message");
                    Logger.LogError($"TV 登录失败: {msg} (code={code})");
                    return false;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Logger.LogError($"TV 登录失败: {e.Message}");
            Logger.LogError("请检查网络连接；若二维码已过期请重新执行 BBDown logintv。");
            return false;
        }
        catch (OperationCanceledException)
        {
            // 与 WEB 登录一致：token 未取消的 TaskCanceledException 是 HttpClient 超时，
            // 报告网络失败而非"已取消"
            if (cancellationToken.IsCancellationRequested)
                Logger.LogWarn("TV 登录已取消。");
            else
                Logger.LogError("TV 登录超时或被中断，请检查网络连接。");
            return false;
        }
        finally
        {
            CleanUpQrCodeFile();
        }
    }

    /// <summary>
    /// 删除临时二维码文件。二维码过期、扫码中断或请求异常时同样需要清理，
    /// 否则含登录地址的图片会一直留在工作目录。
    /// </summary>
    private static void CleanUpQrCodeFile()
    {
        try { File.Delete("qrcode.png"); }
        catch (IOException) { /* 文件可能正被看图程序占用 */ }
        catch (UnauthorizedAccessException) { /* 权限不足，留待用户手动清理 */ }
    }

    private static void SetOwnerOnlyPermission(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix/macOS: chmod 600 (owner read/write only)
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
