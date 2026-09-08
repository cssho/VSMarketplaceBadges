using System;
using System.ComponentModel;
using System.Linq;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// Route binding for /{badgeType}/{item}.{ext} goes through CustomEnumConverter, which keys off
    /// [EnumMember]. A badge type missing its attribute, its subject, or its ToBadgeValue case fails
    /// at request time rather than at build time -- these tests turn that into a build failure.
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

        // An unmapped segment converts to null, which model binding turns into the default
        // BadgeType.Unknown -- the controller then returns 400.
        [Theory]
        [InlineData("Version")]      // matching is case sensitive
        [InlineData("installsshort")]
        public void UnknownSegments_DoNotBind(string segment)
            => Assert.Null(TypeDescriptor.GetConverter(typeof(BadgeType)).ConvertFrom(segment));

        // BadgeType.Unknown carries no [EnumMember], so CreateEnumDictionary keys it under "".
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
