using System;
using System.ComponentModel;
using System.Linq;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// /{badgeType}/{item}.{ext} のルートバインドは CustomEnumConverter を経由し、[EnumMember] を
    /// キーにする。属性・subject・ToBadgeValue の case のいずれかが欠けたバッジタイプは、ビルド時では
    /// なくリクエスト時に初めて失敗する。これらのテストはそれをビルド時の失敗に変える。
    /// </summary>
    public class BadgeTypeBindingTests
    {
        public static TheoryData<BadgeType> AllBadgeTypes()
        {
            var data = new TheoryData<BadgeType>();
            foreach (var t in Enum.GetValues(typeof(BadgeType)).Cast<BadgeType>().Where(t => t != BadgeType.Unknown))
                data.Add(t);
            return data;
        }

        [Theory]
        [MemberData(nameof(AllBadgeTypes))]
        public void EveryBadgeType_HasAnEnumMemberValue(BadgeType type)
            => Assert.False(string.IsNullOrEmpty(type.ToEnumMember()),
                $"{type} is missing its [EnumMember(Value = \"...\")] attribute.");

        [Theory]
        [MemberData(nameof(AllBadgeTypes))]
        public void EveryBadgeType_RoundTripsThroughRouteBinding(BadgeType type)
        {
            var converter = TypeDescriptor.GetConverter(typeof(BadgeType));
            Assert.Equal(type, converter.ConvertFrom(type.ToEnumMember()));
        }

        [Theory]
        [MemberData(nameof(AllBadgeTypes))]
        public void EveryBadgeType_HasASubject(BadgeType type)
            => Assert.False(string.IsNullOrEmpty(new BadgeRequest { BadgeType = type }.Subject),
                $"{type} has no subject constant in BadgeRequest.");

        [Theory]
        [MemberData(nameof(AllBadgeTypes))]
        public void EveryBadgeType_HasAToBadgeValueCase(BadgeType type)
        {
            var item = new VSMarketplaceItem(new VSMarketplaceItemRaw
            {
                Versions = new[] { new VSMarketplaceVersion { Version = "1.0.0" } },
                Statistics = Array.Empty<VSMarketplaceStatistics>()
            });
            Assert.NotNull(item.ToBadgeValue(type));
        }

        [Theory]
        [InlineData("version", BadgeType.Version)]
        [InlineData("installs-short", BadgeType.InstallsShort)]
        [InlineData("trending-monthly", BadgeType.TrendingMonthly)]
        public void KnownSegments_BindToTheirBadgeType(string segment, BadgeType expected)
            => Assert.Equal(expected, TypeDescriptor.GetConverter(typeof(BadgeType)).ConvertFrom(segment));

        // マップされていないセグメントは null に変換され、モデルバインドが既定値の
        // BadgeType.Unknown にする。コントローラーはそれを受けて 400 を返す。
        [Theory]
        [InlineData("Version")]      // 一致判定は大文字小文字を区別する
        [InlineData("installsshort")]
        public void UnknownSegments_DoNotBind(string segment)
            => Assert.Null(TypeDescriptor.GetConverter(typeof(BadgeType)).ConvertFrom(segment));

        // BadgeType.Unknown には [EnumMember] が付いていないため、CreateEnumDictionary は "" をキーにする。
        [Fact]
        public void EmptySegment_BindsToUnknown()
            => Assert.Equal(BadgeType.Unknown, TypeDescriptor.GetConverter(typeof(BadgeType)).ConvertFrom(""));

        [Theory]
        [InlineData("svg", ImageExt.Svg)]
        [InlineData("png", ImageExt.Png)]
        public void ImageExtensions_Bind(string segment, ImageExt expected)
            => Assert.Equal(expected, TypeDescriptor.GetConverter(typeof(ImageExt)).ConvertFrom(segment));

        [Theory]
        [InlineData(ImageExt.Svg, "image/svg+xml")]
        [InlineData(ImageExt.Png, "image/png")]
        public void ContentType_MatchesTheExtension(ImageExt ext, string expected)
            => Assert.Equal(expected, new BadgeRequest { ImageExt = ext }.ContentType.ToString());
    }
}
