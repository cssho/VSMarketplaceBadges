using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace VSMarketplaceBadges.Entity
{
    public class BadgeRequest
    {
        private const string InstallsSubject = "installs";
        private const string DownloadsSubject = "downloads";
        private const string VersionSubject = "Visual%20Studio%20Marketplace";
        private const string VersionShortSubject = "VS%20Marketplace";
        private const string RatingSubject = "rating";
        private const string TrendingDailySubject = "trending--daily";
        private const string TrendingWeeklySubject = "trending--weekly";
        private const string TrendingMonthlySubject = "trending--monthly";
        private BadgeType badgeType;

        [FromRoute]
        public BadgeType BadgeType
        {
            get => badgeType; set
            {
                badgeType = value;
                Subject = value switch
                {
                    BadgeType.Version => VersionSubject,
                    BadgeType.VersionShort => VersionShortSubject,
                    BadgeType.Installs => InstallsSubject,
                    BadgeType.InstallsShort => InstallsSubject,
                    BadgeType.Downloads => DownloadsSubject,
                    BadgeType.DownloadsShort => DownloadsSubject,
                    BadgeType.Rating => RatingSubject,
                    BadgeType.RatingShort => RatingSubject,
                    BadgeType.RatingStar => RatingSubject,
                    BadgeType.TrendingDaily => TrendingDailySubject,
                    BadgeType.TrendingWeekly => TrendingWeeklySubject,
                    BadgeType.TrendingMonthly => TrendingMonthlySubject,
                    _ => ""
                };
            }
        }

        [FromRoute]
        public string ItemName { get; set; }

        [FromRoute]
        public ImageExt ImageExt { get; set; } = ImageExt.Svg;

        private string subject;
        private string color = "brightgreen";

        /// <summary>
        /// バッジの左側のラベル。<see cref="BadgeType"/> のセッターが既定値を入れ、
        /// クエリ文字列で上書きできる。
        /// </summary>
        /// <remarks>
        /// 値は <see cref="ShiledsIoService"/> で shields.io のパスに文字列連結されるため、
        /// 素通しすると <c>?subject=../../badge/X</c> でパスを書き換えられる。
        /// <see cref="Sanitize"/> で防ぐ。
        /// </remarks>
        [FromQuery]
        public string Subject
        {
            get => subject;
            set => subject = Sanitize(value);
        }

        /// <summary>バッジの色。<see cref="Subject"/> と同じ理由でサニタイズする。</summary>
        [FromQuery]
        public string Color
        {
            get => color;
            set => color = Sanitize(value);
        }

        /// <summary>
        /// パスの構造を変えられる文字と制御文字を取り除き、長さを上限で切る。
        /// </summary>
        /// <remarks>
        /// <c>/</c> を落とせばセグメントが増えないので <c>..</c> も traversal にならない。
        /// <c>?</c> と <c>#</c> はクエリ・フラグメントの境界を勝手に作らせないため。
        /// 長さ上限は、無意味に長い URL で上流とキャッシュキーを膨らませないための保険。
        ///
        /// ここでは URL エンコードはしない。<see cref="BadgeType"/> のセッターが入れる既定値が
        /// エンコード済みのリテラル (例: Visual%20Studio%20Marketplace) なので、二重エンコードになる。
        /// </remarks>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var buffer = new StringBuilder(Math.Min(value.Length, MaxValueLength));
            foreach (var c in value)
            {
                if (buffer.Length >= MaxValueLength)
                    break;
                if (c == '/' || c == '\\' || c == '?' || c == '#' || char.IsControl(c))
                    continue;
                buffer.Append(c);
            }
            return buffer.ToString();
        }

        private const int MaxValueLength = 128;

        public MediaTypeHeaderValue ContentType => extCache[ImageExt];

        private static readonly Dictionary<ImageExt, MediaTypeHeaderValue> extCache
            = new Dictionary<ImageExt, MediaTypeHeaderValue>{
                { ImageExt.Svg, new MediaTypeHeaderValue("image/svg+xml") },
                { ImageExt.Png, new MediaTypeHeaderValue("image/png") }
            };
    }
}
