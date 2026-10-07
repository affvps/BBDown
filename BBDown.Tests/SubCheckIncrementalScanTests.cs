using BBDown.Core;
using BBDown.Core.Fetcher;

namespace BBDown.Tests;

/// <summary>
/// sub check 的增量扫描（<see cref="IAidLister"/>）此前不存在：每次检查都走
/// IFetcher.FetchAsync，对 mid: 目标要为**每个**投稿再发一次详情请求展开分P
/// （实测一个 53 投稿的订阅，每次检查都固定花 ~11 秒在展开上，与是否有新内容无关）。
///
/// 这里用假分页驱动 <see cref="SpaceVideoFetcher.CollectNewAidsAsync"/>（该方法不触网），
/// 钉住"只取新 aid + 整页均已下载即停止翻页"的判据与各个边界。
/// </summary>
public class SubCheckIncrementalScanTests
{
    /// <summary>构造假分页：<paramref name="pages"/> 按页号 1..N 给出各页 aid，超出范围返回空页。</summary>
    private static (Func<int, Task<(IReadOnlyList<string> Aids, int TotalCount)>> Fetch, List<int> Requested)
        FakePages(int totalCount, params string[][] pages)
    {
        var requested = new List<int>();
        Task<(IReadOnlyList<string>, int)> Fetch(int pageNumber)
        {
            requested.Add(pageNumber);
            IReadOnlyList<string> aids = pageNumber >= 1 && pageNumber <= pages.Length ? pages[pageNumber - 1] : [];
            return Task.FromResult((aids, totalCount));
        }
        return (Fetch, requested);
    }

    private static Task<List<string>> Collect(
        IReadOnlySet<string> known,
        bool fullScan,
        Func<int, Task<(IReadOnlyList<string> Aids, int TotalCount)>> fetch)
        => SpaceVideoFetcher.CollectNewAidsAsync("mid:1", known, fullScan, fetch, CancellationToken.None);

    private static HashSet<string> Known(params string[] aids) => [.. aids];

    [Fact]
    public async Task NewAidsAreCollectedInListOrder_OldOnesSkipped()
    {
        // 一页装得下（total 3 < PageSize 50）：只请求第 1 页
        var (fetch, requested) = FakePages(3, ["a1", "a2", "a3"]);
        var newAids = await Collect(Known("a3"), fullScan: false, fetch);

        Assert.Equal(["a1", "a2"], newAids);
        Assert.Equal([1], requested);
    }

    [Fact]
    public async Task AllKnownFirstPage_StopsImmediately()
    {
        // 最常见的"没有新增"：只看第 1 页就结束，绝不触发任何逐稿展开
        var (fetch, requested) = FakePages(120, ["k1", "k2"], ["k3"], ["k4"]);
        var newAids = await Collect(Known("k1", "k2", "k3", "k4"), fullScan: false, fetch);

        Assert.Empty(newAids);
        Assert.Equal([1], requested);
    }

    [Fact]
    public async Task FullyKnownLaterPage_StopsPaging_ButKeepsEarliestNewContent()
    {
        // 第 1 页有新增 → 继续翻页确认边界；第 2 页全为已下载 → 停在第 2 页，不请求第 3 页
        var (fetch, requested) = FakePages(150, ["new1", "k1"], ["k2"], ["k3"]);
        var newAids = await Collect(Known("k1", "k2", "k3"), fullScan: false, fetch);

        Assert.Equal(["new1"], newAids);
        Assert.Equal([1, 2], requested);
    }

    [Fact]
    public async Task FullScan_IgnoresEarlyStop_AndPagesToTheEnd()
    {
        // --full-scan：同样数据下必须翻完全部页（供"漏下的旧投稿"重新纳入）
        var (fetch, requested) = FakePages(150, ["new1", "k1"], ["k2"], ["k3"]);
        var newAids = await Collect(Known("k1", "k2", "k3"), fullScan: true, fetch);

        Assert.Equal(["new1"], newAids);
        Assert.Equal([1, 2, 3], requested);
    }

    [Fact]
    public async Task UnknownOldAid_KeepsScanning_SoFailedDownloadsStillRetried()
    {
        // 上次下载失败的稿件不写历史 → 它是"未知 aid"，所在页仍算有新增，
        // 扫描不会因增量而把它跳过（若要找更旧的失败稿件用 --full-scan）
        var (fetch, requested) = FakePages(100, ["k1", "failed-old"], ["k2"]);
        var newAids = await Collect(Known("k1", "k2"), fullScan: false, fetch);

        Assert.Equal(["failed-old"], newAids);
        Assert.Equal([1, 2], requested);
    }

    [Fact]
    public async Task BoundaryDuplicate_CountsOnce_AndDoesNotKeepPaging()
    {
        // 翻页期间 UP 主新增投稿会让边界条目在相邻两页重复；重复项不算"本页新增"，
        // 否则会在边界处永远多翻一页
        var (fetch, requested) = FakePages(150, ["n1", "k1"], ["n1", "k2"], ["k3"]);
        var newAids = await Collect(Known("k1", "k2", "k3"), fullScan: false, fetch);

        Assert.Equal(["n1"], newAids);
        Assert.Equal([1, 2], requested);
    }

    [Fact]
    public async Task EmptyLaterPage_StopsWithoutThrowing()
    {
        // 接口提前结束（翻页上限/风控降级）：停止翻页但不丢已收集结果、不抛异常
        var (fetch, requested) = FakePages(150, ["n1", "k1"], []);
        var newAids = await Collect(Known("k1"), fullScan: true, fetch);

        Assert.Equal(["n1"], newAids);
        Assert.Equal([1, 2], requested);
    }

    [Fact]
    public async Task EmptyFirstPage_ThrowsReadableError()
    {
        var (fetch, _) = FakePages(0);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(Known(), fullScan: false, fetch));

        Assert.Contains("mid:1", ex.Message);
    }

    [Fact]
    public async Task ZeroTotalCountWithContent_DoesNotAskForMorePages()
    {
        // 接口给的 page.count 为 0 但列表有内容：按 1 页处理，不能除零/少算页数
        var (fetch, requested) = FakePages(0, ["n1"]);
        var newAids = await Collect(Known(), fullScan: false, fetch);

        Assert.Equal(["n1"], newAids);
        Assert.Equal([1], requested);
    }

    [Fact]
    public void SpaceFetcher_ImplementsAidLister_SoMidTargetsUseTheLightPath()
    {
        // sub check 的轻量路径依赖这句 instanceof：mid: 目标必须命中
        Assert.IsAssignableFrom<IAidLister>(FetcherFactory.CreateFetcher("mid:163637592", false));
    }
}
