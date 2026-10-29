using System.Text.Json;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Infrastructure.Security;
using ModelContextProtocol.Protocol;

namespace DevWorkflow.Mcp.Tools;

/// <summary>Marks the MCP assembly for test hosts.</summary>
public sealed class McpAssemblyMarker;

/// <summary>
/// Runs a tool body and returns either its JSON result or a structured error
/// (<c>{"error":{"kind","code","message"}}</c> with <c>isError</c> set), matching the API's problem codes.
/// </summary>
internal static class ToolResults
{
    public static Caller CallerFrom(IHttpContextAccessor accessor) =>
        (accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context for this MCP request.")).User.ToCaller();

    public static async Task<CallToolResult> RunAsync<T>(Func<Task<T>> body)
    {
        try
        {
            return Success(await body());
        }
        catch (Exception exception) when (WorkflowError.From(exception) is { } error)
        {
            return Failure(error);
        }
    }

    private static CallToolResult Success<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, WorkflowJson.Options);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element.ValueKind == JsonValueKind.Object ? element : Wrap(element),
        };
    }

    private static CallToolResult Failure(WorkflowError error)
    {
        var element = JsonSerializer.SerializeToElement(
            new { Error = new { error.Kind, error.Code, error.Message } },
            WorkflowJson.Options);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element,
        };
    }

    // Structured content must be a JSON object, so lists are wrapped as {"items": [...]}.
    private static JsonElement Wrap(JsonElement element) =>
        JsonSerializer.SerializeToElement(new { Items = element }, WorkflowJson.Options);
}
