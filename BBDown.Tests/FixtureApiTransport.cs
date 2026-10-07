using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>实例级重映射；真实请求仍经过 HTTPUtil 的状态码、重试、取消和响应上限。</summary>
internal sealed class FixtureApiTransport(FakeBilibiliApiServer server) : IApiTransport
{
    private string LocalUrl(string url)
    {
        var uri = new Uri(url);
        return $"http://127.0.0.1:{server.Port}{uri.PathAndQuery}";
    }

    public Task<string> GetStringAsync(string url, CancellationToken token = default, bool rejectHtml = true)
        => HttpApiTransport.Instance.GetStringAsync(LocalUrl(url), token, rejectHtml);

    public Task<byte[]> PostAsync(string url, byte[] body, Dictionary<string, string> headers, CancellationToken token = default)
        => HttpApiTransport.Instance.PostAsync(LocalUrl(url), body, headers, token);
}
