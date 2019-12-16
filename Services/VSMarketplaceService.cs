using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Utf8Json;
using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Services
{
    public class VSMarketplaceService : UseCacheService, IVSMarketplaceService
    {
        private static readonly string endpoint = "/_apis/public/gallery/extensionquery";
        private readonly HttpClient client;
        private readonly IDistributedCache cache;
        private readonly ILogger logger;

        public VSMarketplaceService(HttpClient client, IDistributedCache cache, ILogger<VSMarketplaceService> logger) : base(cache, logger)
        {
            this.client = client;
            this.cache = cache;
            this.logger = logger;
        }

        public async Task<VSMarketplaceItem> LoadVsmItemDataFromApi(string itemName)
        {
            var cached = await FromCache(itemName);
            if (cached != null)
                return new VSMarketplaceItem(JsonSerializer.Deserialize<VSMarketplaceResponse>(cached)
                        .Results.FirstOrDefault()?.Extensions?.FirstOrDefault());

            try
            {
                return await CoreRequest(itemName);
            }
            catch (Exception e)
            {
                logger.LogError(e, "VSMApi");
            }

            return await CoreRequest(itemName);
        }

        private async Task<VSMarketplaceItem> CoreRequest(string itemName)
        {
            var req = new ByteArrayContent(JsonSerializer.Serialize(new { filters = new[] { new { criteria = new[] { new { filterType = 7, value = itemName }, new { filterType = 12, value = "4096" } } } }, flags = 914 }));
            req.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var result = await client.PostAsync(endpoint, req);
            if (result.IsSuccessStatusCode)
            {
                var response = await result.Content.ReadAsByteArrayAsync();
                try
                {
                    var extensions = JsonSerializer.Deserialize<VSMarketplaceResponse>(response);
                    var raw = extensions.Results.FirstOrDefault()?.Extensions?.FirstOrDefault();
                    if (raw == null)
                    {
                        logger.LogInformation("Not found item: {itemName}", itemName);
                        return null;
                    }
                    var item = new VSMarketplaceItem(raw);
                    await SaveCache(itemName, response, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });
                    return item;
                }
                catch (Exception ex)
                {
                    ex.Data.Add("json", await result.Content.ReadAsStringAsync());
                    throw ex;
                }

            }
            var e = new InvalidCastException("Invalid extension data");
            e.Data.Add("req", req.ToString());
            e.Data.Add("res", await result.Content.ReadAsStringAsync());
            throw e;
        }
    }

}