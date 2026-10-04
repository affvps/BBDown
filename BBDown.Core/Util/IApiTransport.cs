namespace BBDown.Core.Util;

/// <summary>每个解析器持有的 API 传输入口；默认仍使用统一的凭据、重试和响应大小策略。</summary>
internal interface IApiTransport
{
    Task<string> GetStringAsync(string url, CancellationToken token = default, bool rejectHtml = true);
    Task<byte[]> PostAsync(string url, byte[] body, Dictionary<string, string> headers, CancellationToken token = default);
}

internal sealed class HttpApiTransport : IApiTransport
{
    internal static readonly HttpApiTransport Instance = new();

    public Task<string> GetStringAsync(string url, CancellationToken token = default, bool rejectHtml = true)
        => HTTPUtil.GetWebSourceAsync(url, token: token, rejectHtml: rejectHtml);

    public Task<byte[]> PostAsync(string url, byte[] body, Dictionary<string, string> headers, CancellationToken token = default)
        => HTTPUtil.GetPostResponseAsync(url, body, headers, token);
}
