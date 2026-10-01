using System.Security.Cryptography;
using System.Text;

namespace BBDown.Core.Util;

/// <summary>
/// B 站开放接口的 appkey 与签名盐常量，以及全库唯一的签名/时间戳实现（REVIEW_PLAN I3）。
/// 签名算法与常量值是风控面：盐值、拼接顺序、编码方式一字未改，仅把原先分散的
/// Parser.GetSign / BBDownUtil.GetSign 与 Parser.GetTimeStamp / BBDownUtil.GetTimeStamp
/// 两份实现收敛为一份，调用方按端点选择盐（TV/APP 用 <see cref="TvSignSalt"/>，
/// BiliPlus 镜像 Host 的 intl playurl 用 <see cref="BiliPlusSignSalt"/>）。
/// </summary>
public static class BiliApiKeys
{
    /// <summary>TV/APP 端点 appkey（tv playurltv、TV 登录 auth_code 轮询）。</summary>
    public const string TvAppKey = "4409e2ce8ffd12b8";

    /// <summary>BiliPlus 镜像 Host 的 intl playurl 端点 appkey。</summary>
    public const string BiliPlusAppKey = "7d089525d3611b1c";

    /// <summary>TV/APP 端点签名盐。</summary>
    public const string TvSignSalt = "59b43e04ad6965f34319062b478f83dd";

    /// <summary>BiliPlus intl playurl 端点签名盐。</summary>
    public const string BiliPlusSignSalt = "acd495b248ec528c2eed1e862d393126";

    /// <summary>参数串 + 盐的 MD5（小写十六进制），服务端按同一算法校验。</summary>
    public static string GetSign(string parameters, string salt)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(parameters + salt)));

    /// <summary>
    /// 签名时间戳：<paramref name="seconds"/> 为 true 返回秒，否则毫秒。
    /// 经服务器时钟偏移校准（ServerClock）：本地时钟偏差超 ~60s 时效窗口会让 wts/ts
    /// 被 B 站拒绝签名（虚拟机时钟不同步/未启用 NTP 的容器等）。offset=0 时与 UTC
    /// 当前时间等价，行为零回归。
    /// </summary>
    public static string GetTimeStamp(bool seconds)
    {
        DateTimeOffset ts = ServerClock.Now;
        return seconds ? ts.ToUnixTimeSeconds().ToString() : ts.ToUnixTimeMilliseconds().ToString();
    }
}
