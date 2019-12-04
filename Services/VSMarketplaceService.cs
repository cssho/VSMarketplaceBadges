using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Utf8Json;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    public class VSMarketplaceService : IVSMarketplaceService
    {
        private static readonly string endpoint = "/_apis/public/gallery/extensionquery";
        private readonly HttpClient client;

        private static readonly Cache<string, VSMarketplaceItem> cache = new Cache<string, VSMarketplaceItem>();

        private static readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim>> semaphoreDic = new ConcurrentDictionary<string, Lazy<SemaphoreSlim>>();

        public VSMarketplaceService(HttpClient client)
        {
            this.client = client;
        }

        public async Task<VSMarketplaceItem> LoadVsmItemDataFromApi(string itemName)
        {
            var cached = cache.Get(itemName);
            if (cached != null) return cached;
            var semLazy = semaphoreDic.GetOrAdd(itemName, x => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1)));
            if (await semLazy.Value.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                cached = cache.Get(itemName);
                if (cached != null) return cached;
                try
                {
                    return await CoreRequest(itemName);
                }
                finally { semLazy.Value.Release(); }
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
                var response = await result.Content.ReadAsStreamAsync();
                try
                {
                    var extensions = await JsonSerializer.DeserializeAsync<VSMarketplaceResponse>(response);
                    var raw = extensions.Results.FirstOrDefault()?.Extensions?.FirstOrDefault();
                    var item = new VSMarketplaceItem(raw);
                    cache.Add(itemName, item, TimeSpan.FromSeconds(30), false);
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