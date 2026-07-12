using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Praxis.Shared.Tests;

/// Guards the promises M0.1 makes about the build itself: the framework we target, and the
/// three settings that make the compiler our first test suite. The real tests arrive with the
/// real types in M0.2 — until then, this is what there is to verify.
public sealed class ScaffoldTests
{
    [Fact]
    public void Shared_targets_net10()
    {
        var framework = typeof(AssemblyMarker).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.Equal(".NETCoreApp,Version=v10.0", framework?.FrameworkName);
    }

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("EnforceCodeStyleInBuild", "true")]
    public void Build_settings_are_enforced_solution_wide(string setting, string expected)
    {
        // These live in Directory.Build.props so no project can quietly opt out of them.
        // If someone deletes one, the whole solution loses a guarantee — so we assert on the
        // file rather than on any one assembly's metadata.
        var props = XDocument.Load(RepoRoot().GetFiles("Directory.Build.props").Single().FullName);

        var value = props.Descendants(setting).SingleOrDefault()?.Value;

        Assert.Equal(expected, value);
    }

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && dir.GetFiles("Directory.Build.props").Length == 0)
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException(
            $"No Directory.Build.props found above {AppContext.BaseDirectory}.");
    }
}
