using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

public class DownloadOrchestratorSelectionTests
{
    [Fact]
    public async Task MissingSelection_ReportsCountAndPreviewWithoutExpandingAllPages()
    {
        var selectedPages = Enumerable.Range(2, 100_000).Select(index => index.ToString()).ToList();
        var logs = new List<string>();
        var orchestrator = CreateOrchestrator(selectedPages, true, logs.Add);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.RunAsync(CreateJob(1)));

        Assert.Contains("所选分P不存在", error.Message);
        Assert.Contains("100000 项", error.Message);
        Assert.Contains("2,3,4", error.Message);
        Assert.DoesNotContain("100001", error.Message);
        Assert.True(error.Message.Length < 256);
        Assert.Single(logs);
        Assert.True(logs[0].Length < 256);
    }

    [Fact]
    public async Task ManyFailedPages_ReportsCountAndBoundedPreview()
    {
        var orchestrator = CreateOrchestrator(null, false, _ => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.RunAsync(CreateJob(Enumerable.Range(1, 25).ToArray())));

        Assert.Contains("共 25 个分P下载失败", error.Message);
        Assert.Contains("P20", error.Message);
        Assert.DoesNotContain("P25", error.Message);
        Assert.True(error.Message.Length < 256);
    }

    private static DownloadOrchestrator CreateOrchestrator(
        List<string>? selectedPages, bool downloadSucceeded, Action<string> log)
        => new(
            (_, _, _) => selectedPages,
            (_, _, _, _) => "unused",
            (_, _) => Task.FromResult(false),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.FromResult(downloadSucceeded),
            (_, _, _, _) => Task.CompletedTask,
            log,
            _ => { });

    private static DownloadPagesRequest CreateJob(params int[] pageIndexes)
    {
        var videoInfo = new VInfo
        {
            Title = "test",
            Desc = "",
            Pic = "",
            PubTime = 0,
            PagesInfo = pageIndexes.Select(index => new Page
            {
                index = index,
                aid = "1",
                cid = index.ToString(),
                epid = "",
                title = "",
                dur = 0,
                res = "",
                pubTime = 0,
            }).ToList(),
        };

        return new DownloadPagesRequest(new MyOption(), videoInfo, [], [], null, false, [],
            "input", "", "", 0, "", null);
    }
}
