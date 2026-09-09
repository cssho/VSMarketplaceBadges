using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    public class FallbackBadgeServiceTests
    {
        [Fact]
        public void 拡張子ごとに対応するファイルを返す()
        {
            var provider = new StubFileProvider(new Dictionary<string, byte[]>
            {
                ["unavailable.svg"] = Encoding.UTF8.GetBytes("<svg/>"),
                ["unavailable.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 }
            });

            var sut = Create(provider);

            Assert.Equal("<svg/>", Encoding.UTF8.GetString(sut.Load(ImageExt.Svg)));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, sut.Load(ImageExt.Png));
        }

        [Fact]
        public void ファイルは起動時に一度だけ読まれる()
        {
            var provider = new StubFileProvider(new Dictionary<string, byte[]>
            {
                ["unavailable.svg"] = Encoding.UTF8.GetBytes("<svg/>"),
                ["unavailable.png"] = new byte[] { 0x89 }
            });

            var sut = Create(provider);
            var readsAfterConstruction = provider.ReadCount;

            sut.Load(ImageExt.Svg);
            sut.Load(ImageExt.Svg);
            sut.Load(ImageExt.Png);

            // 上流障害という負荷の高い場面で毎回ディスクを触らないことを保証する。
            Assert.Equal(2, readsAfterConstruction);
            Assert.Equal(readsAfterConstruction, provider.ReadCount);
        }

        [Fact]
        public void ファイルが無い形式は_null_を返す()
        {
            var provider = new StubFileProvider(new Dictionary<string, byte[]>
            {
                ["unavailable.svg"] = Encoding.UTF8.GetBytes("<svg/>")
            });

            var sut = Create(provider);

            Assert.NotNull(sut.Load(ImageExt.Svg));
            Assert.Null(sut.Load(ImageExt.Png));
        }

        [Fact]
        public void 読み込みが例外を投げても構築は失敗しない()
        {
            var sut = Create(new ThrowingFileProvider());

            Assert.Null(sut.Load(ImageExt.Svg));
            Assert.Null(sut.Load(ImageExt.Png));
        }

        [Fact]
        public void Unknown_は常に_null_を返す()
        {
            var provider = new StubFileProvider(new Dictionary<string, byte[]>
            {
                ["unavailable.svg"] = Encoding.UTF8.GetBytes("<svg/>"),
                ["unavailable.png"] = new byte[] { 0x89 }
            });

            Assert.Null(Create(provider).Load(ImageExt.Unknown));
        }

        private static FallbackBadgeService Create(IFileProvider provider)
            => new FallbackBadgeService(provider, NullLogger<FallbackBadgeService>.Instance);

        private class StubFileProvider : IFileProvider
        {
            private readonly IDictionary<string, byte[]> files;

            public StubFileProvider(IDictionary<string, byte[]> files) => this.files = files;

            public int ReadCount { get; private set; }

            public IFileInfo GetFileInfo(string subpath)
                => files.TryGetValue(subpath, out var bytes)
                    ? new StubFileInfo(subpath, bytes, () => ReadCount++)
                    : new NotFoundFileInfo(subpath);

            public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

            public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        }

        private class StubFileInfo : IFileInfo
        {
            private readonly byte[] bytes;
            private readonly Action onRead;

            public StubFileInfo(string name, byte[] bytes, Action onRead)
            {
                Name = name;
                this.bytes = bytes;
                this.onRead = onRead;
            }

            public bool Exists => true;
            public long Length => bytes.Length;
            public string PhysicalPath => null;
            public string Name { get; }
            public DateTimeOffset LastModified => DateTimeOffset.MinValue;
            public bool IsDirectory => false;

            public Stream CreateReadStream()
            {
                onRead();
                return new MemoryStream(bytes);
            }
        }

        private class ThrowingFileProvider : IFileProvider
        {
            public IFileInfo GetFileInfo(string subpath) => throw new IOException("disk on fire");

            public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

            public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        }
    }
}
