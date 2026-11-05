using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    // The editor features' words in Chinese and Japanese: the schema's summaries (hover, completion,
    // signature help) and the phrases of hovers, completions, code actions and block summaries.
    // Languages are named as TjaDiagnostic.MessageIn takes them: "zh…", "ja…", anything else English.
    static class TjaText
    {
        public enum Lang { En, Zh, Ja }

        public static Lang Of(string language)
        {
            language = (language ?? "").ToLowerInvariant();
            return language.StartsWith("zh", StringComparison.Ordinal) ? Lang.Zh
                : language.StartsWith("ja", StringComparison.Ordinal) ? Lang.Ja : Lang.En;
        }

        public static string Pick(string language, string en, string zh, string ja)
        {
            var lang = Of(language);
            return lang == Lang.Zh ? zh : lang == Lang.Ja ? ja : en;
        }

        public static string Format(string language, string en, string zh, string ja, params object[] args)
            => string.Format(CultureInfo.InvariantCulture, Pick(language, en, zh, ja), args);

        // ---- The schema's summaries, by keyword.

        static readonly Dictionary<string, (string Zh, string Ja)> Summaries = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["#START"] = ("开始一个谱面块；双人谱面用 P1 或 P2 指定侧别。", "譜面ブロックを始める。ダブル譜面では P1 か P2 で側を指定する。"),
            ["#END"] = ("结束谱面块。", "譜面ブロックを終える。"),
            ["#BPMCHANGE"] = ("从这里改变 BPM。", "ここから BPM を変える。"),
            ["#MEASURE"] = ("从下一格起改变拍号。", "次の位置から拍子を変える。"),
            ["#SCROLL"] = ("改变滚动速度；带 i 时是复数速度，虚部为纵向。", "スクロール速度を変える。i を付けると複素数で、虚部が縦方向。"),
            ["#DELAY"] = ("把位置后移若干秒，拍子不动。", "拍を動かさずに位置を秒数だけ進める。"),
            ["#GOGOSTART"] = ("从这里开始 GOGO 时间。", "ここからゴーゴータイム。"),
            ["#GOGOEND"] = ("在这里结束 GOGO 时间。", "ここでゴーゴータイムを終える。"),
            ["#BARLINEOFF"] = ("从这里起隐藏小节线。", "ここから小節線を隠す。"),
            ["#BARLINEON"] = ("从这里起显示小节线。", "ここから小節線を表示する。"),
            ["#SECTION"] = ("在这里重置分歧的计数。", "ここで分岐の判定数をリセットする。"),
            ["#BRANCHSTART"] = ("开始分歧：条件，以及玄人与达人的门槛。", "分岐を始める：条件と、玄人・達人のしきい値。"),
            ["#N"] = ("分歧的普通路线。", "分岐の普通ルート。"),
            ["#E"] = ("分歧的玄人路线。", "分岐の玄人ルート。"),
            ["#M"] = ("分歧的达人路线。", "分岐の達人ルート。"),
            ["#BRANCHEND"] = ("结束分歧：之后又是共通音符。", "分岐を終える：以降はまた共通の音符。"),
            ["#LEVELHOLD"] = ("固定路线：在路线里，选定之后固定；在别处，固定正在游玩的路线。", "ルートを固定する：ルート内では選ばれたあと、それ以外では演奏中のルート。"),
            ["#NMSCROLL"] = ("音符按自己的时刻、BPM 与 SCROLL 移动。", "音符が自分の時刻・BPM・SCROLL で動く。"),
            ["#BMSCROLL"] = ("音符按共用的拍子时钟移动，忽略 SCROLL。", "音符が共通の拍の時計で動き、SCROLL は無視する。"),
            ["#HBSCROLL"] = ("音符按共用的拍子时钟移动，再乘以 SCROLL。", "音符が共通の拍の時計で動き、SCROLL を掛ける。"),
            ["#JPOSSCROLL"] = ("在 seconds 秒内把判定点移动 move 像素（按 1280 宽计）；direction 为 0 时反向。", "seconds 秒かけて判定枠を move ピクセル（幅 1280 基準）動かす。direction が 0 なら逆向き。"),
            ["#SUDDEN"] = ("音符在击打前 show 秒出现，前 move 秒开始移动。", "音符が叩く show 秒前に現れ、move 秒前に動き出す。"),
            ["#LANE_ALPHA"] = ("在 ms 毫秒内把轨道背景变到 target% 的不透明度（负数：到此处为止）。", "ms ミリ秒かけてレーン背景を不透明度 target% にする（負の値：ここまでに）。"),
            ["TITLE"] = ("曲名。", "曲名。"),
            ["SUBTITLE"] = ("副标题；-- 在选曲中隐藏，++ 显示。", "サブタイトル。-- で選曲画面に出さず、++ で出す。"),
            ["BPM"] = ("开始时的 BPM。", "最初の BPM。"),
            ["OFFSET"] = ("从音频开头到谱面开头的秒数（负数：更晚）。", "音源の始まりから譜面の始まりまでの秒数（負の値：遅く）。"),
            ["COURSE"] = ("之后各块的难度。", "以降のブロックの難易度。"),
            ["LEVEL"] = ("这个难度的星级。", "その難易度の★の数。"),
            ["BALLOON"] = ("依次是每个气球的打数（普通路线的列表）。", "各風船の打数を順に（普通ルートのリスト）。"),
            ["BALLOONNOR"] = ("普通路线上每个气球的打数。", "普通ルートの各風船の打数。"),
            ["BALLOONEXP"] = ("玄人路线上每个气球的打数。", "玄人ルートの各風船の打数。"),
            ["BALLOONMAS"] = ("达人路线上每个气球的打数。", "達人ルートの各風船の打数。"),
            ["STYLE"] = ("Single 或 Double：这个难度是否为双人谱面。", "Single か Double：その難易度が 2 人用かどうか。"),
            ["GENRE"] = ("歌曲的分类。", "曲のジャンル。"),
            ["WAVE"] = ("音频文件。", "音源ファイル。"),
            ["BGMOVIE"] = ("背景视频文件。", "背景動画ファイル。"),
            ["MOVIEOFFSET"] = ("视频比音频晚开始的秒数。", "動画が音源より遅れて始まる秒数。"),
            ["DEMOSTART"] = ("选曲预览开始的位置。", "選曲画面の試聴の開始位置。"),
            ["MAKER"] = ("谱面作者。", "譜面の作者。"),
        };

        public static string Summary(TjaKeyword k, string language)
        {
            var lang = Of(language);
            if (lang == Lang.En) return k.Summary;
            string name = k.Name;
            if (Summaries.TryGetValue(name, out var texts)) return lang == Lang.Zh ? texts.Zh : texts.Ja;
            if (name.StartsWith("NOTESDESIGNER", StringComparison.Ordinal) && int.TryParse(name.Substring(13), out int course) && course >= 0 && course < SongInfo.CourseNames.Count)
                return Format(language, "", "{0} 难度的谱面作者。", "{0} の譜面の作者。", SongInfo.CourseNames[course]);
            if (name.StartsWith("SUBTITLE", StringComparison.Ordinal)) return Pick(language, k.Summary, "某种语言的副标题。", "ある言語でのサブタイトル。");
            if (name.StartsWith("TITLE", StringComparison.Ordinal)) return Pick(language, k.Summary, "某种语言的曲名。", "ある言語での曲名。");
            return k.Summary;
        }

        // ---- Notes and routes.

        public static string NoteName(NoteKind kind, string language)
        {
            switch (kind)
            {
                case NoteKind.Don: return Pick(language, "Don", "咚", "ドン");
                case NoteKind.Ka: return Pick(language, "Ka", "咔", "カッ");
                case NoteKind.BigDon: return Pick(language, "Big don", "大咚", "大ドン");
                case NoteKind.BigKa: return Pick(language, "Big ka", "大咔", "大カッ");
                case NoteKind.Roll: return Pick(language, "Drumroll", "连打", "連打");
                case NoteKind.BigRoll: return Pick(language, "Big drumroll", "大连打", "大連打");
                case NoteKind.Balloon: return Pick(language, "Balloon", "气球", "ふうせん");
                case NoteKind.Kusudama: return Pick(language, "Kusudama", "彩球", "くすだま");
                case NoteKind.Mine: return Pick(language, "Mine", "地雷", "地雷");
                case NoteKind.AdLib: return Pick(language, "Ad-Lib", "Ad-Lib", "アドリブ");
                case NoteKind.Kadon: return Pick(language, "Kadon", "咔咚", "カドン");
                default: return kind.ToString();
            }
        }

        public static string RouteName(BranchRoute route, string language)
        {
            switch (route)
            {
                case BranchRoute.Expert: return Pick(language, "Expert", "玄人", "玄人");
                case BranchRoute.Master: return Pick(language, "Master", "达人", "達人");
                default: return Pick(language, "Normal", "普通", "普通");
            }
        }

        // ---- Completion: what each note character and branch condition is.

        public static string NoteCharacter(string note, string language)
        {
            switch (note)
            {
                case "1": return NoteName(NoteKind.Don, language);
                case "2": return NoteName(NoteKind.Ka, language);
                case "3": return NoteName(NoteKind.BigDon, language);
                case "4": return NoteName(NoteKind.BigKa, language);
                case "5": return NoteName(NoteKind.Roll, language);
                case "6": return NoteName(NoteKind.BigRoll, language);
                case "7": return NoteName(NoteKind.Balloon, language);
                case "9": return NoteName(NoteKind.Kusudama, language);
                case "8": return Pick(language, "End of a roll", "连打的结尾", "連打の終わり");
                case "0": return Pick(language, "Blank", "空白", "空白");
                case "G": return Pick(language, "Kadon: don and ka together (OpenTaiko)", "咔咚：咚和咔同时打（OpenTaiko）", "カドン：ドンとカッを同時に（OpenTaiko）");
                case "C": return Pick(language, "Mine: not to be hit (OpenTaiko)", "地雷：不能打（OpenTaiko）", "地雷：叩いてはいけない（OpenTaiko）");
                case "F": return Pick(language, "Ad-Lib: a hidden bonus note (OpenTaiko)", "Ad-Lib：隐藏的加分音符（OpenTaiko）", "アドリブ：隠しボーナス音符（OpenTaiko）");
                case ",": return Pick(language, "End of the measure", "小节结束", "小節の終わり");
                default: return note;
            }
        }

        public static string Condition(string condition, string language)
        {
            switch (condition)
            {
                case "p": return Pick(language, "Accuracy (%)", "精准率（%）", "精度（%）");
                case "pp": return Pick(language, "Good judgements (%)", "良的比例（%）", "良の割合（%）");
                case "jp": return Pick(language, "Good judgements (count)", "良的个数", "良の数");
                case "d": return Pick(language, "Good judgements on big notes (count)", "大音符上良的个数", "大音符の良の数");
                case "jg": return Pick(language, "OK judgements (count)", "可的个数", "可の数");
                case "jb": return Pick(language, "Bad judgements (count)", "不可的个数", "不可の数");
                case "r": return Pick(language, "Drumroll hits", "连打数", "連打数");
                case "rb": return Pick(language, "Balloon hits", "气球打数", "風船の打数");
                case "s": return Pick(language, "Score", "分数", "スコア");
                default: return condition;
            }
        }

        public static string Stars(string language) => Pick(language, "Stars", "星级", "★の数");

        // English ordinals; Chinese and Japanese count plainly.
        public static string Ordinal(int n)
            => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
