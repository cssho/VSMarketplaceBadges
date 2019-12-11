using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using VSMarketplaceBadges.Services;
using System.Net.Mime;

namespace VSMarketplaceBadges.Controllers
{
    [Controller]
    [Route("/")]
    public class BadgeController : ControllerBase
    {

        private readonly ILogger logger;
        private readonly IVSMarketplaceService marketplace;
        private readonly IShiledsIoService shiledsIo;

        public BadgeController(ILogger<BadgeController> logger, IVSMarketplaceService marketplace,
            IShiledsIoService shiledsIo)
        {
            this.logger = logger;
            this.marketplace = marketplace;
            this.shiledsIo = shiledsIo;
        }

        [HttpGet("{BadgeType}/{ItemName}.{ImageExt}")]
        [ResponseCache(Location = ResponseCacheLocation.None, Duration = 0)]
        public async Task<ActionResult<string>> Get(BadgeRequest request)
        {
            if (request.BadgeType == BadgeType.Unknown || request.ImageExt == ImageExt.Unknown)
                return BadRequest();
            var item = await marketplace.LoadVsmItemDataFromApi(request.ItemName);
            return new ObjectResult(await shiledsIo.LoadImage(request, item?.ToBadgeValue(request.BadgeType), Request.QueryString.ToString()))
            {
                ContentTypes = new MediaTypeCollection { request.ContentType }
            };
        }
    }
}
