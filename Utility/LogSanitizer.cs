using System.Text.RegularExpressions;

namespace VSMarketplaceBadges.Utility
{
    /// <summary>
    /// 利用者入力をログに出す前に整える。
    /// </summary>
    public static class LogSanitizer
    {
        /// <summary>ログに残す 1 値あたりの最大長。</summary>
        public const int MaxLength = 128;

        /// <summary>
        /// 制御文字 (Unicode カテゴリ Cc)。<c>char.IsControl</c> と同じ範囲。
        /// </summary>
        /// <remarks>
        /// 自前のループではなく <see cref="Regex.Replace(string, string)"/> を使うのは、
        /// CodeQL の cs/log-forging がサニタイザとして認識するのが
        /// String.Replace / String.Remove / String.ReplaceLineEndings / Regex.Replace に限られ、
        /// StringBuilder で 1 文字ずつ弾く実装は barrier と見なされないため。
        /// 挙動は変わらないが、こう書くことでアラートを抑制ではなく解消できる。
        /// </remarks>
        private static readonly Regex ControlCharacters = new Regex(@"\p{Cc}");

        /// <summary>
        /// 制御文字を落とし、長さを <see cref="MaxLength"/> で切り詰める。
        /// 切り詰めた場合は末尾に省略記号を付ける。
        /// </summary>
        /// <remarks>
        /// 構造化ログ (`{itemName}` のプレースホルダ) を使っているため、改行を混ぜて偽のログ行を
        /// 捏造する古典的な log forging は元々成立しない。それでもここで整えるのは、
        /// ログを後段で機械処理するときにフィールド値へ制御文字や無制限長の文字列が
        /// 紛れ込まないようにするため。CloudWatch の取り込み量を無駄に膨らませない意味もある。
        /// </remarks>
        public static string ForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var stripped = ControlCharacters.Replace(value, string.Empty);

            return stripped.Length <= MaxLength
                ? stripped
                : stripped.Substring(0, MaxLength) + "…";
        }
    }
}
