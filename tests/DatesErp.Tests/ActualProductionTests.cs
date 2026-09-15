using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Views.Screens;
namespace DatesErp.Tests;
public class ActualProductionTests
{
    [Fact]
    public void Actual_Delivery_Persists_Stock_QC_Reports_And_Rolls_Back_Atomically()
    {
        using var host = new TestHost(); host.LoginAsAdmin(); int steps = 0;
        ActualDeliveryScenarios.Run(host.Services, (ok, message) => { Assert.True(ok, message); steps++; });
        Assert.True(steps >= 105);
    }
    [Theory]
    [InlineData("")][InlineData("NaN")][InlineData("Infinity")][InlineData("-1")][InlineData("3001")][InlineData("2700.5")]
    public void Actual_UI_Row_Does_Not_Coerce_Invalid_Input(string value)
    {
        var row = new ActualProductionRow(new ActualDeliveryItemDto { PlannedCartons = 3000 }, false) { Actual = value };
        Assert.False(row.TryQuantity(out _)); Assert.NotEmpty(row.Error); Assert.Equal("—", row.Difference);
    }
    [Fact]
    public void Arabic_Digits_Are_Actual_Input_Not_Implicit_Planned_Quantity()
    {
        var row = new ActualProductionRow(new ActualDeliveryItemDto { PlannedCartons = 3000 }, false);
        Assert.Empty(row.Actual); row.Actual = "٢٧٠٠"; Assert.True(row.TryQuantity(out var quantity)); Assert.Equal(2700, quantity); Assert.Equal("300", row.Difference);
        Assert.True(ActualProductionRow.TryNonnegative("١٫٥", out var hours)); Assert.Equal(1.5, hours);
    }
    [Fact]
    public void Submission_Cannot_Choose_Customer_Product_Lot_Or_Planned_Quantity()
    {
        var fields = typeof(ActualProductionItemDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "ActualCartons", "OrderItemId" }, fields);
    }
}
