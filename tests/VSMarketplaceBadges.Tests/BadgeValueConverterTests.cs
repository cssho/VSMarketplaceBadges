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

        // バージョンはセマンティックバージョニングの規則で比較されるため、1.10.0 が 1.9.0 より上になる。
        [Fact]
        public void Version_PicksMaxBySemanticVersioning()
        {
            Assert.Equal("v1.10.0", MaxVersionOf("1.9.0", "1.10.0"));
        }

        // 数値部が同じなら、プレリリースより正式版のほうが優先順位が高い。
        [Fact]
        public void Version_PrefersReleaseOverPreRelease()
        {
            Assert.Equal("v2.0.0", MaxVersionOf("2.0.0-rc.1", "2.0.0"));
        }

        // プレリリース識別子は左から順に比較し、数値識別子は英数字識別子より低い。
        [Theory]
        [InlineData("1.0.0-alpha", "1.0.0-alpha.1", "v1.0.0-alpha.1")]
        [InlineData("1.0.0-alpha.9", "1.0.0-alpha.10", "v1.0.0-alpha.10")]
        [InlineData("1.0.0-1", "1.0.0-alpha", "v1.0.0-alpha")]
        [InlineData("1.0.0-beta", "1.0.0-alpha", "v1.0.0-beta")]
        public void Version_ComparesPreReleaseIdentifiers(string first, string second, string expected)
            => Assert.Equal(expected, MaxVersionOf(first, second));

        // ビルドメタデータは優先順位に影響しないため、数値部とプレリリースが同じなら順序は変わらない。
        [Fact]
        public void Version_IgnoresBuildMetadata()
            => Assert.Equal("v1.0.1", MaxVersionOf("1.0.0+build.999", "1.0.1"));

        // 桁数が違う場合は不足分を 0 として比較する (Marketplace には 4 桁のバージョンもある)。
        [Fact]
        public void Version_TreatsMissingSegmentsAsZero()
            => Assert.Equal("v1.2.0.1", MaxVersionOf("1.2", "1.2.0.1"));

        // semver として解釈できないバージョンが混ざった場合は序数の文字列比較にフォールバックする。
        [Fact]
        public void Version_FallsBackToOrdinalComparisonForUnparsableVersions()
            => Assert.Equal("v2019.1", MaxVersionOf("2019.1", "1.10.0"));

        private static string MaxVersionOf(params string[] versions)
        {
            var raw = new VSMarketplaceItemRaw
            {
                Versions = Array.ConvertAll(versions, x => new VSMarketplaceVersion { Version = x }),
                Statistics = Array.Empty<VSMarketplaceStatistics>()
            };
            return new VSMarketplaceItem(raw).ToBadgeValue(BadgeType.Version);
        }

        // installs は updateCount を含まず、downloads は含む。
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
        [InlineData(3.9, "★★★★☆")]      // 小数部が 7/8 以上なら星 1 つ分に切り上げる
        [InlineData(3.7, "★★★¾☆")]
        [InlineData(2.2, "★★¼☆☆")]
        [InlineData(1.05, "★☆☆☆☆")]     // 小数部が 1/8 未満なら何も加算しない
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
