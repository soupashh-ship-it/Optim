using Optim.Core.Power;
using Xunit;

namespace Optim.Core.Tests;

public class PowerEngineTests
{
    [Fact]
    public void Parse_lists_plans_and_marks_active()
    {
        const string output =
            "Existing Power Schemes (* Active)\n" +
            "-----------------------------------\n" +
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n" +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\n" +
            "Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)\n";
        var plans = PowerEngine.ParseList(output);
        Assert.Equal(3, plans.Count);
        var active = Assert.Single(plans, p => p.IsActive);
        Assert.Equal("Balanced", active.Name);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", active.Guid);
    }

    [Fact]
    public void Parse_empty_returns_empty()
    {
        Assert.Empty(PowerEngine.ParseList(""));
        Assert.Empty(PowerEngine.ParseList("No power schemes.\n"));
    }
}
