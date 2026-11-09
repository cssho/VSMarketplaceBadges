using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// Marketplace の応答で statistics / versions が欠けていても、バッジが unavailable に
    /// 落ちないことを担保する (#46)。公開直後でインストール数 0 の拡張機能では、
    /// Marketplace は statistics キーごと応答から省く。
    /// </summary>
    public class MissingMarketplaceFieldsTests
    {
        [Fact]
        public void statistics_が無ければ統計値はすべて_0()
        {
            var item = new VSMarketplaceItem(new VSMarketplaceItemRaw
            {
                Versions = new[] { new VSMarketplaceVersion { Version = "0.42210.0" } },
                Statistics = null,
            });

            Assert.Equal("v0.42210.0", item.ToBadgeValue(BadgeType.VersionShort));
            Assert.Equal("0", item.ToBadgeValue(BadgeType.Installs));
            Assert.Equal("0", item.ToBadgeValue(BadgeType.Downloads));
            Assert.Equal("0/5 (0)", item.ToBadgeValue(BadgeType.RatingShort));
            Assert.Equal("0", item.ToBadgeValue(BadgeType.TrendingDaily));
        }

        [Fact]
        public void versions_が無ければバージョンは_null()
        {
            var item = new VSMarketplaceItem(new VSMarketplaceItemRaw
            {
                Versions = null,
                Statistics = new[] { new VSMarketplaceStatistics { StatisticName = "install", Value = 5 } },
            });

            // null は ShiledsIoService で unknown と表示される。"v" だけのバッジにしないこと。
            Assert.Null(item.ToBadgeValue(BadgeType.Version));
            Assert.Equal("5", item.ToBadgeValue(BadgeType.Installs));
        }

        [Fact]
        public void versions_が空でもバージョンは_null()
        {
            var item = new VSMarketplaceItem(new VSMarketplaceItemRaw
            {
                Versions = new VSMarketplaceVersion[0],
            });

            Assert.Null(item.Version);
        }

        // 実際の JSON を逆シリアル化する経路で確かめる。statistics キーが無いと
        // プロパティは null になるので、ここで落ちると Marketplace を 2 回呼んだうえで
        // unavailable バッジになっていた。
        [Fact]
        public async Task statistics_キーの無い応答でも項目を返し_Marketplace_は_1_回だけ呼ぶ()
        {
            var handler = new JsonHandler(
                "{\"results\":[{\"extensions\":[{\"versions\":[{\"version\":\"0.42210.0\"}]}]}]}");
            using var provider = TestServices.Build(marketplace: handler);
            var service = provider.GetRequiredService<IVSMarketplaceService>();

            var item = await service.LoadVsmItemDataFromApi("escapepz.pzstudio42");

            Assert.NotNull(item);
            Assert.Equal("v0.42210.0", item.Version);
            Assert.Equal(0, item.InstallCount);
            Assert.Equal(1, handler.Calls);
        }

        private class JsonHandler : HttpMessageHandler
        {
            private readonly string json;
            public int Calls;

            public JsonHandler(string json) => this.json = json;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
