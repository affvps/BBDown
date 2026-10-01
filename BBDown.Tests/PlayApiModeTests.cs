using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// 接口模式优先级的**单一来源**：<c>--use-intl-api</c> / <c>--use-app-api</c> / <c>--use-tv-api</c>
/// 同时给出时，日志与 <c>&lt;apiType&gt;</c> 展示值必须等于 Parser 实际分派的接口。
/// 收口前两者各有一套顺序（展示 TV &gt; APP &gt; INTL，分派 INTL &gt; APP &gt; TV），
/// 一旦有人只改一边，这里就会失败。
/// </summary>
public class PlayApiModeTests
{
    [Theory]
    [InlineData(false, false, false, PlayApiMode.Web, "WEB")]
    [InlineData(true, false, false, PlayApiMode.Tv, "TV")]
    [InlineData(false, true, false, PlayApiMode.Intl, "INTL")]
    [InlineData(false, false, true, PlayApiMode.App, "APP")]
    // 多开关组合：INTL > APP > TV（与 GetPlayJsonAsync 的分派顺序一致）
    [InlineData(true, true, false, PlayApiMode.Intl, "INTL")]
    [InlineData(true, false, true, PlayApiMode.App, "APP")]
    [InlineData(false, true, true, PlayApiMode.Intl, "INTL")]
    [InlineData(true, true, true, PlayApiMode.Intl, "INTL")]
    public void ResolveApiMode_AndLabel_FollowDispatchPriority(bool tv, bool intl, bool app, PlayApiMode expected, string label)
    {
        var mode = Parser.ResolveApiMode(tv, intl, app);

        Assert.Equal(expected, mode);
        Assert.Equal(label, Parser.ApiModeLabel(mode));
    }

    /// <summary>展示名必须是四个固定标签之一（&lt;apiType&gt; 占位符的取值集合，wiki 有表）。</summary>
    [Fact]
    public void ApiModeLabel_CoversEveryMode()
    {
        var labels = Enum.GetValues<PlayApiMode>().Select(Parser.ApiModeLabel).ToArray();

        Assert.Equal(new[] { "WEB", "INTL", "APP", "TV" }, labels);
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }
}
