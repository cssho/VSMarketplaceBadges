using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    public class VSMarketplaceService : IVSMarketplaceService
    {
        private static readonly string endpoint = "/_apis/public/gallery/extensionquery";
        private readonly HttpClient client;
        private readonly UpstreamCache cache;
        private readonly ILogger logger;

        private readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public VSMarketplaceService(HttpClient client, UpstreamCache cache, ILogger<VSMarketplaceService> logger)
        {
            this.client = client;
            this.cache = cache;
            this.logger = logger;
        }

        /// <summary>
        /// キャッシュに入れる <see cref="VSMarketplaceItem"/> 1 件の大きさの見積もり。
        /// 数値のフィールドとオブジェクトのヘッダーで 128 バイト、それにバージョン文字列を足す。
        /// </summary>
        private static long ItemSizeBytes(VSMarketplaceItem item) =>
            128 + (long)(item.Version?.Length ?? 0) * sizeof(char);

        public async Task<VSMarketplaceItem> LoadVsmItemDataFromApi(string itemName)
        {
            if (cache.TryGet<VSMarketplaceItem>(itemName, out var cached))
                return cached;

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
            var req = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { filters = new[] { new { criteria = new[] { new { filterType = 7, value = itemName } } } }, flags = 914 }, options));
            req.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var result = await client.PostAsync(endpoint, req);
            if (result.IsSuccessStatusCode)
            {
                var response = await result.Content.ReadAsByteArrayAsync();
                try
                {
                    var extensions = JsonSerializer.Deserialize<VSMarketplaceResponse>(response, options);
                    var raw = extensions.Results.FirstOrDefault()?.Extensions?.FirstOrDefault();
                    if (raw == null)
                    {
                        // itemName は URL から来る利用者入力なので、そのままログに流さない。
                        logger.LogInformation("Not found item: {itemName}", LogSanitizer.ForLog(itemName));
                        return null;
                    }
                    var item = new VSMarketplaceItem(raw);
                    // 生の応答は versions/files/statistics を丸ごと含み数十 KB になる。
                    // バッジに要る値だけを計算済みのオブジェクトのまま持つ。
                    cache.Set(itemName, item, ItemSizeBytes(item), TimeSpan.FromMinutes(10));
                    return item;
                }
                catch (Exception ex)
                {
                    ex.Data.Add("json", await result.Content.ReadAsStringAsync());
                    throw;
                }

            }
            var e = new InvalidCastException("Invalid extension data");
            e.Data.Add("req", req.ToString());
            e.Data.Add("res", await result.Content.ReadAsStringAsync());
            throw e;
        }
    }

}
