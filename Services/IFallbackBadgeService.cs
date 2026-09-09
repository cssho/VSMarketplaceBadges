using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Services
{
    /// <summary>
    /// 上流 (Marketplace API / shields.io) が落ちているときに代わりに配信する、
    /// 同梱済みの "unavailable" バッジを供給する。
    /// </summary>
    public interface IFallbackBadgeService
    {
        /// <summary>
        /// 指定形式のフォールバックバッジを返す。読み込めなかった場合は null。
        /// </summary>
        byte[] Load(ImageExt ext);
    }
}
