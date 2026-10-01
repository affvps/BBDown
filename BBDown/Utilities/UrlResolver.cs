using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BBDown.Core.Util;

namespace BBDown;

public static partial class UrlResolver
{
    /// <summary>
    /// 解析用户输入（URL、BV号、AV号、EP号等）为统一格式的 avid 标识符。
    /// http(s) 输入与裸 ID/前缀目标分派给两个独立方法（I12），两条路径只共用返回前的
    /// 日志净化（RF-54）与 av 跳转检查（<see cref="FixAvidAsync"/>）。
    /// </summary>
    public static async Task<string> ResolveAsync(string input, CancellationToken token = default)
    {
        // 大小写不敏感：用户可能输入 HTTPS:// 或 Http://
        var target = input.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? await ResolveHttpUrlAsync(input, token)
            : await ResolveBareIdAsync(input, token);

        // RF-54：净化下沉到来源——target 的派生串（aidOri/fid/sid 等 query 值经 GetQueryString
        // 的 [^&]+ 可匹配 CR/LF）以原始控制字符形态落日志（Workflow/Pages/Options/serve 的
        // Logger 层无转义），serve 客户端或 CLI 粘贴恶意链接可伪造日志行/ANSI 序列。
        // 在返回前统一单行化，比逐 sink 补丁收敛（SanitizeLogString 当前零单测，随本批补）。
        target = BBDownApiServer.SanitizeLogString(target);
        return await FixAvidAsync(target, token);
    }

