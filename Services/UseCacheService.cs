using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace VSMarketplaceBadges.Services
{
    public abstract class UseCacheService
    {
        private readonly IDistributedCache cache;
        private readonly ILogger logger;

        public UseCacheService(IDistributedCache cache, ILogger logger)
        {
            this.cache = cache;
            this.logger = logger;
        }


        protected async Task<byte[]> FromCache(string key)
        {
            try
            {
                return await cache.GetAsync(key);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Redis error.");
            }
            return null;
        }

        protected async Task SaveCache(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            try
            {
                await cache.SetAsync(key, value, options);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Redis error.");

            }

        }

        protected async Task RefreshCache(string key)
        {
            try
            {
                await cache.RefreshAsync(key);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Redis error.");

            }

        }

    }
}