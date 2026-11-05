using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    // The diagnostics' messages in Chinese and Japanese, by code, with the English ones' {0}, {1}.
    // TjaParser.Parse's exceptions and TaikoChart.Warnings stay in English.
    static class TjaMessages
    {
        static readonly Dictionary<string, (string Zh, string Ja)> Messages = new Dictionary<string, (string, string)>
        {
            ["TJA1001"] = ("TJA 谱面是空的。", "TJA 譜面が空です。"),
            ["TJA1002"] = ("{1} 侧没有 {0} 难度的块（#START）。", "{1} 側に {0} の譜面（#START）がありません。"),
            ["TJA1003"] = ("读不出数字：{0}", "数値を読めません：{0}"),
            ["TJA1004"] = ("BPM 必须是正的有限数。", "BPM は正の有限の数でなければなりません。"),
            ["TJA1005"] = ("[ 没有闭合：{0}", "[ が閉じていません：{0}"),
            ["TJA1006"] = ("[ 前面没有音符：{0}", "[ の前に音符がありません：{0}"),
            ["TJA1007"] = ("分歧边界前的小节缺少逗号。", "分岐の境目の前の小節にカンマがありません。"),
            ["TJA1008"] = ("不支持的音符：{0}", "対応していない音符：{0}"),
            ["TJA1009"] = ("不是气球的音符上写了打数：{0}", "風船でない音符に打数があります：{0}"),
            ["TJA1010"] = ("气球打数无效：{0}", "風船の打数が正しくありません：{0}"),
            ["TJA1011"] = ("#BPMCHANGE 的值无效。", "#BPMCHANGE の値が正しくありません。"),
            ["TJA1012"] = ("#MEASURE 应写作 a/b：{0}", "#MEASURE は a/b で書きます：{0}"),
            ["TJA1013"] = ("#MEASURE 的值无效。", "#MEASURE の値が正しくありません。"),
            ["TJA1014"] = ("#BRANCHEND 前没有 #BRANCHSTART。", "#BRANCHSTART のない #BRANCHEND です。"),
            ["TJA1015"] = ("路线标记（#N/#E/#M）不在分歧里。", "分岐の外にルート指定（#N/#E/#M）があります。"),
            ["TJA1016"] = ("分歧路线写了两次：{0}", "分岐ルートが二度書かれています：{0}"),
            ["TJA2001"] = ("缺少 #END", "#END がありません"),
            ["TJA2002"] = ("读不出的气球打数列表：{0}", "読めない風船の打数：{0}"),
            ["TJA2003"] = ("最后一个小节没有逗号", "最後の小節にカンマがありません"),
            ["TJA2004"] = ("未结束的连打由 #END 结束", "終わっていない連打を #END で終えました"),
            ["TJA2005"] = ("各路线共用的连打结束时刻不同，保留第一个（第 {0} 小节）", "ルート共通の連打の終わりが異なります。最初のものを使います（{0} 小節目）"),
            ["TJA2006"] = ("分歧 {0} 没有 #N：普通路线为空", "分岐 {0} に #N がありません：普通ルートは空です"),
            ["TJA2007"] = ("读不出的分歧条件：#BRANCHSTART {0}", "読めない分岐条件：#BRANCHSTART {0}"),
            ["TJA2008"] = ("#BRANCHSTART 与第一个 #N/#E/#M 之间的音符是共通的，会与各路线重叠（第 {0} 小节）", "#BRANCHSTART と最初の #N/#E/#M の間の音符は共通で、各ルートと重なります（{0} 小節目）"),
            ["TJA2009"] = ("未知指令：{0}", "不明な命令：{0}"),
            ["TJA2010"] = ("参数读不出，已忽略：{0}", "引数を読めないため無視しました：{0}"),
            ["TJA2011"] = ("路线中的 #LANE_ALPHA 在分歧 {0} 判定之前开始：{1}", "ルート内の #LANE_ALPHA が分岐 {0} の判定より前に始まります：{1}"),
            ["TJA2012"] = ("没有可结束的连打，8 当作空白（第 {0} 小节）", "終える連打がない 8 は空白として扱います（{0} 小節目）"),
            ["TJA2013"] = ("连打中的连打开头当作空白（第 {0} 小节）", "連打中の連打の始まりは空白として扱います（{0} 小節目）"),
            ["TJA2014"] = ("未结束的连打被种类 {0} 的音符结束（第 {1} 小节）", "終わっていない連打を種類 {0} の音符で終えました（{1} 小節目）"),
            ["TJA2015"] = ("未使用的音符 E 当作空白（第 {0} 小节）", "未使用の音符 E は空白として扱います（{0} 小節目）"),
            ["TJA2016"] = ("{0} 里没有这个气球的打数，按 5 打（第 {1} 小节）", "{0} にこの風船の打数がないため 5 打とします（{1} 小節目）"),
            ["TJA3001"] = ("不支持的 COURSE「{0}」：整个文件会被拒收。", "対応していない COURSE「{0}」：ファイル全体が受け付けられません。"),
            ["TJA3002"] = ("{0}/{1} 写了两次：整个文件会被拒收。", "{0}/{1} が二度書かれています：ファイル全体が受け付けられません。"),
            ["TJA3003"] = ("#START 后只能是 P1 或 P2，不能是「{0}」：整个文件会被拒收。", "#START の後は P1 か P2 で、「{0}」は使えません：ファイル全体が受け付けられません。"),
            ["TJA3004"] = ("LEVEL 为 {0}：不在 1 到 10 之间。", "LEVEL が {0} です：1〜10 ではありません。"),
            ["TJA3005"] = ("是不是要写 {0}？", "{0} のことですか？"),
            ["TJA3006"] = ("这个小节 {0} 格，即 1/{1} 音符：不是 2 与 3 的组合。", "この小節は {0} 個で 1/{1} 音符になります：2 と 3 の組み合わせではありません。"),
            ["TJA3007"] = ("分歧 {0} 的各路线结束时刻不同（{1}）。", "分岐 {0} の各ルートの終わる時刻が違います（{1}）。"),
        };

        // "zh…" and "ja…" (as LSP's locale gives them, "zh-cn", "ja"); anything else is English.
        public static string Format(TjaDiagnosticKind kind, string language, object[] args)
        {
            language = (language ?? "").ToLowerInvariant();
            bool zh = language.StartsWith("zh", StringComparison.Ordinal), ja = language.StartsWith("ja", StringComparison.Ordinal);
            if ((!zh && !ja) || !Messages.TryGetValue(kind.Code, out var texts)) return kind.Message(args);
            // A missing LEVEL is said, not shown as "missing".
            if (kind == TjaDiagnosticKind.LevelOutOfRange && args.Length > 0 && (string)args[0] == "missing") return zh ? "缺少 LEVEL。" : "LEVEL がありません。";
            return string.Format(CultureInfo.InvariantCulture, zh ? texts.Zh : texts.Ja, args);
        }

        internal static bool Has(string code, out string zh, out string ja)
        {
            bool found = Messages.TryGetValue(code, out var texts);
            zh = texts.Zh; ja = texts.Ja;
            return found;
        }
    }
}
