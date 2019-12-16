using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    public class ShiledsIoService : UseCacheService, IShiledsIoService
    {
        public ShiledsIoService(HttpClient client, IDistributedCache cache, ILogger<ShiledsIoService> logger) : base(cache, logger)
        {
            this.client = client;
            this.cache = cache;
            this.logger = logger;
        }

        private readonly HttpClient client;
        private readonly IDistributedCache cache;
        private readonly ILogger logger;

        public async Task<byte[]> LoadImage(BadgeRequest request, string badgeValue, string additionalQuery)
        {
            if (badgeValue == null)
            {
                badgeValue = "unknown";
                request.Color = "lightgrey";
            }

            string requestUri = $"/badge/{request.Subject}-{badgeValue}-{request.Color}.{request.ImageExt.ToEnumMember()}" + additionalQuery;
            byte[] image = await FromCache(requestUri);
            if (image == null)
            {
                image = await client.GetByteArrayAsync(requestUri);
                await SaveCache(requestUri, image, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
                    SlidingExpiration = TimeSpan.FromHours(12)
                });
            }
            else
            {
                await RefreshCache(requestUri);
            }
            return image;
        }

    }
}