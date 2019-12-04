using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    public class ShiledsIoService : IShiledsIoService
    {
        public ShiledsIoService(HttpClient client)
        {
            this.client = client;
        }
        private static readonly Cache<string, byte[]> cache = new Cache<string, byte[]>();
        private readonly HttpClient client;

        public async Task<byte[]> LoadImage(BadgeRequest request, string badgeValue, string additionalQuery)
        {
            if (badgeValue == null)
            {
                badgeValue = "unknown";
                request.Color = "lightgrey";
            }

            var key = string.Join(":", new[] { request.Subject, badgeValue, request.Color, request.ImageExt.ToEnumMember(), additionalQuery }
                .Where(x => !string.IsNullOrEmpty(x)));

            var image = cache.Get(key) ?? await client.GetByteArrayAsync($"/badge/{request.Subject}-{badgeValue}-{request.Color}.{request.ImageExt.ToEnumMember()}" + additionalQuery);
            cache.Add(key, image, TimeSpan.FromDays(1));
            return image;
        }
    }
}