using System;
using System.Collections.Generic;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    public class BadgeValueConverterTests
    {
        private static VSMarketplaceItem Item(
            string version = "1.0.0",
            double averageRating = 0,
            double ratingCount = 0,
            double trendingDaily = 0,
            double trendingWeekly = 0,
            double trendingMonthly = 0,
            double install = 0,
            double updateCount = 0,
            double migratedInstallCount = 0)
            => new VSMarketplaceItem(new VSMarketplaceItemRaw
            {
                Versions = new[] { new VSMarketplaceVersion { Version = version } },
                Statistics = new[]
                {
                    new VSMarketplaceStatistics { StatisticName = "averagerating", Value = averageRating },
                    new VSMarketplaceStatistics { StatisticName = "ratingcount", Value = ratingCount },
                    new VSMarketplaceStatistics { StatisticName = "trendingdaily", Value = trendingDaily },
                    new VSMarketplaceStatistics { StatisticName = "trendingweekly", Value = trendingWeekly },
                    new VSMarketplaceStatistics { StatisticName = "trendingmonthly", Value = trendingMonthly },
                    new VSMarketplaceStatistics { StatisticName = "install", Value = install },
                    new VSMarketplaceStatistics { StatisticName = "updateCount", Value = updateCount },
                    new VSMarketplaceStatistics { StatisticName = "migratedInstallCount", Value = migratedInstallCount },
                }
            });

        [Theory]
        [InlineData(BadgeType.Version)]
        [InlineData(BadgeType.VersionShort)]
        public void Version_IsPrefixedWithV(BadgeType type)
            => Assert.Equal("v2.3.4", Item(version: "2.3.4").ToBadgeValue(type));

        // Versions are compared as strings, not as semantic versions, so 1.10.0 sorts
        // below 1.9.0. This test pins the current behaviour; changing it is a product
        // decision, not a refactor.
        [Fact]
        public void Version_PicksMaxByOrdinalStringComparison()
        {
            var raw = new VSMarketplaceItemRaw
            {
                Versions = new[]
                {
                    new VSMarketplaceVersion { Version = "1.9.0" },
                    new VSMarketplaceVersion { Version = "1.10.0" },
                },
                Statistics = Array.Empty<VSMarketplaceStatistics>()
            };
            Assert.Equal("v1.9.0", new VSMarketplaceItem(raw).ToBadgeValue(BadgeType.Version));
        }

        // Installs excludes updateCount; downloads includes it.
        [Fact]
        public void Installs_SumsInstallAndMigrated()
            => Assert.Equal("150", Item(install: 100, updateCount: 40, migratedInstallCount: 50)
                .ToBadgeValue(BadgeType.Installs));

        [Fact]
        public void Downloads_AlsoIncludesUpdateCount()
            => Assert.Equal("190", Item(install: 100, updateCount: 40, migratedInstallCount: 50)
                .ToBadgeValue(BadgeType.Downloads));

        [Theory]
        [InlineData(0, "0")]
        [InlineData(999, "999")]
        [InlineData(1000, "1K")]
        [InlineData(1500, "1.5K")]
        [InlineData(1_500_000, "1.5M")]
        [InlineData(2_000_000_000, "2G")]
        public void InstallsShort_AbbreviatesWithUnitSuffix(long install, string expected)
            => Assert.Equal(expected, Item(install: install).ToBadgeValue(BadgeType.InstallsShort));

        [Fact]
        public void DownloadsShort_AbbreviatesTheDownloadTotal()
            => Assert.Equal("1.5K", Item(install: 1000, updateCount: 500).ToBadgeValue(BadgeType.DownloadsShort));

        [Fact]
        public void Rating_IsRoundedToTwoDecimalsWithCount()
            => Assert.Equal("average: 4.33/5 (12 ratings)",
                Item(averageRating: 4.333333, ratingCount: 12).ToBadgeValue(BadgeType.Rating));

        [Fact]
        public void RatingShort_DropsTheLabels()
            => Assert.Equal("4.33/5 (12)",
                Item(averageRating: 4.333333, ratingCount: 12).ToBadgeValue(BadgeType.RatingShort));

        [Theory]
        [InlineData(0, "☆☆☆☆☆")]
        [InlineData(5, "★★★★★")]
        [InlineData(4.5, "★★★★½")]
        [InlineData(3.9, "★★★★☆")]      // fraction >= 7/8 rounds up to a full star
        [InlineData(3.7, "★★★¾☆")]
        [InlineData(2.2, "★★¼☆☆")]
        [InlineData(1.05, "★☆☆☆☆")]     // fraction < 1/8 contributes nothing
        public void RatingStar_RendersFractionalStars(double average, string expected)
            => Assert.Equal(expected, Item(averageRating: average).ToBadgeValue(BadgeType.RatingStar));

        [Theory]
        [InlineData(BadgeType.TrendingDaily)]
        [InlineData(BadgeType.TrendingWeekly)]
        [InlineData(BadgeType.TrendingMonthly)]
        public void Trending_IsRoundedToTwoDecimals(BadgeType type)
            => Assert.Equal("1.24", Item(trendingDaily: 1.2356, trendingWeekly: 1.2356, trendingMonthly: 1.2356)
                .ToBadgeValue(type));

        [Fact]
        public void UnknownBadgeType_Throws()
            => Assert.Throws<ArgumentException>(() => Item().ToBadgeValue(BadgeType.Unknown));
    }
}
