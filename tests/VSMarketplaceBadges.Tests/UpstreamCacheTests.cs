using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            using var provider = TestServices.Build(marketplace: handler);
            var marketplace = provider.GetRequiredService<IVSMarketplaceService>();

            var first = await marketplace.LoadVsmItemDataFromApi("publisher.extension");
            var second = await marketplace.LoadVsmItemDataFromApi("publisher.extension");

            Assert.Equal(1, handler.Calls);
            Assert.Same(first, second);
            Assert.Equal("v1.10.0", second.Version);
            Assert.Equal(1545, second.DownloadCount);
            Assert.Equal(1245, second.InstallCount);
        }

        [Fact]
        public async Task Marketplace_のキャッシュは大文字小文字を区別しない()
        {
            var handler = new CountingHandler(MarketplaceResponse);
            using var provider = TestServices.Build(marketplace: handler);
            var marketplace = provider.GetRequiredService<IVSMarketplaceService>();

            await marketplace.LoadVsmItemDataFromApi("Publisher.Extension");
            await marketplace.LoadVsmItemDataFromApi("publisher.extension");

            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task shields_io_はヒット時に上流を呼ばず同じ画像を返す()
        {
            var handler = new CountingHandler("<svg/>");
            using var provider = TestServices.Build(shieldsIo: handler);
            var shieldsIo = provider.GetRequiredService<IShiledsIoService>();
            var request = new BadgeRequest { BadgeType = BadgeType.VersionShort, ImageExt = ImageExt.Svg };

            var first = await shieldsIo.LoadImage(request, "1.0.0", "");
            var second = await shieldsIo.LoadImage(request, "1.0.0", "");

            Assert.Equal(1, handler.Calls);
            Assert.Equal(first, second);
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
