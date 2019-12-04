using System;
using System.Linq;
using System.Runtime.Serialization;
using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Utility
{
    public static class BadgeValuConverterExtentions
    {
        private static readonly double[] FractionBoundaryValues = new[] { 7.0 / 8.0, 5.0 / 8.0, 3.0 / 8.0, 1.0 / 8.0 };
        public static string ToBadgeValue(this VSMarketplaceItem item, BadgeType type)
        {
            switch (type)
            {
                case BadgeType.Version:
                case BadgeType.VersionShort:
                    return item.Version;
                case BadgeType.Installs:
                    return item.InstallCount.ToString();
                case BadgeType.InstallsShort:
                    return ApplyUnit(item.InstallCount);
                case BadgeType.Rating:
                    return $"average: {Math.Round(item.AverageRating, 2)}/5 ({item.RatingCount } ratings)";
                case BadgeType.RatingShort:
                    return $"{Math.Round(item.AverageRating, 2)}/5 ({item.RatingCount })";
                case BadgeType.TrendingDaily:
                    return $"{Math.Round(item.TrendingDaily, 2)}";
                case BadgeType.TrendingMonthly:
                    return $"{Math.Round(item.TrendingMonthly, 2)}";
                case BadgeType.TrendingWeekly:
                    return $"{Math.Round(item.TrendingWeekly, 2)}";
                case BadgeType.RatingStar:
                    return LoadRatingStar(item.AverageRating);
                case BadgeType.Downloads:
                    return item.DownloadCount.ToString();
                case BadgeType.DownloadsShort:
                    return ApplyUnit(item.DownloadCount);
                default:
                    throw new ArgumentException();
            }
        }

        private static readonly string[] units = { "", "K", "M", "G", "T" };
        private static string ApplyUnit(double installs, int unitIdx = 0)
        {
            if (installs < 1000 || unitIdx == units.Length) return installs.ToString() + units[unitIdx];
            return ApplyUnit(Math.Round(installs / 1000, 2, MidpointRounding.AwayFromZero), unitIdx + 1);
        }

        private static string LoadRatingStar(double average)
        {
            if (average == 0) return "☆☆☆☆☆";
            var floored = Math.Floor(average);
            var fraction = average - floored;

            var stars = "";
            while (stars.Length < floored) stars += '★';

            stars += fraction >= FractionBoundaryValues[0] ? "★"
                : fraction >= FractionBoundaryValues[1] ? "¾"
                : fraction >= FractionBoundaryValues[2] ? "½"
                : fraction >= FractionBoundaryValues[3] ? "¼"
                : "";

            while (stars.Length < 5) stars += '☆';
            return stars;
        }

        public static string ToEnumMember(this Enum enumValue)
        {
            var type = enumValue.GetType();
            var info = type.GetField(enumValue.ToString());
            var da = (EnumMemberAttribute[])(info.GetCustomAttributes(typeof(EnumMemberAttribute), false));

            return da.FirstOrDefault()?.Value;
        }
    }
}