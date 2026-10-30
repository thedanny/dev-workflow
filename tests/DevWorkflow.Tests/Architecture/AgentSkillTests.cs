using System.Reflection;
using System.Text.RegularExpressions;
using DevWorkflow.Application.Security;
using DevWorkflow.Mcp.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace DevWorkflow.Tests.Architecture;

/// <summary>Keeps the local executor skill in step with the MCP tools it tells agents to call.</summary>
public sealed partial class AgentSkillTests
{
    private static readonly string SkillPath = Path.Combine(RepositoryRoot(), "agents", "skills", "dev-workflow-executor", "SKILL.md");

    [Fact]
    public void The_skill_names_every_executor_tool_and_no_tool_an_executor_cannot_call()
    {
        var skill = File.ReadAllText(SkillPath);
        var executorTools = Tools().Where(t => t.Roles.Contains(RoleNames.Executor)).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var allTools = Tools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var mentioned = CodeSpan().Matches(skill).Select(m => m.Groups[1].Value).Where(allTools.Contains).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(executorTools.Except(mentioned));
        Assert.Empty(mentioned.Except(executorTools));
    }

    [Fact]
    public void The_skill_frontmatter_names_its_folder()
    {
        var lines = File.ReadLines(SkillPath).Take(4).ToList();

        Assert.Equal("---", lines[0]);
        Assert.Equal("name: dev-workflow-executor", lines[1]);
        Assert.StartsWith("description: ", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void No_mcp_tool_can_merge_or_deploy()
    {
        var forbidden = new[] { "merge", "deploy", "release_to", "protect" };

        Assert.DoesNotContain(Tools(), t => forbidden.Any(word => t.Name.Contains(word, StringComparison.Ordinal)));
    }

    private static IEnumerable<(string Name, string[] Roles)> Tools() =>
        from type in typeof(ExecutorTools).Assembly.GetTypes()
        where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
        from method in type.GetMethods()
        let tool = method.GetCustomAttribute<McpServerToolAttribute>()
        where tool?.Name is not null
        let roles = method.GetCustomAttribute<AuthorizeAttribute>()?.Roles ?? type.GetCustomAttribute<AuthorizeAttribute>()?.Roles ?? ""
        select (tool.Name!, roles.Split(','));

    private static string RepositoryRoot()
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

    [GeneratedRegex(@"`([a-z]+(?:_[a-z]+)+)(?:\(|`)")]
    private static partial Regex CodeSpan();
}
