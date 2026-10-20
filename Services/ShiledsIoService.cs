using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    public class ShiledsIoService : IShiledsIoService
    {
        public ShiledsIoService(HttpClient client, UpstreamCache cache, ILogger<ShiledsIoService> logger)
        {
            this.client = client;
            this.cache = cache;
            this.logger = logger;
        }

        private readonly HttpClient client;
        private readonly UpstreamCache cache;
        private readonly ILogger logger;

        public async Task<byte[]> LoadImage(BadgeRequest request, string badgeValue, string additionalQuery)
        {
            if (badgeValue == null)
            {
                badgeValue = "unknown";
                request.Color = "lightgrey";
            }

            // キーには利用者が送るクエリ文字列がそのまま入る。長いキーで上限をすり抜けられないよう、
            // UpstreamCache はキーの長さもエントリの大きさに含めて数える。
            string requestUri = $"/badge/{request.Subject}-{EscapeBadgeValue(badgeValue)}-{request.Color}.{request.ImageExt.ToEnumMember()}" + additionalQuery;
            if (!cache.TryGet<byte[]>(requestUri, out var image))
            {
                image = await client.GetByteArrayAsync(requestUri);
                cache.Set(requestUri, image, image.Length, TimeSpan.FromDays(1), TimeSpan.FromHours(12));
            }
            return image;
        }

        /// <summary>
        /// バッジの値を shields.io のパス用にエスケープする。
        /// </summary>
        /// <remarks>
        /// shields.io のパスは <c>-</c> でラベル・値・色を区切り、<c>_</c> を空白として読む。
        /// そのため値に含まれる <c>-</c> と <c>_</c> は二重にしないと意味が変わる。
        /// プレリリース版 (<c>v1.0.0-beta.1</c>) や負の trending 値 (<c>-1.5</c>) は、エスケープしないと
        /// shields.io が 200 のまま「404: badge not found」の画像を返し、フォールバックにも入らない。
        ///
        /// ラベル (<see cref="BadgeRequest.Subject"/>) は対象にしない。既定値はエスケープ済みの
        /// リテラル (<c>trending--daily</c>) で、クエリで上書きする利用者も自分でエスケープしているため、
        /// ここでエスケープすると二重になって表示が変わる。
        /// </remarks>
        public static string EscapeBadgeValue(string value) =>
            value.Replace("-", "--").Replace("_", "__");

    }
}
