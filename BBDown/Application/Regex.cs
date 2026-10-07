using System;
using System.Text.RegularExpressions;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{

    [GeneratedRegex("://[^/]+:\\d+/")]
    private static partial Regex PcdnRegex();
    [GeneratedRegex("://[^/]*akamaized\\.net/")]
    private static partial Regex AkamRegex();
    [GeneratedRegex("://[^/]+/")]
    private static partial Regex UposRegex();
}
