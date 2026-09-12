using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using VSMarketplaceBadges.Services;
using System.Net.Mime;

namespace VSMarketplaceBadges.Controllers
{
    [Controller]
    [Route("/")]
    public class BadgeController : ControllerBase
    {

        /// <summary>
        /// 上流障害でフォールバックを返すときの Cache-Control (秒)。
        /// 既定の 3600 秒のままだと、上流が復旧しても最大 1 時間 CloudFront に
        /// unavailable バッジが残り続けてしまう。
        /// </summary>
        private const int FallbackCacheSeconds = 60;

        /// <summary>
        /// バッジ応答に付ける CSP。
        /// </summary>
        /// <remarks>
        /// SVG は同一オリジンの文書として直接開かれるとスクリプトを実行できる。上流 (shields.io) の
        /// 応答をそのまま自ドメインで配信している以上、万一攻撃者が制御するバイト列を引けた場合に
        /// XSS になりうるので、スクリプトを実行させない層をここで 1 枚挟む。
        ///
        /// CloudFront の Response Headers Policy ではなくアプリ側で付けているのは、
        /// ドキュメントページ (wwwroot/index.html) が jQuery・Bootstrap・Twitter ウィジェットと
        /// インラインスクリプトを使っており、配信全体に同じ CSP をかけると壊れるため。
        /// 全体に効かせる安全なヘッダー (HSTS / nosniff など) は CloudFront 側で付与している。
        ///
        /// style-src に unsafe-inline を許すのは、直接開いたときにバッジの見た目を保つため。
        /// img タグ経由での描画には CSP は関与しないので、表示には影響しない。
        /// </remarks>
        private const string BadgeContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; sandbox";

        private readonly ILogger logger;
        private readonly IVSMarketplaceService marketplace;
        private readonly IShiledsIoService shiledsIo;
        private readonly IFallbackBadgeService fallback;

        public BadgeController(ILogger<BadgeController> logger, IVSMarketplaceService marketplace,
            IShiledsIoService shiledsIo, IFallbackBadgeService fallback)
        {
            this.logger = logger;
            this.marketplace = marketplace;
            this.shiledsIo = shiledsIo;
            this.fallback = fallback;
        }

        [HttpGet("{BadgeType}/{ItemName}.{ImageExt}")]
        [ResponseCache(Duration = 3600)]
        public async Task<ActionResult<string>> Get(BadgeRequest request)
        {
            if (request.BadgeType == BadgeType.Unknown || request.ImageExt == ImageExt.Unknown)
                return BadRequest();

            var image = await LoadBadge(request);
            if (image == null)
            {
                // README に貼られたバッジが壊れた画像アイコンにならないよう、同梱の
                // unavailable バッジで代替する。同梱物すら読めないときだけ 503 を返す。
                image = fallback.Load(request.ImageExt);
                if (image == null)
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);

                Response.Headers[HeaderNames.CacheControl] = $"public, max-age={FallbackCacheSeconds}";
            }

            Response.Headers[HeaderNames.ContentSecurityPolicy] = BadgeContentSecurityPolicy;

            return new ObjectResult(image)
            {
                ContentTypes = new MediaTypeCollection { request.ContentType }
            };
        }

        /// <summary>
        /// バッジ画像を取得する。上流 (Marketplace API / shields.io) が落ちている場合は null。
        /// <see cref="BadgeValuConverterExtentions.ToBadgeValue"/> は自前のロジックなので
        /// 意図的に try の外に置いてある。ここでの例外は実装の不具合であり、
        /// フォールバックで覆い隠さずそのまま 500 として表に出す。
        /// </summary>
        private async Task<byte[]> LoadBadge(BadgeRequest request)
        {
            VSMarketplaceItem item;
            try
            {
                item = await marketplace.LoadVsmItemDataFromApi(request.ItemName);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Marketplace API failed. Serving the fallback badge.");
                return null;
            }

            var badgeValue = item?.ToBadgeValue(request.BadgeType);

            try
            {
                return await shiledsIo.LoadImage(request, badgeValue, Request.QueryString.ToString());
            }
            catch (Exception e)
            {
                logger.LogError(e, "shields.io failed. Serving the fallback badge.");
                return null;
            }
        }
    }
}
