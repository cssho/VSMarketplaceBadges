using System.ComponentModel;
using System.Runtime.Serialization;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Entity
{
    [TypeConverter(typeof(CustomEnumConverter<BadgeType>))]
    public enum BadgeType
    {
        Unknown,
        [EnumMember(Value="version")]
        Version,
        [EnumMember(Value="version-short")]
        VersionShort,
        [EnumMember(Value="installs")]
        Installs,
        [EnumMember(Value="installs-short")]
        InstallsShort,
        [EnumMember(Value="rating")]
        Rating,
        [EnumMember(Value="rating-short")]
        RatingShort,
        [EnumMember(Value="rating-star")]
        RatingStar,
        [EnumMember(Value="trending-daily")]
        TrendingDaily,
        [EnumMember(Value="trending-weekly")]
        TrendingWeekly,
        [EnumMember(Value="trending-monthly")]
        TrendingMonthly,
        [EnumMember(Value="downloads")]
        Downloads,
        [EnumMember(Value="downloads-short")]
        DownloadsShort
    }
}