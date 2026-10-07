using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using System.Linq;
using BBDown.Core;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
namespace BBDown;

internal partial class Program
{
    /// <summary>
    /// 组装轨道展示行：字段为空时整段不显示（等价于历史 `.Replace("[] ", "")` 写法，
    /// 但不会误伤内容本身含 "[] " 的字段）。<paramref name="prefix"/> 形如 "0. " 或 "[视频] "。
    /// internal 供 TrackLineFormatTests 直接验证与旧写法的等价性（I15 收敛点）。
    /// </summary>
    internal static string BuildTrackLine(string prefix, params string?[] fields)
        => prefix + string.Join(" ", fields.Where(f => !string.IsNullOrEmpty(f)).Select(f => $"[{f}]"));

    /// <summary>
    /// 按码率估算时长对应的字节数：Entity.bandwidth 的单位是 kbps
    /// （Parser 取接口的 bps 值 / 1000），故 bytes = 秒 × kbps × 1024 / 8。
    /// 只在接口未给出 size 时用作展示估算（音频轨恒走此路）。
    /// internal 供 TrackLineFormatTests 直接验证算式（I15 收敛点）。
    /// </summary>
    internal static long EstimatedBytes(long bandwidthKbps, int seconds)
        => seconds * bandwidthKbps * 1024 / 8;

