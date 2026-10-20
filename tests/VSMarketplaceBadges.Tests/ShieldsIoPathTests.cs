using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// shields.io に渡すパスで、バッジの値に含まれる区切り文字がエスケープされることを担保する。
    /// </summary>
    /// <remarks>
    /// エスケープしないと shields.io は 200 のまま「404: badge not found」の画像を返すので、
    /// フォールバックにも入らず壊れたバッジがそのまま表示される。
    /// </remarks>
    public class ShieldsIoPathTests
    {
        [Theory]
        [InlineData("v1.0.0-beta.1", "v1.0.0--beta.1")]
        [InlineData("-1.5", "--1.5")]
        [InlineData("a_b", "a__b")]
        [InlineData("average: 4.2/5 (630 ratings)", "average: 4.2/5 (630 ratings)")]
        [InlineData("v1.2.3", "v1.2.3")]
        public void バッジの値の区切り文字を二重にする(string value, string expected)
        {
            Assert.Equal(expected, ShiledsIoService.EscapeBadgeValue(value));
        }

        [Fact]
        public async Task 値はエスケープしラベルはそのまま渡す()
        {
            var handler = new RecordingHandler();
            using var provider = TestServices.Build(shieldsIo: handler);
            var shieldsIo = provider.GetRequiredService<IShiledsIoService>();
            var request = new BadgeRequest { BadgeType = BadgeType.TrendingDaily, ImageExt = ImageExt.Svg };

            await shieldsIo.LoadImage(request, "-1.5", "");

            // ラベルの trending--daily は既定値のまま (エスケープ済みのリテラル) で、二重にしない。
            Assert.Equal("/badge/trending--daily---1.5-brightgreen.svg", handler.LastPath);
        }

        private class RecordingHandler : HttpMessageHandler
        {
            public string LastPath { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastPath = request.RequestUri.AbsolutePath;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1 })
                });
            }
        }
    }
}
