using System;
using Microsoft.Extensions.Caching.Memory;

namespace VSMarketplaceBadges.Services
{
    /// <summary>
    /// 上流 (Marketplace API / shields.io) の応答を、Lambda の実行環境ごとのメモリに持つキャッシュ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// エントリの大きさは値だけでなくキー文字列とエントリ自体の管理領域まで含めて数える。
    /// shields.io のキーには利用者が送るクエリ文字列 (最大で URL 長の 8KB 近く) がそのまま入るため、
    /// 値だけを数えると上限がメモリ使用量の上限にならない。
    /// </para>
    /// <para>
    /// <see cref="MemoryCacheOptions.SizeLimit"/> を設定した <see cref="MemoryCache"/> は、
    /// サイズを指定しないエントリを追加すると例外を投げる。そのため DI 共有の
    /// <c>IMemoryCache</c> は使わず専用のインスタンスを持ち、追加は <see cref="Set{T}"/> の
    /// 1 か所だけで行ってサイズの指定漏れを起こさないようにしている。
    /// </para>
    /// <para>
    /// 値はコピーせずに参照のまま返す。呼び出し側で書き換えないこと。
    /// </para>
    /// </remarks>
    public sealed class UpstreamCache : IDisposable
    {
        /// <summary>
        /// キャッシュ全体の上限 (バイト)。Lambda のメモリは 1024MB で、実測の使用量は 180MB 前後。
        /// </summary>
        public const long DefaultSizeLimitBytes = 200L * 1024 * 1024;

        /// <summary>
        /// エントリ 1 件あたりの管理領域の見積もり (バイト)。<see cref="MemoryCache"/> の
        /// エントリオブジェクト、内部の辞書、期限の情報などをまとめて多めに見積もった値。
        /// </summary>
        public const long EntryOverheadBytes = 512;

        private readonly MemoryCache cache;

        public UpstreamCache() : this(DefaultSizeLimitBytes)
        {
        }

        public UpstreamCache(long sizeLimitBytes)
        {
            cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimitBytes });
        }

        /// <summary>
        /// キャッシュから取り出す。スライディング期限は取り出した時点で延びる。
        /// </summary>
        public bool TryGet<T>(string key, out T value) => cache.TryGetValue(key, out value);

        /// <summary>
        /// キャッシュに入れる。上限を超える場合は入れずに捨てる (例外は投げない)。
        /// </summary>
        /// <remarks>
        /// 上限を超えた追加は、捨てられるだけでなく <see cref="MemoryCache"/> の圧縮も
        /// バックグラウンドで起動する。圧縮では上限の 5% (<see cref="MemoryCacheOptions.CompactionPercentage"/>
        /// の既定値) ぶんの既存エントリが、期限切れ → 最後に使われたのが古い順に追い出される。
        /// つまり長いキーで上限まで埋められると、正規のバッジのエントリも押し出される。
        /// その場合も上流へ取りに行き直すだけで、表示は壊れない。
        /// </remarks>
        /// <param name="valueBytes">値が占めるおおよそのバイト数。</param>
        public void Set<T>(string key, T value, long valueBytes, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration = null)
        {
            using var entry = cache.CreateEntry(key);
            entry.Value = value;
            entry.Size = EntrySize(key, valueBytes);
            entry.AbsoluteExpirationRelativeToNow = absoluteExpiration;
            entry.SlidingExpiration = slidingExpiration;
        }

        /// <summary>
        /// エントリ 1 件の大きさ。キーは UTF-16 なので 1 文字 2 バイトで数える。
        /// </summary>
        public static long EntrySize(string key, long valueBytes) =>
            (long)key.Length * sizeof(char) + valueBytes + EntryOverheadBytes;

        public void Dispose() => cache.Dispose();
    }
}
