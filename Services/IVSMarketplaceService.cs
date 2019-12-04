using System.Threading.Tasks;
using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Services
{
    public interface IVSMarketplaceService
    {
        public Task<VSMarketplaceItem> LoadVsmItemDataFromApi(string itemName);
    }
}