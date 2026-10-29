using System.Xml.Linq;

namespace DevWorkflow.Tests.Architecture;

/// <summary>
/// Guards the dependency direction between projects by reading the project files,
/// so a forbidden reference fails the tests even before any code uses it.
/// </summary>
public class ProjectLayeringTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["DevWorkflow.Domain"] = [],
        ["DevWorkflow.Application"] = ["DevWorkflow.Domain"],
        ["DevWorkflow.Infrastructure"] = ["DevWorkflow.Application"],
        ["DevWorkflow.Api"] = ["DevWorkflow.Application", "DevWorkflow.Infrastructure"],
        ["DevWorkflow.Mcp"] = ["DevWorkflow.Application", "DevWorkflow.Infrastructure"],
    };

    public static TheoryData<string> Projects => [.. AllowedReferences.Keys];

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_references_only_allowed_layers(string project)
    {
        var references = ReadProjectReferences(project);

        var forbidden = references.Except(AllowedReferences[project]).ToArray();

        Assert.True(
            forbidden.Length == 0,
            $"{project} must not reference: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Every_source_project_has_a_layering_rule()
    {
        var sourceProjects = Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        Assert.Equal(AllowedReferences.Keys.Order(StringComparer.Ordinal), sourceProjects);
    }

    private static string[] ReadProjectReferences(string project)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, $"{project}.csproj");

        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")!.Value)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevWorkflow.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find DevWorkflow.slnx above the test output directory.");
    }
}
