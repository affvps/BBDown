using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core.DRM;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
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

        var decryptor = new DrmMediaDecryptor(new SystemProcessRunner());
        int timeoutMs = checked(Math.Max(1, Core.Config.Current.MuxerTimeoutMinutes) * 60 * 1000);
        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            Logger.Log("解密视频流...");
            await decryptor.DecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, videoPath, timeoutMs, token);
            Logger.Log("视频解密完成");
        }

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            Logger.Log("解密音频流...");
            await decryptor.DecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, audioPath, timeoutMs, token);
            Logger.Log("音频解密完成");
        }
    }

    /// <summary>
    /// 构造 mp4decrypt 的命令行参数（纯函数，供单测钉住 CLI 契约）。Bento4 的 mp4decrypt
    /// **只有** <c>--key &lt;id&gt;:&lt;k&gt;</c> 这一个传密钥的选项（可重复），没有 <c>--key-file</c>：
    /// 旧实现把密钥写进临时文件再用 <c>--key-file</c> 传入，mp4decrypt 会把输入文件当作多余参数
    /// 直接拒绝（<c>ERROR: unexpected argument (&lt;input&gt;)</c>）——DRM 解密从未成功过。
    /// </summary>
    internal static IReadOnlyList<string> BuildDecryptArguments(string kid, string key, string input, string output)
        => ["--key", $"{kid}:{key}", input, output];

}
