using Bookstore.Domain;

namespace Bookstore.Tests;

public sealed class OrderRulesTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("   ", 1)]
    [InlineData("12 An Dương Vương", 0)]
    [InlineData("12 An Dương Vương", -1)]
    public void PlacementRejectsInvalidAddressOrQuantity(string? address, int quantity) =>
        Assert.Throws<OrderRuleException>(() =>
            OrderRules.ValidatePlacement(address, new Dictionary<int, int> { [1] = quantity }));

    [Fact]
    public void CheckoutAcceptsLongestValidSavedAddress()
    {
        var address = new ShippingAddress(1, new string('A', 120), "0912345678", new string('P', 100),
            new string('D', 100), new string('W', 100), new string('S', 180), true);

        Assert.Equal(address.Formatted, OrderRules.ValidatePlacement(address.Formatted,
            new Dictionary<int, int> { [1] = 1 }));
    }

    [Fact]
    public void TotalUsesUnitPriceAndQuantity()
    {
        var lines = new[] { new OrderLine(1, "Dế Mèn", 2, 65_000), new OrderLine(2, "Tuổi thơ", 1, 120_000) };

        Assert.Equal(250_000L, OrderRules.Total(lines));
    }

    [Fact]
    public void SelectionCannotExceedStock() =>
        Assert.Throws<OrderRuleException>(() => OrderRules.Select(new Book(1, "Dế Mèn", 65_000, 5), 6));
}
