using LgController.ThinQ;

namespace LgController.Tests;

public class ThinQRegionTests
{
    [Theory]
    [InlineData("BR", "aic")]
    [InlineData("us", "aic")]
    [InlineData("KR", "kic")]
    [InlineData(" pt ", "eic")]
    public void FromCountry_MapsToRegion(string country, string region) =>
        Assert.Equal(region, ThinQRegion.FromCountry(country));

    [Fact]
    public void FromCountry_UnknownCountry_Throws() =>
        Assert.Throws<ArgumentException>(() => ThinQRegion.FromCountry("ZZ"));
}