    /// <summary>
    /// 解析 http(s) 输入：b23.tv 短链、视频 av/bv、番剧/课程 ep/ss、合集与系列列表、收藏夹、
    /// 空间主页、国际版 ep；未识别的 B 站官方域名按泛抓取解析页面。
    /// 返回值可能是纯 avid，也可能是 <c>mid:</c>/<c>favId:</c>/<c>listBizId:</c>/<c>seriesBizId:</c>
    /// /<c>cheese:</c>/<c>ep:</c> 前缀目标，故累加变量名为 target（I12 注明）。
    /// internal：从 <see cref="ResolveAsync"/> 拆出（I12），供单测直测不触发 av 跳转检查的分支。
    /// </summary>
    internal static async Task<string> ResolveHttpUrlAsync(string input, CancellationToken token = default)
    {
        var target = input;
        var lowerInput = input.ToLowerInvariant();
        // b23.tv 短链：仅接受 host 精确等于 b23.tv 的输入。用 Contains 会让
        // "evilb23.tv" / "b23.tv.evil.com" 这类伪造域名混进来触发一次到攻击者
        // 服务器的出站请求。重定向逐跳校验：每一跳的 Location 在发起下一跳前
        // 都必须仍是可信 B 站域名，拒绝即中止——不访问非可信/私网目标。
        if (Uri.TryCreate(input, UriKind.Absolute, out var inputUri)
            && inputUri.Host.Equals("b23.tv", StringComparison.OrdinalIgnoreCase))
        {
            string tmp = await HTTPUtil.GetWebLocationCheckedAsync(input, IsTrustedBilibiliUri, token: token);
            if (tmp == input) throw new HttpRequestException($"短链解析失败（目标可能已失效或无法重定向）: {input}");
            input = tmp;
            lowerInput = input.ToLowerInvariant();
        }
        if (lowerInput.Contains("video/av"))
        {
            target = AvRegex().Match(input).Groups[1].Value;
        }
        else if (lowerInput.Contains("video/bv"))
        {
            target = DecodeBv(input);
        }
        else if (input.Contains("/cheese/"))
        {
            string epId = "";
            if (input.Contains("/ep"))
            {
                epId = EpRegex().Match(input).Groups[1].Value;
            }
            else if (input.Contains("/ss"))
            {
                epId = await GetEpidBySSIdAsync(SsRegex().Match(input).Groups[1].Value, token);
            }
            target = $"cheese:{epId}";
        }
        else if (input.Contains("/ep"))
        {
            string epId = EpRegex().Match(input).Groups[1].Value;
            target = $"ep:{epId}";
        }
        else if (input.Contains("/ss"))
        {
            string epId = await GetEpIdByBangumiSSIdAsync(SsRegex().Match(input).Groups[1].Value, token);
            target = $"ep:{epId}";
        }
        else if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_collection")) // 列表类型是合集
        {
            string bizId = BBDownUtil.GetQueryString("business_id", input);
            target = $"listBizId:{bizId}";
        }
        else if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_series")) // 列表类型是系列
        {
            string bizId = BBDownUtil.GetQueryString("business_id", input);
            target = $"seriesBizId:{bizId}";
        }
        else if (input.Contains("/channel/collectiondetail?sid="))
        {
            string bizId = BBDownUtil.GetQueryString("sid", input);
            target = $"listBizId:{bizId}";
        }
        else if (input.Contains("/channel/seriesdetail?sid="))
        {
            string bizId = BBDownUtil.GetQueryString("sid", input);
            target = $"seriesBizId:{bizId}";
        }
        else if (input.Contains("/space.bilibili.com/") && input.Contains("/lists/"))
        {
            var type = BBDownUtil.GetQueryString("type", input).ToLower();
            var path = input.Split('?', '#')[0];
            var sidPart = path[(path.LastIndexOf('/') + 1)..];

            if (type == "series")
            {
                target = $"seriesBizId:{sidPart}";
            }
            else
            {
                target = $"listBizId:{sidPart}";
            }
        }
        else if (input.Contains("/space.bilibili.com/") && input.Contains("/favlist"))
        {
            string mid = UidRegex().Match(input).Groups[1].Value;
            string fid = BBDownUtil.GetQueryString("fid", input);
            target = $"favId:{fid}:{mid}";
        }
        else if (input.Contains("/space.bilibili.com/"))
        {
            string mid = UidRegex().Match(input).Groups[1].Value;
            target = $"mid:{mid}";
        }
        else if (input.Contains("ep_id="))
        {
            string epId = BBDownUtil.GetQueryString("ep_id", input);
            target = $"ep:{epId}";
        }
        else if (GlobalEpRegex().Match(input).Success)
        {
            string epId = GlobalEpRegex().Match(input).Groups[1].Value;
            target = $"ep:{epId}";
        }
        else if (BangumiMdRegex().Match(input).Success)
        {
            string mdId = BangumiMdRegex().Match(input).Groups[1].Value;
            string epId = await GetEpIdByMDAsync(mdId, token);
            target = $"ep:{epId}";
        }
        else
        {
            // 泛抓取分支：对未识别的 HTTP URL 抓取整页 HTML 并解析 __INITIAL_STATE__。
            // 这是解析器里唯一会向"用户输入的目标主机"发起网络请求的路径，目标域名
            // 完全由输入决定。若不加域名守卫，serve 模式下攻击者提交自己的域名，
            // 该分支会用携带操作者 B 站 Cookie 的请求抓取攻击者页面（HTTPUtil 此前
            // 无条件附加 Cookie），把本地凭据外发给攻击者（SSRF + 凭据泄露）。
            // 因此这里：1) 只允许抓取 B 站官方域名；2) 用匿名请求（不带 Cookie）；
            // 3) 逐跳校验重定向——初始域名可信不代表重定向目标可信，可信域名的
            // 开放重定向仍可把请求导向内网/非可信主机，泛抓取必须逐跳校验。
            if (!IsTrustedBilibiliUrl(input))
                throw new ArgumentException("输入有误：仅支持解析 B 站域名下的链接");
            string web = await HTTPUtil.GetWebSourceAnonymousCheckedAsync(input, IsTrustedBilibiliUri, token: token);
            Regex regex = StateRegex();
            string json = regex.Match(web).Groups[1].Value;
            // 页面未含 __INITIAL_STATE__ 时 json 为空，JsonDocument.Parse("") 会抛底层 JsonException；
            // 换成用户可读的"无法解析"错误
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("输入有误：无法从该链接解析出视频信息");
            using var jDoc = JsonDocument.Parse(json);
            var epList = jDoc.RootElement.EnumerateArraySafe("epList");
            var firstEp = epList.FirstOrDefault();
            if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                throw new InvalidOperationException("未找到任何分P信息");
            string epId = firstEp.GetValueAsStringSafe("id");
            target = $"ep:{epId}";
        }
        return target;
    }

