using Optim.Core.Startup;
using Xunit;

namespace Optim.Core.Tests;

public class StartupEngineTests
{
    private readonly string _name = "OptimTest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Add_list_remove_roundtrip()
    {
        Assert.False(StartupEngine.Add("", @"C:\x.exe"));
        Assert.False(StartupEngine.Add(_name, "  "));
        Assert.True(StartupEngine.Add(_name, @"C:\Windows\System32\notepad.exe"));
        try
        {
            Assert.Contains(StartupEngine.List(), i => i.Name == _name);
        }
        finally
        {
            Assert.True(StartupEngine.Remove(_name));
        }
        Assert.DoesNotContain(StartupEngine.List(), i => i.Name == _name);
    }
}
