using System;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// <see cref="UpstreamCache"/> がキー文字列の長さまで含めて上限を数えることを担保する。
    /// </summary>
    /// <remarks>
    /// shields.io のキーには利用者が送るクエリ文字列がそのまま入る。値だけを数えると、
    /// 長いクエリを大量に送られたときに上限を超えてメモリを使い続けてしまう。
    /// </remarks>
    public class UpstreamCacheSizeTests
    {
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

        [Fact]
        public void エントリの大きさはキーと値と管理領域の合計()
        {
            Assert.Equal(3 * 2 + 100 + UpstreamCache.EntryOverheadBytes, UpstreamCache.EntrySize("abc", 100));
        }

        [Fact]
        public void 上限に収まるエントリは入る()
        {
            using var cache = new UpstreamCache(10_000);

            cache.Set("/badge/a", new byte[100], 100, OneHour);

            Assert.True(cache.TryGet<byte[]>("/badge/a", out var value));
            Assert.Equal(100, value.Length);
        }

        [Fact]
        public void 値が小さくてもキーが長ければ上限を超えて入らない()
        {
            using var cache = new UpstreamCache(10_000);
            // 値は 10 バイトだが、キーは 5,000 文字 (10,000 バイト) ある。
            var key = "/badge/a?label=" + new string('x', 5_000);

            cache.Set(key, new byte[10], 10, OneHour);

            Assert.False(cache.TryGet<byte[]>(key, out _));
        }

        [Fact]
        public void 長いキーで埋めても上限の件数までしか入らない()
        {
            // 1 件 = キー 2,000 文字 (4,000 バイト) + 値 10 + 管理領域 512 = 4,522 バイト。
            // 上限 10,000 バイトなら 2 件まで入り、3 件目は捨てられる。
            using var cache = new UpstreamCache(10_000);
            string Key(int i) => $"/badge/{i}?label=" + new string('x', 2_000 - 15);

            for (var i = 0; i < 3; i++)
                cache.Set(Key(i), new byte[10], 10, OneHour);

            Assert.True(cache.TryGet<byte[]>(Key(0), out _));
            Assert.True(cache.TryGet<byte[]>(Key(1), out _));
            Assert.False(cache.TryGet<byte[]>(Key(2), out _));
        }
    }
}
