using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace VSMarketplaceBadges.Entity
{
    public class BadgeRequest2
    {
        //[FromRoute]
        public BadgeType BadgeType { get; set; }

        //[FromRoute]
        public string ItemName { get; set; }

        //[FromRoute]
        public ImageExt ImageExt { get; set; } = ImageExt.Svg;
    }
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

        [FromQuery]
        public string Subject { get; set; }

        [FromQuery]
        public string Color { get; set; } = "brightgreen";

        public MediaTypeHeaderValue ContentType => extCache[ImageExt];

        private static readonly Dictionary<ImageExt, MediaTypeHeaderValue> extCache
            = new Dictionary<ImageExt, MediaTypeHeaderValue>{
                { ImageExt.Svg, new MediaTypeHeaderValue("image/svg+xml") },
                { ImageExt.Png, new MediaTypeHeaderValue("image/png") }
            };
    }
}