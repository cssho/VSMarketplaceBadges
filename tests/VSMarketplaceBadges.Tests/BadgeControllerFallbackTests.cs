using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using VSMarketplaceBadges.Controllers;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// 上流 (Marketplace API / shields.io) が落ちたときに、README 上で壊れた画像アイコンに
    /// ならず同梱の unavailable バッジへ退避することを担保する。
    /// </summary>
    public class BadgeControllerFallbackTests
    {
        private static readonly byte[] RealBadge = { 1, 2, 3 };
        private static readonly byte[] FallbackBadge = { 9, 9, 9 };

        [Fact]
        public async Task 正常時は上流の画像をそのまま返す()
        {
            var controller = Create(new StubMarketplace(), new StubShieldsIo());

            var result = await controller.Get(Request(BadgeType.VersionShort, ImageExt.Svg));

            Assert.Equal(RealBadge, Body(result));
            // 既定の [ResponseCache(Duration = 3600)] を上書きしていないこと。
            Assert.False(controller.Response.Headers.ContainsKey(HeaderNames.CacheControl));
        }

        [Fact]
        public async Task shields_io_が落ちたらフォールバックを返す()
        {
            var controller = Create(new StubMarketplace(), new StubShieldsIo { Throws = true });

            var result = await controller.Get(Request(BadgeType.VersionShort, ImageExt.Svg));

            Assert.Equal(FallbackBadge, Body(result));
        }

        [Fact]
        public async Task Marketplace_API_が落ちたらフォールバックを返す()
        {
            var controller = Create(new StubMarketplace { Throws = true }, new StubShieldsIo());

            var result = await controller.Get(Request(BadgeType.VersionShort, ImageExt.Png));

            Assert.Equal(FallbackBadge, Body(result));
        }

        [Fact]
        public async Task フォールバック時は_Cache_Control_を短くする()
        {
            var controller = Create(new StubMarketplace(), new StubShieldsIo { Throws = true });

            await controller.Get(Request(BadgeType.VersionShort, ImageExt.Svg));

            // 既定の 3600 秒のままだと、上流が復旧しても最大 1 時間 CloudFront に
            // unavailable バッジが残り続けてしまう。
            Assert.Equal("public, max-age=60", controller.Response.Headers[HeaderNames.CacheControl]);
        }

        [Fact]
        public async Task 同梱物すら読めないときは_503()
        {
            var controller = Create(new StubMarketplace(), new StubShieldsIo { Throws = true },
                new StubFallback { Bytes = null });

            var result = await controller.Get(Request(BadgeType.VersionShort, ImageExt.Svg));

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        }

        [Fact]
        public async Task 未知のバッジタイプは上流を呼ばずに_400()
        {
            var marketplace = new StubMarketplace();
            var controller = Create(marketplace, new StubShieldsIo());

            var result = await controller.Get(Request(BadgeType.Unknown, ImageExt.Svg));

            Assert.IsType<BadRequestResult>(result.Result);
            Assert.False(marketplace.Called);
        }

        private static BadgeRequest Request(BadgeType type, ImageExt ext)
            => new BadgeRequest { BadgeType = type, ItemName = "publisher.extension", ImageExt = ext };

        private static byte[] Body(ActionResult<string> result)
            => Assert.IsType<ObjectResult>(result.Result).Value as byte[];

        private static BadgeController Create(IVSMarketplaceService marketplace, IShiledsIoService shieldsIo,
            IFallbackBadgeService fallback = null)
            => new BadgeController(NullLogger<BadgeController>.Instance, marketplace, shieldsIo,
                fallback ?? new StubFallback { Bytes = FallbackBadge })
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

        private class StubMarketplace : IVSMarketplaceService
        {
            public bool Throws { get; set; }
            public bool Called { get; private set; }

            public Task<VSMarketplaceItem> LoadVsmItemDataFromApi(string itemName)
            {
                Called = true;
                if (Throws)
                    throw new HttpRequestException("marketplace down");
                // null は「拡張機能が見つからない」を表す正常な戻り値。
                return Task.FromResult<VSMarketplaceItem>(null);
            }
        }

        private class StubShieldsIo : IShiledsIoService
        {
            public bool Throws { get; set; }

            public Task<byte[]> LoadImage(BadgeRequest request, string badgeValue, string additionalQuery)
            {
                if (Throws)
                    throw new HttpRequestException("shields.io down");
                return Task.FromResult(RealBadge);
            }
        }

        private class StubFallback : IFallbackBadgeService
        {
            public byte[] Bytes { get; set; }

            public byte[] Load(ImageExt ext) => Bytes;
        }
    }
}
