using System.Collections.Generic;
using System.Linq;

namespace VSMarketplaceBadges.Entity
{
    public class VSMarketplaceItem
    {
        public VSMarketplaceItem(VSMarketplaceItemRaw raw)
        {
            Version = $"v{raw.Versions.Max(x => x.Version)}";
            foreach (var statistic in raw.Statistics)
            {
                switch (statistic.StatisticName)
                {
                    case "averagerating":
                        AverageRating = statistic.Value;
                        break;
                    case "ratingcount":
                        RatingCount = (int)statistic.Value;
                        break;
                    case "trendingdaily":
                        TrendingDaily = statistic.Value;
                        break;
                    case "trendingmonthly":
                        TrendingMonthly = statistic.Value;
                        break;
                    case "trendingweekly":
                        TrendingWeekly = statistic.Value;
                        break;
                    case "install":
                        Install = (long)statistic.Value;
                        break;
                    case "updateCount":
                        UpdateCount = (long)statistic.Value;
                        break;                        
                    case "migratedInstallCount":
                        MigratedInstallCount = (long)statistic.Value;
                        break;
                }
            }
        }
        public string Version { get; set; }
        public double AverageRating { get; set; } = 0;
        public int RatingCount { get; set; } = 0;
        public double TrendingDaily { get; set; } = 0;
        public double TrendingMonthly { get; set; } = 0;
        public double TrendingWeekly { get; set; } = 0;

        public long Install { get; set; }
        public long UpdateCount { get; set; }
        public long MigratedInstallCount { get; set; }

        public long DownloadCount => Install + UpdateCount + MigratedInstallCount;
        public long InstallCount => Install + MigratedInstallCount;
    }
    public class VSMarketplaceResponse
    {
        public IEnumerable<VSMarketplaceExtensions> Results { get; set; }
    }
    public class VSMarketplaceExtensions
    {
        public IEnumerable<VSMarketplaceItemRaw> Extensions { get; set; }
    }

    public class VSMarketplaceVersion
    {
        public string Version { get; set; }
    }
    public class VSMarketplaceStatistics
    {
        public string StatisticName { get; set; }
        public double Value { get; set; }
    }
    public class VSMarketplaceItemRaw
    {
        public IEnumerable<VSMarketplaceVersion> Versions { get; set; }
        public IEnumerable<VSMarketplaceStatistics> Statistics { get; set; }
    }
}