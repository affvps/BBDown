using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>
/// Entity 值对象的服务器可控输入防御（第 14 轮消纳批 RF-48/RF-60）。
/// </summary>
public class EntityTests
{
    private static Page MakePage(string aid) => new()
    {
        index = 1,
        aid = aid,
        cid = "1",
        epid = "",
        title = "t",
        dur = 0,
        res = "",
        pubTime = 0,
        cover = "",
        desc = "",
    };

    // ── RF-48：Page.bvid getter 对服务器可控 aid 的 AOORE 防护 ──

    [Theory]
    [InlineData("170001")]
    [InlineData("0")]          // Encode 范围校验下界：AOORE → 回落原始 aid
    [InlineData("-5")]         // 负数：AOORE → 回落原始 aid
    [InlineData("2251799813685248")] // >= MAX_AID（2^51）：AOORE → 回落原始 aid
    public void Page_Bvid_OutOfRangeAid_FallsBackToRawAid(string aid)
    {
        var p = MakePage(aid);
        if (long.TryParse(aid, out var n) && n >= 1 && n < 2251799813685248L)
            Assert.Equal(BBDown.Core.Util.BilibiliBvConverter.Encode(n), p.bvid);
        else
            Assert.Equal(aid, p.bvid);
    }

    [Fact]
    public void Page_Bvid_NonNumericAid_ReturnsRawAid()
    {
        Assert.Equal("BV1xx411c7mD", MakePage("BV1xx411c7mD").bvid);
    }

    // ── I13 前置断言：Page 的字段语义（构造形态在重构中由"阶梯构造器"改为"无参构造 + 初始化器"，
    //    这些断言是重构前后共同遵守的契约，不随构造语法变化）──

    [Fact]
    public void Page_FullConstruction_MapsEveryField()
    {
        var p = new Page
        {
            index = 3,
            aid = "170001",
            cid = "999",
            epid = "ep1",
            title = "标题",
            dur = 125,
            res = "1920x1080",
            pubTime = 1700000000,
            cover = "cover",
            desc = "desc",
            ownerName = "up",
            ownerMid = "42",
        };

        Assert.Equal(3, p.index);
        Assert.Equal("170001", p.aid);
        Assert.Equal("999", p.cid);
        Assert.Equal("ep1", p.epid);
        Assert.Equal("标题", p.title);
        Assert.Equal(125, p.dur);
        Assert.Equal("1920x1080", p.res);
        Assert.Equal(1700000000, p.pubTime);
        Assert.Equal("cover", p.cover);
        Assert.Equal("desc", p.desc);
        Assert.Equal("up", p.ownerName);
        Assert.Equal("42", p.ownerMid);
        Assert.Empty(p.points);
    }

    /// <summary>RF-73：aid/cid/epid 的净化只在属性 setter 收口——任何构造路径都必须过同一道闸。</summary>
    [Fact]
    public void Page_PathSegments_AreSanitizedOnEveryConstructionPath()
    {
        const string evil = "..\\..\\..\\Windows\\System32";
        var p = new Page
        {
            index = 1,
            aid = evil,
            cid = evil,
            epid = evil,
            title = "t",
            dur = 0,
            res = "",
            pubTime = 0,
        };
        Assert.Equal(".._.._.._Windows_System32", p.aid);
        Assert.Equal(".._.._.._Windows_System32", p.cid);
        Assert.Equal(".._.._.._Windows_System32", p.epid);
    }

    /// <summary>拷贝构造：index 用新值，其余字段（含可选字段）从源对象复制。</summary>
    [Fact]
    public void Page_CopyConstructor_OverridesIndexAndCopiesOtherFields()
    {
        var source = new Page
        {
            index = 1,
            aid = "170001",
            cid = "999",
            epid = "ep1",
            title = "原标题",
            dur = 10,
            res = "1280x720",
            pubTime = 7,
            cover = "cover",
            desc = "desc",
            ownerName = "up",
            ownerMid = "42",
        };
        var copy = new Page(9, source);

        Assert.Equal(9, copy.index);
        Assert.Equal(source.aid, copy.aid);
        Assert.Equal(source.cid, copy.cid);
        Assert.Equal(source.epid, copy.epid);
        Assert.Equal(source.title, copy.title);
        Assert.Equal(source.dur, copy.dur);
        Assert.Equal(source.res, copy.res);
        Assert.Equal(source.pubTime, copy.pubTime);
        Assert.Equal(source.cover, copy.cover);
        Assert.Equal(source.desc, copy.desc);
        Assert.Equal(source.ownerName, copy.ownerName);
        Assert.Equal(source.ownerMid, copy.ownerMid);
    }

    // ── RF-60：Audio.shortCodecs 文化不变性 ──

    [Fact]
    public void Audio_ShortCodecs_IsCultureInvariant()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // RF-68：tr-TR 下 'i' 经文化敏感 ToUpper() 变 'İ'（U+0130），查表失败静默退化选轨优先级。
            // 输入必须含小写 'i' 才能触发该规则——原输入 "e-ac-3" 不含 'i'，ToUpperInvariant
            // 与回退后的 ToUpper() 在 tr-TR 下产出相同，断言恒成立（假绿）。此处用 "avci"（AVC Intra）作回归输入。
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var a = new Audio { id = "1", dfn = "", baseUrl = "https://x", codecs = "avci", bandwidth = 0, dur = 0 };
            Assert.Equal("AVCI", a.shortCodecs);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
