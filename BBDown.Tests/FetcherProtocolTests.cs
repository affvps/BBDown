using BBDown.Core;
using BBDown.Core.Fetcher;

namespace BBDown.Tests;

public class FetcherProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalVideo_PreservesNumericOwnerAndFetchesInteractiveChoices(bool interactive)
    {
        using var server = new FakeBilibiliApiServer();
        server.Register("/x/web-interface/view", $$$$"""
            {"code":0,"data":{"title":" title ","desc":" description ","pic":"cover","pubdate":123,
            "bvid":"BVexample","cid":99,"owner":{"mid":42,"name":"author"},
            "rights":{"is_stein_gate":{{{{(interactive ? 1 : 0)}}}}},"is_upower_exclusive":true,
            "is_upower_preview":true,"is_upower_play":false,
            "pages":[{"page":1,"cid":99,"part":" start ","duration":10,"dimension":{"width":1920,"height":1080}}]}}
            """);
        server.Register("/x/player.so", "<interaction>{\"graph_version\":7}</interaction>");
        server.Register("/x/stein/edgeinfo_v2", """
            {"code":0,"data":{"edges":{"questions":[{"choices":[{"cid":100,"option":" choice A "},{"cid":101,"option":"choice B"}]}]}}}
            """);

        var info = await new NormalInfoFetcher(new FixtureApiTransport(server)).FetchAsync("170001");

        Assert.Equal("title", info.Title);
        Assert.Equal("description", info.Desc);
        Assert.Equal(interactive, info.IsSteinGate);
        Assert.True(info.IsUpowerExclusive);
        Assert.True(info.IsUpowerPreview);
        Assert.False(info.IsUpowerPlay);
        Assert.All(info.PagesInfo, p => { Assert.Equal("42", p.ownerMid); Assert.Equal("author", p.ownerName); });
        Assert.Equal("1920x1080", info.PagesInfo[0].res);
        Assert.Equal(interactive ? 3 : 1, info.PagesInfo.Count);
        Assert.Equal(interactive ? 3 : 1, server.Requests.Count);
        Assert.Contains("aid=170001", server.Requests[0].Query);
        if (interactive)
        {
            Assert.Equal(["99", "100", "101"], info.PagesInfo.Select(p => p.cid));
            Assert.Equal([1, 2, 3], info.PagesInfo.Select(p => p.index));
            Assert.Contains("graph_version=7", server.Requests[2].Query);
        }
    }

    [Fact]
    public async Task Bangumi_RequestedPreviewInSection_IsSelectedAndOtherPreviewsAreExcluded()
    {
        using var server = new FakeBilibiliApiServer();
        server.Register("/pgc/view/web/season", """
            {"code":0,"result":{"title":"Season","cover":"cover","evaluate":"desc","episodes":[{"id":1}],
            "section":[{"title":"Extras","episodes":[{"id":7,"aid":11,"cid":21,"title":"Trailer","badge":"预告"},
            {"id":8,"aid":12,"cid":22,"title":"Hidden trailer","badge":"预告"},{"id":9,"aid":13,"cid":23,"title":"Making of"}]}]}}
            """);

        var info = await new BangumiInfoFetcher(new FixtureApiTransport(server)).FetchAsync("ep:7");

        Assert.Equal("Season[Extras]", info.Title);
        Assert.Equal("1", info.Index);
        Assert.Equal(["7", "9"], info.PagesInfo.Select(p => p.epid));
        Assert.True(info.IsBangumi);
        Assert.Equal("7", FakeBilibiliApiServer.GetQueryValue(Assert.Single(server.Requests).Query, "ep_id"));
    }

    [Fact]
    public async Task Cheese_NumericMetadataAndRequestedEpisode_ArePreserved()
    {
        using var server = new FakeBilibiliApiServer();
        server.Register("/pugv/view/web/season", """
            {"code":0,"data":{"title":" Course ","subtitle":" summary ","cover":"cover","up_info":{"mid":42,"uname":"teacher"},
            "episodes":[{"id":7,"aid":17,"cid":27,"index":1,"title":" first ","duration":60,"release_date":123},
            {"id":8,"aid":18,"cid":28,"index":2,"title":" second ","duration":90,"release_date":124}]}}
            """);

        var info = await new CheeseInfoFetcher(new FixtureApiTransport(server)).FetchAsync("cheese:8");

        Assert.Equal("Course", info.Title);
        Assert.Equal("2", info.Index);
        Assert.True(info.IsCheese);
        Assert.Equal(123, info.PubTime);
        Assert.Equal("second", info.PagesInfo[1].title);
        Assert.All(info.PagesInfo, p => Assert.Equal("42", p.ownerMid));
    }

    [Theory]
    [InlineData("normal", "/x/web-interface/view", "170001")]
    [InlineData("bangumi", "/pgc/view/web/season", "ep:7")]
    [InlineData("cheese", "/pugv/view/web/season", "cheese:8")]
    public async Task ApiErrors_AreReportedBeforeMissingPayload(string kind, string path, string id)
    {
        using var server = new FakeBilibiliApiServer();
        server.Register(path, """{"code":-404,"message":"resource unavailable"}""");
        var transport = new FixtureApiTransport(server);
        IFetcher fetcher = kind switch
        {
            "normal" => new NormalInfoFetcher(transport),
            "bangumi" => new BangumiInfoFetcher(transport),
            _ => new CheeseInfoFetcher(transport)
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.FetchAsync(id));

        Assert.Contains("-404", ex.Message);
        Assert.Single(server.Requests);
    }
}
