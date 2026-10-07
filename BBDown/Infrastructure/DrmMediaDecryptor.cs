namespace BBDown;

/// <summary>通过实例执行器解密一个文件，验证输出后才替换原始媒体。</summary>
internal sealed class DrmMediaDecryptor(IExternalProcessRunner runner)
{
    internal async Task DecryptAsync(string tool, string kid, string key, string input, int timeoutMs, CancellationToken token = default)
    {
        // 唯一临时名防止上一次遗留的 .dec 文件被误认为本次成功输出。
        var output = input + ".dec-" + Guid.NewGuid().ToString("N");
        var errors = new List<string>();
        try
        {
            int code = await runner.RunAsync(new ExternalProcessSpec
            {
                FileName = tool,
                Arguments = [.. Program.BuildDecryptArguments(kid, key, input, output)],
                TimeoutMs = timeoutMs,
                ToolDisplayName = "mp4decrypt",
                OnStandardError = errors.Add
            }, token);
            if (code != 0)
                throw new InvalidOperationException($"mp4decrypt 解密失败 (code={code}): {string.Join(Environment.NewLine, errors)}");
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("mp4decrypt 退出码为 0 但未产出有效的解密文件");
            token.ThrowIfCancellationRequested();
            // 同目录覆盖移动，避免先 Delete 原文件再 Move 失败导致丢失唯一媒体副本。
            try { File.Move(output, input, overwrite: true); }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"无法替换解密文件（原文件已保留）: {input}", ex);
            }
        }
        finally
        {
            try { File.Delete(output); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 保留解密/取消的原始异常。 */ }
        }
    }
}
