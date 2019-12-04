using System.ComponentModel;
using System.Runtime.Serialization;

namespace VSMarketplaceBadges.Entity
{
    public enum ImageExt
    {
        Unknown,
        [EnumMember(Value = "svg")]
        Svg,
        [EnumMember(Value = "png")]
        Png
    }
}