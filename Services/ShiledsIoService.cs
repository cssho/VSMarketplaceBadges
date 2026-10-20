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
            string requestUri = $"/badge/{request.Subject}-{badgeValue}-{request.Color}.{request.ImageExt.ToEnumMember()}" + additionalQuery;
            if (!cache.TryGet<byte[]>(requestUri, out var image))
            {
                image = await client.GetByteArrayAsync(requestUri);
                cache.Set(requestUri, image, image.Length, TimeSpan.FromDays(1), TimeSpan.FromHours(12));
            }
            return image;
        }

    }
}
