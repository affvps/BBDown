using System.Net;
using System.Net.Sockets;
using System.Text;
using BBDown.Core.Util;

namespace BBDown.Tests;

public class GrpcTrailerTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("invalid")]
    public async Task GrpcStatus_IsReadFromTrailersAfterEntireBody(string status)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task RespondAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            int bodyLength = 0;
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    bodyLength = int.Parse(line[15..], System.Globalization.CultureInfo.InvariantCulture);
            }
            // 消费请求体，避免 Connection: close 被 TCP RST 覆盖而丢失最后的 trailers。
            var requestBody = new char[bodyLength];
            await reader.ReadBlockAsync(requestBody.AsMemory(), timeout.Token);
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/grpc\r\n" +
                "grpc-status: 0\r\nTransfer-Encoding: chunked\r\nTrailer: grpc-status\r\nConnection: close\r\n\r\n5\r\n");
            await stream.WriteAsync(head, timeout.Token);
            await stream.WriteAsync(new byte[5], timeout.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"\r\n0\r\ngrpc-status: {status}\r\n\r\n"), timeout.Token);
        }
        var server = RespondAsync();
        try
        {
            var request = HTTPUtil.GetPostResponseAsync($"http://127.0.0.1:{port}/grpc", [0], token: timeout.Token);
            if (status == "0") Assert.Equal(new byte[5], await request);
            else
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => request);
                Assert.Contains("grpc-status=" + status, ex.Message);
            }
        }
        finally { await server; }
    }
}