    /// <summary>
    /// 解析裸 ID / 前缀目标：bv/av 号、ep/ss/md、cheese 路径，以及下载器自身的前缀标识
    /// （mid:/favId:/listBizId:/seriesBizId:，与 FetcherFactory 对齐）。
    /// internal：从 <see cref="ResolveAsync"/> 拆出（I12），见 <see cref="ResolveHttpUrlAsync"/>。
    /// </summary>
    internal static async Task<string> ResolveBareIdAsync(string input, CancellationToken token = default)
    {
        var target = input;
        if (input.ToLowerInvariant().StartsWith("bv"))
        {
            target = DecodeBv(input);
        }
        else if (input.ToLowerInvariant().StartsWith("av"))
        {
            target = input.ToLowerInvariant()[2..];
        }
        else if (input.StartsWith("mid:") || input.StartsWith("favId:")
                 || input.StartsWith("listBizId:") || input.StartsWith("seriesBizId:"))
        {
            // 与 FetcherFactory 的前缀对齐：这些是下载器自身的列表/空间目标标识（如 sub add 的输入），
            // 直接透传给对应 fetcher，而不是落入下方"无法识别"分支
            target = input;
        }
        else if (input.StartsWith("cheese/"))
        {
            string epId = "";
            if (input.Contains("/ep"))
            {
                epId = EpRegex().Match(input).Groups[1].Value;
            }
            else if (input.Contains("/ss"))
            {
                epId = await GetEpidBySSIdAsync(SsRegex().Match(input).Groups[1].Value, token);
            }
            target = $"cheese:{epId}";
        }
        else if (input.StartsWith("ep") && input.Length > 2 && (char.IsAsciiDigit(input[2]) || input[2] == ':'))
        {
            // 兼容 "ep123" 与 "ep:123" 两种写法（sub add 会原样保存用户输入）。
            // 前置校验 ep 后跟数字/冒号：否则 "episode"/"epoxy" 之类会被解析为 ep:isode
            // 并发起对 B 站接口的请求。
            string epId = input[2..].TrimStart(':');
            target = $"ep:{epId}";
        }
        else if (input.StartsWith("ss") && input.Length > 2 && (char.IsAsciiDigit(input[2]) || input[2] == ':'))
        {
            try
            {
                string epId = await GetEpIdByBangumiSSIdAsync(input[2..].TrimStart(':'), token);
                target = $"ep:{epId}";
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Core.Logger.LogWarn($"番剧 SS 解析失败，尝试课程 SS: {ex.Message}");
                string epId = await GetEpidBySSIdAsync(input[2..].TrimStart(':'), token);
                target = $"cheese:{epId}";
            }
        }
        else if (input.StartsWith("md") && input.Length > 2 && (char.IsAsciiDigit(input[2]) || input[2] == ':'))
        {
            string mdId = input[2..].TrimStart(':');
            if (mdId == "")
                throw new ArgumentException($"输入有误：无法识别的专栏 ID，当前值: '{input}'");
            string epId = await GetEpIdByMDAsync(mdId, token);
            target = $"ep:{epId}";
        }
        else
        {
            throw new ArgumentException("输入有误：无法识别的视频 URL 或 ID");
        }
        return target;
    }
    private static async Task<string> FixAvidAsync(string avid, CancellationToken token = default)
    {
        // 空串（如裸 "av"/"bv" 输入剥离前缀后）的 All(char.IsDigit) 是真空真，会让
        // 下方对 "https://www.bilibili.com/video/av/" 发起一次无意义网络请求。先判空。
        if (string.IsNullOrEmpty(avid) || !avid.All(char.IsDigit))
            return avid;
        try
        {
            string api = $"https://www.bilibili.com/video/av{avid}/";
            // 用逐跳校验的重定向解析：av 跳转检查同样不允许跟随到非可信主机。
            // 返回原地址（未被重定向/重定向被拒）时按非番剧处理。
            string location = await HTTPUtil.GetWebLocationCheckedAsync(api, IsTrustedBilibiliUri, token: token);
            return location.Contains("/ep") ? $"ep:{EpRegex().Match(location).Groups[1].Value}" : avid;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException：若非用户主动取消（token 未取消），是 HttpClient
            // 超时/服务器断开——跳转检查失败按原 av 号处理，不阻断解析；
            // 若确实是用户取消，则重新抛出让上层走取消路径。
            if (token.IsCancellationRequested) throw;
            Core.Logger.LogWarn($"av{avid} 跳转检查失败: {ex.Message}");
            return avid;
        }
    }

