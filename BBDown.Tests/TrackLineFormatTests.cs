namespace BBDown.Tests;

/// <summary>
/// I15 收敛点回归：Display.cs 的展示行组装与码率估算公式。
/// 两者都是"抽公共实现"的收敛点，须钉住与收敛前写法逐字符等价（组装）
/// 与算式等价（估算；带宽单位是 kbps，见 Parser.cs 取接口 bps 值 / 1000）。
/// </summary>
public class TrackLineFormatTests
{
    /// <summary>
    /// 收敛前的写法（Display.cs / DownloadTrackPreparation.cs 的历史实现）：
    /// 字段照写后整体 .Replace("[] ", "")。仅作等价性对照，不参与生产代码。
    /// 对照集不含"末字段为空"——旧写法会留下尾部 "[]"，而真实用法末字段恒为 [~大小]/[大小]。
    /// </summary>
    private static string LegacyTrackLine(string prefix, params string?[] fields)
        => (prefix + string.Join(" ", fields.Select(f => $"[{f}]"))).Replace("[] ", "");

    [Theory]
    [InlineData("0. ", "1080P", "1920x1080", "avc1.640028", "30", "2000 kbps", "~15.00 MB")]
    [InlineData("0. ", "1080P", null, "avc1.640028", null, "2000 kbps", "~15.00 MB")]
    [InlineData("0. ", null, null, "ec-3", null, "448 kbps", "~3.20 MB")]
    [InlineData("[视频] ", "4K", "3840x2160", "hev1.1.6.L150", "60", "8000 kbps", "~1.00 GB")]
    public void BuildTrackLine_MatchesLegacyReplaceHack(
        string prefix, string? dfn, string? res, string codecs, string? fps, string bandwidth, string size)
    {
        var expected = LegacyTrackLine(prefix, dfn, res, codecs, fps, bandwidth, size);

        Assert.Equal(expected, Program.BuildTrackLine(prefix, dfn, res, codecs, fps, bandwidth, size));
    }

    [Fact]
    public void BuildTrackLine_DoesNotRewriteFieldText()
    {
        // 收敛前的整体 Replace 会把字段内容里的 "[] " 一并吃掉（误伤）；
        // 新实现只按字段判空决定"是否显示该段"，不修改字段内容。
        Assert.Equal("0. [a[] b] [x]", Program.BuildTrackLine("0. ", "a[] b", "x"));
        // 旧写法把字段内容里的 "[] " 一并吃掉——这正是新实现修掉的差异，一并钉住
        Assert.Equal("0. [ab] [x]", LegacyTrackLine("0. ", "a[] b", "x"));
    }

    [Theory]
    [InlineData(320L, 60, 2_457_600L)]
    [InlineData(2000L, 60, 15_360_000L)]
    [InlineData(0L, 3600, 0L)]
    public void EstimatedBytes_IsSecondsTimesKbpsOverEight(long kbps, int seconds, long expected)
        => Assert.Equal(expected, Program.EstimatedBytes(kbps, seconds));

    [Fact]
    public void EstimatedBytes_KeepsLongArithmetic()
    {
        // 1 天 × 60 Mbps：旧内联写法因 Entity.bandwidth 是 long 本就不溢出，
        // 收敛后签名固定为 long，防止回归成 int 运算（2^31 量级即溢出）
        Assert.Equal(663_552_000_000L, Program.EstimatedBytes(60_000L, 86_400));
    }
}
