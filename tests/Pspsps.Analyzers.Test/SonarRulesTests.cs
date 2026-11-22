using System.Text.RegularExpressions;

namespace Pspsps.Analyzers.Test;

/// <summary>
/// The Sonar rule files name the analyzer version they were made for. A version bump without .github/scripts/sonar-rules.sh
/// leaves the rules the new version adds out of them, and TreatWarningsAsErrors turns those into build errors.
/// </summary>
public sealed class SonarRulesTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Directory.Packages.props not found above the test's directory.");
    }

    [TestCase("sonar-main.globalconfig")]
    [TestCase("sonar-tests.globalconfig")]
    public void TheRuleFileNamesTheAnalyzerVersionInUse(string file)
    {
        var root = RepositoryRoot();
        var version = Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Packages.props")), "Include=\"SonarAnalyzer.CSharp\" Version=\"([^\"]+)\"").Groups[1].Value;
        var header = File.ReadLines(Path.Combine(root, "analyzers", file)).First();

        Assert.That(version, Is.Not.Empty);
        Assert.That(header, Does.Contain($"(SonarAnalyzer.CSharp {version})"), "run bash .github/scripts/sonar-rules.sh from the repository root");
    }
}
