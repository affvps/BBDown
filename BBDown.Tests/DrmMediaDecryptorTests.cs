namespace BBDown.Tests;

public class DrmMediaDecryptorTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("nonzero")]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public async Task Decrypt_OnlyReplacesInputAfterSuccessfulOutput_AndCleansTemporaryFiles(string outcome)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-decrypt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "input with spaces.mp4");
        using var cts = new CancellationTokenSource();
        try
        {
            await File.WriteAllTextAsync(input, "encrypted-original");
            // 历史固定 .dec 文件不能影响本次有效输出的判断。
            await File.WriteAllTextAsync(input + ".dec", "stale-output");
            var runner = new StubRunner(async (spec, token) =>
            {
                Assert.Equal("mp4decrypt", spec.FileName);
                Assert.Equal(1500, spec.TimeoutMs);
                Assert.Equal(["--key", "kid:key", input], spec.Arguments.Take(3));
                var output = spec.Arguments[3];
                if (outcome != "missing")
                    await File.WriteAllTextAsync(output, outcome == "empty" ? "" : "plaintext", token);
                if (outcome == "cancel") cts.Cancel();
                if (outcome == "timeout") throw new TimeoutException("process timed out");
                return outcome == "nonzero" ? 1 : 0;
            });
            var operation = new DrmMediaDecryptor(runner).DecryptAsync("mp4decrypt", "kid", "key", input, 1500, cts.Token);

            if (outcome == "success") await operation;
            else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            else if (outcome == "timeout") await Assert.ThrowsAsync<TimeoutException>(() => operation);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

            Assert.Equal(outcome == "success" ? "plaintext" : "encrypted-original", await File.ReadAllTextAsync(input));
            Assert.Empty(Directory.GetFiles(dir, "*.dec-*"));
            Assert.Equal("stale-output", await File.ReadAllTextAsync(input + ".dec"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Decrypt_OutputCannotReplaceInput_PreservesOriginalAndCleansTemporaryFile()
    {
        // Windows 的共享锁可重复制造替换失败；Unix 打开的 inode 不阻止 rename。
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-decrypt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "input.mp4");
        try
        {
            await File.WriteAllTextAsync(input, "encrypted-original");
            using (var locked = File.Open(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var runner = new StubRunner(async (spec, token) =>
                {
                    await File.WriteAllTextAsync(spec.Arguments[3], "plaintext", token);
                    return 0;
                });
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new DrmMediaDecryptor(runner).DecryptAsync("mp4decrypt", "kid", "key", input, 1500));
            }
            Assert.Equal("encrypted-original", await File.ReadAllTextAsync(input));
            Assert.Empty(Directory.GetFiles(dir, "*.dec-*"));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class StubRunner(Func<ExternalProcessSpec, CancellationToken, Task<int>> run) : IExternalProcessRunner
    {
        public Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
            => run(spec, cancellationToken);
    }
}
