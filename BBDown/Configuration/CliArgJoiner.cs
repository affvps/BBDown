namespace BBDown;

/// <summary>
/// argv 预处理：把"以 '-' 开头的选项值"并入它前面的选项，补齐 GNU getopt 语义。
///
/// 起因：Spectre.Console.Cli 的 tokenizer 对任何以 '-' 开头的 argv 一律判为选项
/// （见其 <c>CommandTreeTokenizer.ScanOptions</c>），于是
/// <c>BBDown sub add mid:19231317 --name "-尾野"</c> 会报
/// <c>Option 'name' is defined but no value has been provided.</c>——
/// 用户看到的是"参数解析有问题"，而 getopt 系列工具会把下一个 argv 直接当作选项值。
/// 值以 '-' 开头的场景并不罕见（订阅显示名、以 '-' 开头的路径/正则/文件模式等）。
///
/// 合并后的写法 <c>--name=-尾野</c>（Spectre 同样接受 <c>--name:-尾野</c>）本来就是可用的，
/// 这里只是让空格写法也生效，不引入新的语法。
///
/// 仅在两处都满足时合并，避免误吞相邻的合法选项：
/// <list type="number">
/// <item>当前 token 是已知选项、且**会消耗值**（bool 开关后面跟 '-' 开头的 token 是笔误，
/// 例如 <c>--skip-mux --skip-subtitle</c>，合并会破坏这条原本合法的命令行）；</item>
/// <item>当前 token 不含 '=' / ':'（Spectre 视其为"值已在内"的分隔符）；</item>
/// <item>下一个 token 以 '-' 开头，且**不是已知选项名**——是则说明用户漏写了值，
/// 保留 Spectre 原本的 "no value has been provided" 报错（与配置文件合并处
/// <c>BBDownConfigParser</c> 判定"值是否以 - 开头"的规则同源）。</item>
/// </list>
/// </summary>
internal static class CliArgJoiner
{
    /// <summary>
    /// 返回合并后的 argv；无需合并时原样返回入参（同一实例，避免无谓分配）。
    /// </summary>
    internal static string[] JoinDashLeadingOptionValues(string[] args)
    {
        var result = new List<string>(args.Length);
        var joined = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (i + 1 < args.Length && CanJoin(token, args[i + 1]))
            {
                // 消费者（Spectre）把 '=' 之前视为选项名、之后视为值，值里再含 '=' 无妨。
                result.Add($"{token}={args[i + 1]}");
                i++;
                joined = true;
                continue;
            }
            result.Add(token);
        }

        return joined ? result.ToArray() : args;
    }

    private static bool CanJoin(string token, string next)
    {
        // 必须是"选项名本体"：以 '-' 开头、且尚未自带值（自带值的 token 不再吃下一个 argv）
        if (token.Length < 2 || token[0] != '-') return false;
        if (token.Contains('=') || token.Contains(':')) return false;
        if (!CliOptionIndex.TakesValue(token)) return false;

        // 下一个 token 必须是"看起来像值"的东西：以 '-' 开头且不是已知选项名。
        // 空串是合法值（Spectre 按 Kind.String 处理），交给原逻辑即可。
        return next.StartsWith('-') && !CliOptionIndex.IsKnownOption(next);
    }
}
