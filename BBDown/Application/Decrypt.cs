using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core.DRM;
using BBDown.Core.Entity;
using System.Diagnostics;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    /// <summary>
    /// 启动外部进程并把"已解析但不可启动"的 Win32Exception 规范化为 InvalidOperationException（RF-43）：
    /// Unix 显式路径无执行位 / Windows 损坏或错误架构二进制（ERROR_BAD_EXE_FORMAT）抛出的
    /// Win32Exception 不在下载页两级 catch 过滤器白名单内，会穿透中止整批。
    /// 与 SystemProcessRunner 启动点同构。
    /// </summary>
    private static Process StartProcessSafe(ProcessStartInfo psi, string toolName)
    {
        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException($"{toolName} 无法启动: {psi.FileName}（进程启动失败）");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{toolName} 无法启动: {psi.FileName}（{ex.Message}）", ex);
        }
    }

    /// <summary>
    /// 解析 device.wvd 路径：<c>--wvd-path</c> 显式指定优先（存在才采用），否则检索
    /// PATH / 程序目录（<see cref="FindTool"/>），最后回落到程序目录的内置 device.wvd。
    /// 内置文件随发布包分发（打包脚本把 device.wvd 与可执行文件放进同一 zip），
    /// 因此默认无需用户准备任何设备文件。
    /// </summary>
    internal static string ResolveWvdPath(MyOption myOption)
        => !string.IsNullOrEmpty(myOption.WvdPath) && File.Exists(myOption.WvdPath)
            ? myOption.WvdPath
            : FindTool("device.wvd") ?? Path.Combine(AppContext.BaseDirectory, "device.wvd");

    /// <summary>
    /// 解析 mp4decrypt 路径：<c>--mp4decrypt-path</c> 显式指定优先，其次 PATH / 程序目录（Bento4）。
    /// 未找到返回 null。
    /// </summary>
    internal static string? ResolveMp4DecryptPath(MyOption myOption)
        => !string.IsNullOrEmpty(myOption.Mp4decryptPath) && File.Exists(myOption.Mp4decryptPath)
            ? myOption.Mp4decryptPath
            : FindTool("mp4decrypt");

    /// <summary>
    /// DRM 预检：在**下载流之前**确认解密链路的两个外部条件（mp4decrypt、device.wvd/手动密钥），
    /// 避免"下了几个 G 才发现缺工具"。返回 false 时已打印可操作指引，调用方按该分P失败处理。
    /// 非 DRM 内容、或用户以 --no-decrypt-drm 关闭自动处理时恒为 true（不做任何检查）。
    /// </summary>
    internal static bool EnsureDrmToolsAvailable(ParsedResult parsed, MyOption myOption)
    {
        var problem = DrmToolchainProblem(parsed, myOption, ResolveMp4DecryptPath(myOption), ResolveWvdPath(myOption));
        if (problem is null) return true;

        foreach (var line in problem.Split('\n'))
            Logger.LogError(line);
        return false;
    }

    /// <summary>
    /// 预检的**纯判定**（工具解析在调用方完成，便于单测对"缺 mp4decrypt"/"缺 device.wvd"
    /// 两种场景拿到确定性结果）：返回 null = 解密链路可用；否则返回两行可操作错误说明
    /// （第一行现状、第二行怎么办），以换行分隔。
    /// </summary>
    internal static string? DrmToolchainProblem(ParsedResult parsed, MyOption myOption, string? mp4decrypt, string wvdPath)
    {
        if (!parsed.IsDrm || !myOption.AutoDecryptDrm) return null;

        if (string.IsNullOrEmpty(mp4decrypt))
        {
            return "此视频受 DRM 保护，需要 mp4decrypt（Bento4）才能解密，但未找到该工具。\n" +
                "请从 https://github.com/axiomatic-systems/Bento4/releases 下载 mp4decrypt，" +
                "放入 PATH 或程序目录，或用 --mp4decrypt-path 指定路径后重试。";
        }

        bool manualKeys = !string.IsNullOrEmpty(myOption.DrmKeyHex) && !string.IsNullOrEmpty(myOption.DrmKidHex);
        if (!manualKeys && parsed.DrmTechType == 2 && !File.Exists(wvdPath))
        {
            return "此视频的 Widevine 密钥需要 device.wvd，但未找到该文件（发布包内置）。\n" +
                "请确认下载解压完整，或用 --wvd-path 指定 device.wvd，或用 --key/--kid 手动提供密钥。";
        }
        return null;
    }

    private static async Task DecryptDrmAsync(ParsedResult parsed, string videoPath, string audioPath, MyOption myOption, CancellationToken token = default)
    {
        Logger.Log("检测到DRM加密，正在获取解密密钥...");

        parsed.KeyHex = myOption.DrmKeyHex ?? "";
        if (!string.IsNullOrEmpty(myOption.DrmKidHex))
            parsed.KidHex = myOption.DrmKidHex;

        if (!string.IsNullOrEmpty(parsed.KeyHex) && !string.IsNullOrEmpty(parsed.KidHex))
        {
            // 手动密钥也不回显 key 材料：与 CkcDecryptor 的 A2 修复一致，只记 kid 与长度，
            // 否则 INFO 级日志会把 AES-128 密钥的 25% 写进持久日志文件
            Logger.Log($"使用手动提供的密钥: kid={parsed.KidHex}, key 长度={parsed.KeyHex.Length} hex 字符");
        }
        else
        {
            try
            {
                // drm_tech_type=2 是 Widevine（B 站约定，与 Parser.cs 请求 &drm_tech_type=2 对应）：
                // 携带 PSSH 时走 Widevine 许可证解密。其它技术类型（如 DASH 明文）不在此路径。
                if (parsed.DrmTechType == 2)
                {
                    if (!string.IsNullOrEmpty(parsed.PsshBase64))
                    {
                        var wvd = ResolveWvdPath(myOption);
                        if (File.Exists(wvd))
                        {
                            var keyResult = await DrmDecryptor.GetKeyWidevineAsync(parsed.PsshBase64, wvd, token);
                            if (keyResult != null)
                            {
                                parsed.KeyHex = keyResult.Value.keyHex;
                                parsed.KidHex = keyResult.Value.kid;
                            }
                        }
                        else
                        {
                            Logger.LogWarn("Widevine DRM 需要 device.wvd（发布包内置），当前未找到；" +
                                "请确认解压完整，或用 --wvd-path 指定文件");
                        }
                    }
                }
                else
                {
                    Logger.LogWarn("当前DRM类型不支持自动解密，请使用 --key --kid 手动提供密钥");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
            {
                Logger.LogWarn($"自动密钥提取异常: {ex.Message}");
            }

            // 取钥必须同时得到 Key 与 Kid：mp4decrypt 的 key-file 行格式是 "kid:key"，
            // Kid 为空会生成 ":key" 无效行导致解密失败或静默产出错误输出。
            // 仅检查 KeyHex 会放过 KidHex 为空的半成品（手动 --key 未配 --kid）。
            if (string.IsNullOrEmpty(parsed.KeyHex) || string.IsNullOrEmpty(parsed.KidHex))
            {
                // 用户显式请求了 DRM 解密（--decrypt-drm / --key / --kid）但取钥失败：
                // 若只是打印警告并 return，任务会继续把"仍是加密的流"当成功产物混流/交付，
                // 用户拿到加密文件却被告知下载成功。这里抛异常让调用方把任务标记为失败，
                // 而不是静默交付加密产物。
                throw new InvalidOperationException(
                    "DRM 解密密钥获取失败（Key 或 Kid 缺失），无法解密。" +
                    "请确认 device.wvd 位于程序目录（发布包内置，--wvd-path 可指定外部 WVD 文件）；" +
                    "若此前可解密而当前突然失败，常见原因是内置 device.wvd 的设备证书已被 B 站吊销/封禁，" +
                    "请用 --wvd-path 更换新版 device.wvd 后重试，或使用 --key --kid 同时提供密钥。");
            }
        }

        Logger.Log($"密钥获取成功: kid={parsed.KidHex}, key 长度={parsed.KeyHex.Length} hex 字符");

        var mp4decrypt = ResolveMp4DecryptPath(myOption);
        if (string.IsNullOrEmpty(mp4decrypt))
        {
            // 与取钥失败一致：用户显式请求解密但没有解密器，若只记录错误并 return，
            // 加密流会被当成功产物交付。抛异常让任务标记失败。
            throw new InvalidOperationException(
                "未找到 mp4decrypt（Bento4 的解密工具），无法解密 DRM 内容。" +
                "请从 https://github.com/axiomatic-systems/Bento4/releases 下载并放入 PATH 或程序目录，" +
                "或用 --mp4decrypt-path 指定路径。");
        }

        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            Logger.Log("解密视频流...");
            var tmpVideo = videoPath + ".dec";
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, videoPath, tmpVideo, token);
            if (File.Exists(tmpVideo) && new FileInfo(tmpVideo).Length > 0)
            {
                File.Delete(videoPath);
                File.Move(tmpVideo, videoPath);
                Logger.Log("视频解密完成");
            }
        }

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            Logger.Log("解密音频流...");
            var tmpAudio = audioPath + ".dec";
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, audioPath, tmpAudio, token);
            if (File.Exists(tmpAudio) && new FileInfo(tmpAudio).Length > 0)
            {
                File.Delete(audioPath);
                File.Move(tmpAudio, audioPath);
                Logger.Log("音频解密完成");
            }
        }
    }

    private static async Task RunDecryptAsync(string mp4decrypt, string kid, string key, string input, string output, CancellationToken token = default)
    {
        // Write key to a temp file to avoid exposing it on the command line
        // (visible via ps aux / /proc/<pid>/cmdline to other local users)
        var keyFile = Path.GetTempFileName();
        var keyLine = $"{kid}:{key}";
        try
        {
            await File.WriteAllTextAsync(keyFile, keyLine, token);

            var psi = new ProcessStartInfo
            {
                FileName = mp4decrypt,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--key-file");
            psi.ArgumentList.Add(keyFile);
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add(output);

            using var proc = StartProcessSafe(psi, "mp4decrypt");
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                // 解密无超时兜底会让进程无限挂起：用混流超时配置作上限（与外部进程执行器一致）。
                // 达到超时同样 Kill 进程树并抛错，不留下孤儿 mp4decrypt。
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, Core.Config.Current.MuxerTimeoutMinutes)));
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 用户取消或超时：进程仍在运行，必须 Kill 掉，避免留下孤儿 mp4decrypt。
                try { proc.Kill(true); } catch { /* 进程可能已自行退出 */ }
                // Kill 后 stderr 管道断裂，ReadToEndAsync 会结束；带超时兜底地等待并观察
                // stderrTask，避免它成为未观察的 faulted Task（旧实现直接 throw 跳过等待，
                // Kill 产生的 IOException 会在终结器/后续 GC 时以 UnobservedTaskException 泄漏）。
                // 清理路径的任何异常都不应掩盖主路径的取消异常，一律忽略。
                try { await stderrTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                throw;
            }

            if (proc.ExitCode != 0)
            {
                var err = await stderrTask;
                try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
                throw new InvalidOperationException($"mp4decrypt 解密失败 (code={proc.ExitCode}): {err}");
            }
            // 进程退出 0 但未产出有效文件：静默忽略会让调用方保留原加密文件、
            // 任务却继续"解密成功"。这里把缺失输出当作失败抛出。
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                throw new InvalidOperationException("mp4decrypt 退出码为 0 但未产出有效的解密文件");
            }
        }
        finally
        {
            // Securely delete the temp key file
            try
            {
                if (File.Exists(keyFile))
                {
                    // Overwrite before delete to prevent recovery。覆写长度必须与写入载荷一致
                    //（第 13 轮 Info②）：kid:key 行为 32+1+32=65 字节，固定写 64 个 NUL 时
                    // FileMode.Create 截断后最后 1 个字符仍留在盘上，"安全覆写"名不副实。
                    await File.WriteAllTextAsync(keyFile, new string('\0', keyLine.Length));
                    File.Delete(keyFile);
                }
            }
            catch (Exception ex) when (ExceptionPolicies.IsBestEffortFailure(ex)) { /* best effort */ }
        }
    }

}
