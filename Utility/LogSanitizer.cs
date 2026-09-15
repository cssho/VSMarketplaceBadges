using System.Text;

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

            var buffer = new StringBuilder(System.Math.Min(value.Length, MaxLength));
            foreach (var c in value)
            {
                if (buffer.Length >= MaxLength)
                    return buffer.Append('…').ToString();
                if (char.IsControl(c))
                    continue;
                buffer.Append(c);
            }
            return buffer.ToString();
        }
    }
}
