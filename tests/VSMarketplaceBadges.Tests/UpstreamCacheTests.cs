using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// 上流の応答がキャッシュされ、ヒット時は上流を呼ばずに同じ値を返すことを担保する。
    /// </summary>
    public class UpstreamCacheTests
    {
        private const string MarketplaceResponse = @"{
  ""results"": [{
    ""extensions"": [{
      ""versions"": [{ ""version"": ""1.10.0"" }, { ""version"": ""1.9.0"" }],
      ""statistics"": [
        { ""statisticName"": ""install"", ""value"": 1200 },
        { ""statisticName"": ""updateCount"", ""value"": 300 },
        { ""statisticName"": ""migratedInstallCount"", ""value"": 45 },
        { ""statisticName"": ""averagerating"", ""value"": 4.5 },
        { ""statisticName"": ""ratingcount"", ""value"": 12 },
        { ""statisticName"": ""trendingdaily"", ""value"": 1.25 },
        { ""statisticName"": ""trendingweekly"", ""value"": 2.5 },
        { ""statisticName"": ""trendingmonthly"", ""value"": 3.75 }
      ]
    }]
  }]
}";

        [Fact]
        public async Task Marketplace_はヒット時に上流を呼ばず同じ値を返す()
        {
            var handler = new CountingHandler(MarketplaceResponse);
            var marketplace = BuildServices(marketplaceHandler: handler).GetRequiredService<IVSMarketplaceService>();

            var first = await marketplace.LoadVsmItemDataFromApi("publisher.extension");
            var second = await marketplace.LoadVsmItemDataFromApi("publisher.extension");

            Assert.Equal(1, handler.Calls);
            Assert.Equal("v1.10.0", second.Version);
            Assert.Equal(first.Version, second.Version);
            Assert.Equal(first.AverageRating, second.AverageRating);
            Assert.Equal(first.RatingCount, second.RatingCount);
            Assert.Equal(first.TrendingDaily, second.TrendingDaily);
            Assert.Equal(first.TrendingWeekly, second.TrendingWeekly);
            Assert.Equal(first.TrendingMonthly, second.TrendingMonthly);
            Assert.Equal(first.Install, second.Install);
            Assert.Equal(first.UpdateCount, second.UpdateCount);
            Assert.Equal(first.MigratedInstallCount, second.MigratedInstallCount);
            Assert.Equal(1545, second.DownloadCount);
            Assert.Equal(1245, second.InstallCount);
        }

        [Fact]
        public async Task shields_io_はヒット時に上流を呼ばず同じ画像を返す()
        {
            var handler = new CountingHandler("<svg/>");
            var shieldsIo = BuildServices(shieldsIoHandler: handler).GetRequiredService<IShiledsIoService>();
            var request = new BadgeRequest { BadgeType = BadgeType.VersionShort, ImageExt = ImageExt.Svg };

            var first = await shieldsIo.LoadImage(request, "1.0.0", "");
            var second = await shieldsIo.LoadImage(request, "1.0.0", "");

            Assert.Equal(1, handler.Calls);
            Assert.Equal(first, second);
        }

        /// <summary>
        /// 本番と同じ登録のまま、通信部分だけを固定の応答を返すハンドラーに差し替える。
        /// </summary>
        private static ServiceProvider BuildServices(CountingHandler marketplaceHandler = null, CountingHandler shieldsIoHandler = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            new Startup(new ConfigurationBuilder().Build(), null).ConfigureServices(services);

            if (marketplaceHandler != null)
                services.AddHttpClient<IVSMarketplaceService, VSMarketplaceService>()
                    .ConfigurePrimaryHttpMessageHandler(() => marketplaceHandler);
            if (shieldsIoHandler != null)
                services.AddHttpClient<IShiledsIoService, ShiledsIoService>()
                    .ConfigurePrimaryHttpMessageHandler(() => shieldsIoHandler);

            return services.BuildServiceProvider();
        }

        private class CountingHandler : HttpMessageHandler
        {
            private readonly string body;
            private int calls;

            public CountingHandler(string body)
            {
                this.body = body;
            }

            public int Calls => calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8)
                });
            }
        }
    }
}
