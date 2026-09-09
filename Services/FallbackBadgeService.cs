using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;

namespace VSMarketplaceBadges.Services
{
    /// <summary>
    /// wwwroot に同梱した unavailable バッジを読み出す。
    /// 上流が落ちている場面で使うものなので、ここで新たに外部通信をしてはいけない。
    /// </summary>
    public class FallbackBadgeService : IFallbackBadgeService
    {
        private const string FileNameFormat = "unavailable.{0}";

        private readonly IReadOnlyDictionary<ImageExt, byte[]> badges;
        private readonly ILogger logger;

        public FallbackBadgeService(IFileProvider fileProvider, ILogger<FallbackBadgeService> logger)
        {
            this.logger = logger;

            // 起動時に一度だけ読んでメモリに持つ。フォールバックが必要になるのは上流障害という
            // 負荷の高い場面なので、そのたびにディスクを触らせない。
            var loaded = new Dictionary<ImageExt, byte[]>();
            foreach (var ext in new[] { ImageExt.Svg, ImageExt.Png })
            {
                var bytes = Read(fileProvider, string.Format(FileNameFormat, ext.ToEnumMember()));
                if (bytes != null)
                    loaded.Add(ext, bytes);
            }
            badges = loaded;
        }

        public byte[] Load(ImageExt ext)
            => badges.TryGetValue(ext, out var bytes) ? bytes : null;

        private byte[] Read(IFileProvider fileProvider, string fileName)
        {
            try
            {
                var file = fileProvider.GetFileInfo(fileName);
                if (!file.Exists)
                {
                    logger.LogError("Fallback badge not found: {fileName}", fileName);
                    return null;
                }

                using var stream = file.CreateReadStream();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to load fallback badge: {fileName}", fileName);
                return null;
            }
        }
    }
}
