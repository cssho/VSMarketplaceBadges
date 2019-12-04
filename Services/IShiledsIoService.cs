using System.Threading.Tasks;
using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Services
{
    public interface IShiledsIoService
    {
        public Task<byte[]> LoadImage(BadgeRequest request, string badgeValue, string additionalQuery);
    }
}