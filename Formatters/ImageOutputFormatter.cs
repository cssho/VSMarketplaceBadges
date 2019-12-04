using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace VSMarketplaceBadges.Formatters
{
    public class ImageOutputFormatter : OutputFormatter
    {
        public ImageOutputFormatter()
        {
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("image/svg+xml"));
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("image/png"));
        }

        public override bool CanWriteResult(OutputFormatterCanWriteContext context)
            => base.CanWriteResult(context) && context.Object is byte[]
                && context.HttpContext.Response.StatusCode == 200;

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
        {
            var response = context.HttpContext.Response;
            var body = context.Object as byte[];
            if (body.Length != 0)
                await response.Body.WriteAsync(body);
        }
    }
}