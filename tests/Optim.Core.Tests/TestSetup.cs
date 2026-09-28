using System.Runtime.CompilerServices;
using Optim.Core.Logging;

namespace Optim.Core.Tests;

/// <summary>
/// Runs once before any test in this assembly. Unit runs must never write into
/// the real app log at %LOCALAPPDATA%\Optim\logs — the bug-hunting playbook
/// reads that log to tell an app crash from a test artifact, so test noise
/// (corrupt-journal quarantine warnings, failed registry probes) has to stay out.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize() => FileLogger.Enabled = false;
}