    private static string DecodeBv(string input)
    {
        var m = BVRegex().Match(input);
        if (!m.Success)
            throw new ArgumentException("输入有误：无法识别的 BV 号");
        try
        {
            return BilibiliBvConverter.Decode(m.Groups[1].Value).ToString();
        }
        catch (ArgumentException ex)
        {
            // 长度/字符非法的 BV 号：转成用户可读的错误而非底层转换器异常，
            // 避免畸形输入（如裸 "bv"）直接因字符串切片越界而崩溃
            throw new ArgumentException($"输入有误：BV 号格式不正确 ({ex.Message})");
        }
    }

    internal static bool IsTrustedBilibiliUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return IsTrustedBilibiliUri(uri);
    }

    /// <summary>基于 <see cref="Uri"/> 的信任判定，供逐跳重定向校验回调使用。</summary>
    internal static bool IsTrustedBilibiliUri(Uri uri)
    {
        // 仅 http/https：泛抓取不需要其它协议，避免把输入导向 file:/ftp: 等本地资源
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        // Uri.Host 对标准 http/https 返回规范化主机名（不含端口/用户信息），
        // 直接比对主机名本身，杜绝 "evil.com/bilibili.com" 这类后缀字符串混淆。
        return HTTPUtil.IsOfficialBilibiliHost(uri.Host);
    }

    private static async Task<string> GetEpidBySSIdAsync(string ssid, CancellationToken token = default)
    {
        string api = $"https://api.bilibili.com/pugv/view/web/season?season_id={ssid}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var jDoc = JsonDocument.Parse(json);
        var episodes = jDoc.RootElement.GetPropertySafe("data").EnumerateArraySafe("episodes");
        var firstEp = episodes.FirstOrDefault();
        if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidOperationException("未找到课程分P信息");
        return firstEp.GetValueAsStringSafe("id");
    }

    private static async Task<string> GetEpIdByBangumiSSIdAsync(string ssId, CancellationToken token = default)
    {
        string api = $"https://{Core.Config.Current.EpHost}/pgc/view/web/season?season_id={ssId}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var jDoc = JsonDocument.Parse(json);
        var episodes = jDoc.RootElement.GetPropertySafe("result").EnumerateArraySafe("episodes");
        var firstEp = episodes.FirstOrDefault();
        if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidOperationException("未找到番剧分P信息");
        return firstEp.GetValueAsStringSafe("id");
    }

    private static async Task<string> GetEpIdByMDAsync(string mdId, CancellationToken token = default)
    {
        string api = $"https://api.bilibili.com/pgc/review/user?media_id={mdId}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var jDoc = JsonDocument.Parse(json);
        return jDoc.RootElement.GetPropertySafe("result").GetPropertySafe("media").GetPropertySafe("new_ep").GetValueAsStringSafe("id");
    }

    [GeneratedRegex("[Aa][Vv](\\d+)")]
    private static partial Regex AvRegex();

    [GeneratedRegex("[Bb][Vv]1(\\w+)")]
    private static partial Regex BVRegex();

    [GeneratedRegex("/ep(\\d+)")]
    private static partial Regex EpRegex();

    [GeneratedRegex("/ss(\\d+)")]
    private static partial Regex SsRegex();

    [GeneratedRegex(@"space\.bilibili\.com/(\d+)")]
    private static partial Regex UidRegex();

    [GeneratedRegex(@"\.bilibili\.tv\/\w+\/play\/\d+\/(\d+)")]
    private static partial Regex GlobalEpRegex();

    [GeneratedRegex("bangumi/media/(md\\d+)")]
    private static partial Regex BangumiMdRegex();

    [GeneratedRegex(@"window\.__INITIAL_STATE__=([\s\S].*?);\(function\(\)")]
    private static partial Regex StateRegex();
}
