using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace VSMarketplaceBadges.Utility
{
    public class Cache<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, CacheItem<TValue>> cache = new ConcurrentDictionary<TKey, CacheItem<TValue>>();

        public void Add(TKey key, TValue value, TimeSpan expiresAfter, bool overwriteIfExist = true)
        {
            if (overwriteIfExist || !cache.ContainsKey(key))
                cache[key] = new CacheItem<TValue>(value, expiresAfter);
        }

        public TValue Get(TKey key)
        {
            if (!cache.ContainsKey(key)) return default(TValue);
            var cached = cache[key];
            if (DateTimeOffset.Now - cached.Created >= cached.ExpiresAfter)
            {
                cache.Remove(key,out var _);
                return default(TValue);
            }
            return cached.Value;
        }

        private class CacheItem<T>
        {
            public CacheItem(T value, TimeSpan expiresAfter)
            {
                Value = value;
                ExpiresAfter = expiresAfter;
            }
            public T Value { get; }
            internal DateTimeOffset Created { get; } = DateTimeOffset.Now;
            internal TimeSpan ExpiresAfter { get; }
        }
    }
}