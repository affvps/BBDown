using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

public class CheeseInfoFetcher : IFetcher
{
    private readonly IApiTransport _transport;

    public CheeseInfoFetcher() : this(HttpApiTransport.Instance) { }

    internal CheeseInfoFetcher(IApiTransport transport) => _transport = transport;

    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[7..];
        string index = "";
        string api = $"https://api.bilibili.com/pugv/view/web/season?ep_id={id}";
        string json = await _transport.GetStringAsync(api, cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        FetcherJson.ThrowIfApiError(infoJson.RootElement, "获取课程信息失败");
        // RF-65：先查 code 再取 data——错误响应（code≠0 且无 data）经 GetPropertySafe 抛
        // 英文裸 KeyNotFoundException，使上方 code 诊断不可达。
        if (!infoJson.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("获取课程信息失败: 响应缺少 data 节点");
        string cover = data.GetValueAsStringSafe("cover");
        string title = data.GetValueAsStringSafe("title");
        string desc = data.GetValueAsStringSafe("subtitle");
        var upInfo = data.TryGetPropertySafe("up_info");
        string ownerName = upInfo?.GetValueAsStringSafe("uname") ?? "";
        string ownerMid = upInfo?.GetValueAsStringSafe("mid") ?? "";
        var pages = data.EnumerateArraySafe("episodes");
        List<Page> pagesInfo = new();
        foreach (var page in pages)
        {
            Page p = new()
            {
                index = page.GetInt32Safe("index"),
                aid = page.GetValueAsStringSafe("aid"),
                cid = page.GetValueAsStringSafe("cid"),
                epid = page.GetValueAsStringSafe("id"),
                title = page.GetValueAsStringSafe("title").Trim(),
                dur = page.GetInt32Safe("duration"),
                res = "",
                pubTime = page.GetInt64Safe("release_date"),
                cover = "",
                desc = "",
                ownerName = ownerName,
                ownerMid = ownerMid,
            };
            if (p.epid == id) index = p.index.ToString();
            pagesInfo.Add(p);
        }
        long pubTime = pagesInfo.Any() ? pagesInfo[0].pubTime : 0;

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = desc.Trim(),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            IsCheese = true,
            Index = index
        };

        return info;
    }
}
