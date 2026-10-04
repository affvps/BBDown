using BBDown.Core;
using BBDown.Core.Protobuf;
using Google.Protobuf;

namespace BBDown.Tests;

public class AppGrpcProtocolTests
{
    private const string UgcPath = "/bilibili.app.playurl.v1.PlayURL/PlayView";
    private const string PgcPath = "/bilibili.pgc.gateway.player.v2.PlayURL/PlayView";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayView_RealHttpAndProtobuf_MapsThroughParser(bool bangumi)
    {
        using var server = new FakeBilibiliApiServer();
        var reply = new PlayViewReply
        {
            VideoInfo = new VideoInfo { Timelength = 10000 },
            Business = new BusinessInfo { ClipInfo = { new ClipInfo { Start = 1, End = 3, ToastText = "opening" } } }
        };
        reply.VideoInfo.StreamList.Add(new StreamItem
        {
            StreamInfo = new StreamInfo { Quality = 80 },
            DashVideo = new DashVideo { BaseUrl = "https://media.example/video", BackupUrl = { "https://backup.example/video" }, Codecid = 7, Size = 10000 }
        });
        reply.VideoInfo.DashAudio.Add(new DashItem { Id = 30280, BaseUrl = "https://media.example/audio", Bandwidth = 128000 });
        reply.VideoInfo.Flac = new DolbyItem { Audio = new DashItem { Id = 30251, BaseUrl = "https://media.example/flac", Bandwidth = 200000 } };
        server.Register(bangumi ? PgcPath : UgcPath, AppHelper.PackMessage(reply.ToByteArray()), "0");

        var parsed = await Parser.ExtractTracksAsync(new FixtureApiTransport(server),
            bangumi ? "ep:7" : "av170001", "170001", "999", bangumi ? "7" : "", false, false, true,
            "AVC", qn: "80");

        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        var payload = PlayViewReq.Parser.ParseFrom(AppHelper.ReadMessage(request.Body));
        Assert.Equal(bangumi ? 7 : 170001, payload.EpId);
        Assert.Equal(999, payload.Cid);
        Assert.Equal(80, payload.Qn);
        Assert.Equal(bangumi ? PlayViewReq.Types.CodeType.Code265 : PlayViewReq.Types.CodeType.Code264, payload.PreferCodecType);
        var video = Assert.Single(parsed.VideoTracks);
        Assert.Equal("80", video.id);
        Assert.Equal("AVC", video.codecs);
        Assert.Equal("https://media.example/video", video.baseUrl);
        Assert.Equal(2, parsed.AudioTracks.Count);
        Assert.Contains(parsed.AudioTracks, audio => audio.codecs == "FLAC");
        Assert.Equal(10, parsed.ActualDurationSec);
        if (bangumi) Assert.Contains(parsed.ExtraPoints, point => point.title == "opening");
    }

    [Fact]
    public async Task PlayView_MalformedProtobuf_ReportsPageFailure()
    {
        using var server = new FakeBilibiliApiServer();
        server.Register(UgcPath, AppHelper.PackMessage([0xff]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppHelper.DoReqAsync(new FixtureApiTransport(server), "170001", "999", "", "80", false, "AVC"));

        Assert.Contains("反序列化失败", ex.Message);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task PlayView_Http200WithGrpcError_IsNotAcceptedAsPlayableData()
    {
        using var server = new FakeBilibiliApiServer();
        server.Register(UgcPath, [], "7");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppHelper.DoReqAsync(new FixtureApiTransport(server), "170001", "999", "", "80", false, "AVC"));

        Assert.Contains("grpc-status=7", ex.Message);
        Assert.Single(server.Requests);
    }
}