    private static void PrintAllTracksInfo(ParsedResult parsedResult, int pageDur, bool onlyShowInfo)
    {
        if (parsedResult.BackgroundAudioTracks.Any() && parsedResult.RoleAudioList.Any())
        {
            Logger.Log($"共计{parsedResult.BackgroundAudioTracks.Count}条背景音频流.");
            int index = 0;
            foreach (var a in parsedResult.BackgroundAudioTracks)
            {
                int pDur = pageDur == 0 ? a.dur : pageDur;
                Logger.LogColor(BuildTrackLine($"{index++}. ", a.codecs, $"{a.bandwidth} kbps",
                    $"~{BBDownUtil.FormatFileSize(EstimatedBytes(a.bandwidth, pDur))}"), false);
            }
            var firstRoleAudio = parsedResult.RoleAudioList[0].audio;
            if (firstRoleAudio != null && firstRoleAudio.Any())
            {
                Logger.Log($"共计{parsedResult.RoleAudioList.Count}条配音, 每条包含{firstRoleAudio.Count}条配音流.");
                index = 0;
                foreach (var a in firstRoleAudio)
                {
                    int pDur = pageDur == 0 ? a.dur : pageDur;
                    Logger.LogColor(BuildTrackLine($"{index++}. ", a.codecs, $"{a.bandwidth} kbps",
                        $"~{BBDownUtil.FormatFileSize(EstimatedBytes(a.bandwidth, pDur))}"), false);
                }
            }
        }
        //展示所有的音视频流信息
        if (parsedResult.VideoTracks.Any())
        {
            Logger.Log($"共计{parsedResult.VideoTracks.Count}条视频流.");
            int index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                int pDur = pageDur == 0 ? v.dur : pageDur;
                var size = v.size > 0 ? v.size : EstimatedBytes(v.bandwidth, pDur);
                Logger.LogColor(BuildTrackLine($"{index++}. ", v.dfn, v.res, v.codecs, v.fps,
                    $"{v.bandwidth} kbps", $"~{BBDownUtil.FormatFileSize(size)}"), false);
                if (onlyShowInfo) Console.WriteLine(v.baseUrl);
            }
        }
        if (parsedResult.AudioTracks.Any())
        {
            Logger.Log($"共计{parsedResult.AudioTracks.Count}条音频流.");
            int index = 0;
            foreach (var a in parsedResult.AudioTracks)
            {
                int pDur = pageDur == 0 ? a.dur : pageDur;
                Logger.LogColor(BuildTrackLine($"{index++}. ", a.codecs, $"{a.bandwidth} kbps",
                    $"~{BBDownUtil.FormatFileSize(EstimatedBytes(a.bandwidth, pDur))}"), false);
                if (onlyShowInfo) Console.WriteLine(a.baseUrl);
            }
        }
    }

    private static void PrintSelectedTrackInfo(Video? selectedVideo, Audio? selectedAudio, int pageDur)
    {
        if (selectedVideo != null)
        {
            int pDur = pageDur == 0 ? selectedVideo.dur : pageDur;
            var size = selectedVideo.size > 0 ? selectedVideo.size : EstimatedBytes(selectedVideo.bandwidth, pDur);
            Logger.LogColor(BuildTrackLine("[视频] ", selectedVideo.dfn, selectedVideo.res, selectedVideo.codecs,
                selectedVideo.fps, $"{selectedVideo.bandwidth} kbps", $"~{BBDownUtil.FormatFileSize(size)}"), false);
        }
        if (selectedAudio != null)
        {
            int pDur = pageDur == 0 ? selectedAudio.dur : pageDur;
            Logger.LogColor(BuildTrackLine("[音频] ", selectedAudio.codecs, $"{selectedAudio.bandwidth} kbps",
                $"~{BBDownUtil.FormatFileSize(EstimatedBytes(selectedAudio.bandwidth, pDur))}"), false);
        }
    }

    /// <summary>读一行输入并解析为轨道序号；非法输入按 0（首条轨道）处理，不抛异常。</summary>
    private static int ReadIntSafe()
    {
        if (!int.TryParse(Console.ReadLine(), out var val))
            return 0;
        return val;
    }

    /// <summary>
    /// 引导用户手动选择轨道：依次提示输入视频/音频流序号，非法或越界回落首条。
    /// </summary>
    /// <param name="parsedResult">已解析出的轨道集合。</param>
    /// <param name="vIndex">输入并回写的视频轨下标。</param>
    /// <param name="aIndex">输入并回写的音频轨下标。</param>
    private static void SelectTrackManually(ParsedResult parsedResult, ref int vIndex, ref int aIndex)
    {
        if (parsedResult.VideoTracks.Any())
        {
            Logger.Log("请选择一条视频流(输入序号): ", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            vIndex = ReadIntSafe();
            // 合法下标是 0..Count-1；用 > 会放过 vIndex==Count，下游 ElementAtOrDefault
            // 取到 null 静默跳过该轨道，产出缺流文件。必须用 >=。
            if (vIndex >= parsedResult.VideoTracks.Count || vIndex < 0) vIndex = 0;
            Console.ResetColor();
        }
        if (parsedResult.AudioTracks.Any())
        {
            Logger.Log("请选择一条音频流(输入序号): ", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            aIndex = ReadIntSafe();
            if (aIndex >= parsedResult.AudioTracks.Count || aIndex < 0) aIndex = 0;
            Console.ResetColor();
        }
    }

    /// <summary>下载单条轨道（视频/音频/配音/背景音共用此路径，不需要轨道类型参数）。</summary>
    private static async Task DownloadTrackAsync(string url, string destPath, BBDownDownloadUtil.DownloadConfig downloadConfig, CancellationToken token = default)
    {
        if (downloadConfig.MultiThread && !url.Contains("-cmcc-"))
        {
            // 下载→合并→清理在目标路径的独占锁内完成（MultiThreadDownloadAndMergeAsync），
            // 调用方不再在锁外合并/删除分片：相同目标路径的第二个任务会等第一个任务
            // 完全结束后再进入，避免复用/误删上一个任务的分片或读到半截成品。
            await BBDownDownloadUtil.MultiThreadDownloadAndMergeAsync(url, destPath, downloadConfig, token);
        }
        else
        {
            if (downloadConfig.MultiThread && url.Contains("-cmcc-"))
            {
                Logger.LogWarn("检测到cmcc域名cdn, 已经禁用多线程");
                downloadConfig.ForceHttp = false;
            }
            await BBDownDownloadUtil.DownloadFileAsync(url, destPath, downloadConfig, token);
        }
    }
}
