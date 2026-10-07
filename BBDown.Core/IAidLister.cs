namespace BBDown.Core;

/// <summary>
/// 轻量条目枚举：只列出条目对应的视频 aid，**不展开分P详情**。
///
/// sub check 的增量扫描只关心"有哪些 aid、哪些还没下载过"；走
/// <see cref="IFetcher.FetchAsync"/> 会为每个投稿再发一次详情请求
/// （<see cref="Fetcher.SpaceVideoFetcher"/>：投稿数量级请求 + 120ms 间隔，
/// 单订阅就要十秒以上，大 UP 主是分钟级且显著加重风控），
/// 而每个新 aid 的下载（DoWorkAsync → av{aid}）本来就会各自重新解析一次。
///
/// 目前只有 <see cref="Fetcher.SpaceVideoFetcher"/>（<c>mid:</c> 目标）实现：
/// 投稿列表接口固定 <c>order=pubdate</c> 倒序返回，"整页都已下载过即停止翻页"这一
/// 判据成立，代价是停止点之后的更旧页不再扫描（<c>--full-scan</c> 可强制全量重扫）。
/// 收藏夹（<c>order=mtime</c>，会与用户手动排序/取消收藏交互）、合集/系列暂不实现，
/// 仍走全量解析路径。
/// </summary>
public interface IAidLister
{
    /// <summary>
    /// 列出尚未下载过的 aid（按列表顺序，最新在前）。
    /// </summary>
    /// <param name="id">目标标识，如 <c>mid:163637592</c>。</param>
    /// <param name="known">已成功下载过的 aid 集合（订阅历史）。</param>
    /// <param name="fullScan">true = 不做提前结束，翻完所有页（CLI: <c>sub check --full-scan</c>）。</param>
    Task<List<string>> ListNewAidsAsync(string id, IReadOnlySet<string> known, bool fullScan, CancellationToken cancellationToken = default);
}
