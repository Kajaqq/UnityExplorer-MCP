# Model Context Protocol (MCP) C# SDK

This document provides comprehensive documentation for the Model Context Protocol (MCP) C# SDK used to build the MCP server in this repository.
The SDK enables .NET applications to implement and interact with MCP clients and servers, standardizing how applications provide context to Large Language Models (LLMs).


## Overview

The Model Context Protocol (MCP) is an open protocol designed to standardize communication between applications and Large Language Models (LLMs). It provides a secure and structured way for LLMs to access various data sources and tools.

This C# SDK provides the necessary components to build both MCP clients and servers, facilitating integration with the broader MCP ecosystem.

- **Official MCP Documentation:** [modelcontextprotocol.io](https://modelcontextprotocol.io/)
- **Protocol Specification:** [spec.modelcontextprotocol.io](https://spec.modelcontextprotocol.io/)

## Packages

The SDK is distributed as a set of NuGet packages, each serving a specific purpose:

| Package                               | Description                                                                                                   |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `ModelContextProtocol`                | The main package, including hosting and dependency injection extensions for most projects.                      |
| `ModelContextProtocol.AspNetCore`     | Contains ASP.NET Core integrations for building HTTP-based MCP servers (Streamable HTTP and SSE transports).  |
| `ModelContextProtocol.Core`           | A minimal package with core client and low-level server APIs, suitable for projects requiring fewer dependencies. |

## Installation

Install the desired packages using the .NET CLI. For most applications, you will start with `ModelContextProtocol`.

```sh
# For standard applications and servers
dotnet add package ModelContextProtocol --prerelease

# For building ASP.NET Core based HTTP servers
dotnet add package ModelContextProtocol.AspNetCore --prerelease

# For client-only or minimal dependency scenarios
dotnet add package ModelContextProtocol.Core --prerelease
```

## Getting Started

### Creating an MCP Server

MCP servers expose tools, prompts, and resources to clients. The SDK provides helpers to quickly set up a server, either as a standalone console application (using Stdio) or as a web service (using HTTP).

#### Example: Stdio Server

A stdio server communicates over standard input and output, making it ideal for scenarios where a client application launches the server as a child process.

**`Program.cs`**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WeatherTools>(); // Discover tools in the WeatherTools class

await builder.Build().RunAsync();

[McpServerToolType]
public static class WeatherTools
{
    [McpServerTool, Description("Gets the weather for a given location.")]
    public static string GetWeather([Description("The location to get the weather for")] string location)
    {
        // In a real application, you would call a weather API here.
        return $"The weather in {location} is sunny and 75°F.";
    }
}
```

#### Example: ASP.NET Core HTTP Server

An HTTP server allows clients to connect over the network. The SDK integrates with ASP.NET Core to map MCP endpoints.

**`Program.cs`**
```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

// Add MCP server services with HTTP transport and discover tools
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<WeatherTools>();

var app = builder.Build();

// Map MCP endpoints (e.g., POST / for messages, GET / for streaming)
app.MapMcp();

app.Run();

[McpServerToolType]
public static class WeatherTools
{
    [McpServerTool, Description("Gets the weather for a given location.")]
    public static string GetWeather([Description("The location to get the weather for")] string location)
    {
        return $"The weather in {location} is sunny and 75°F.";
    }
}
```

### Creating an MCP Client

An MCP client connects to a server to consume its tools, prompts, and resources. The client can then integrate these capabilities, for example, by exposing them to an LLM.

```csharp
using ModelContextProtocol.Client;
using Microsoft.Extensions.AI;
using OpenAI; // Assuming usage of an OpenAI client

// 1. Configure the transport to connect to the server
// This example uses Stdio, launching the server process directly.
var clientTransport = new StdioClientTransport(new()
{
    Command = "dotnet",
    Arguments = ["run", "--project", "../MyMcpServerProject"]
});

// For an HTTP server, you would use HttpClientTransport:
// var clientTransport = new HttpClientTransport(new()
// {
//     Endpoint = new Uri("http://localhost:3001")
// });

// 2. Create and connect the MCP client
await using var mcpClient = await McpClient.CreateAsync(clientTransport);

// 3. List available tools from the server
var tools = await mcpClient.ListToolsAsync();
Console.WriteLine($"Found {tools.Count} tools:");
foreach (var tool in tools)
{
    Console.WriteLine($"- {tool.Name}: {tool.Description}");
}

// 4. Use the tools with an AI Chat Client (e.g., OpenAI)
// McpClientTool derives from AIFunction, enabling direct integration.
var openAIClient = new OpenAIClient("YOUR_API_KEY").GetChatClient("gpt-4o-mini");

var chatClient = openAIClient.AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var chatOptions = new ChatOptions { Tools = [.. tools] };
var response = await chatClient.GetResponseAsync("What is the weather in London?", chatOptions);

Console.WriteLine(response.Text);
```

## Key Concepts

### MCP Primitives

The core components exposed by an MCP server are called primitives. There are three types:

*   **Tools**: Functions that an LLM can call to perform actions or retrieve information (e.g., `get_weather`).
*   **Prompts**: Pre-defined templates that can be used to generate structured conversations or instructions for an LLM.
*   **Resources**: Data sources that can be read by clients or LLMs, identified by a URI (e.g., `file:///path/to/document.txt`).

### Defining Server Primitives

Primitives are defined as methods in a class and decorated with attributes.

*   **Tools**: Use `[McpServerToolType]` on a class and `[McpServerTool]` on a static or instance method. Use the `[Description]` attribute to document the tool and its parameters for the LLM.

    ```csharp
    [McpServerToolType]
    public class Calculator
    {
        [McpServerTool, Description("Adds two numbers.")]
        public static int Add([Description("The first number")] int a, [Description("The second number")] int b)
        {
            return a + b;
        }
    }
    ```

*   **Prompts**: Use `[McpServerPromptType]` on a class and `[McpServerPrompt]` on a method. The method should return a `ChatMessage` or `ChatMessage[]`.

    ```csharp
    [McpServerPromptType]
    public static class SystemPrompts
    {
        [McpServerPrompt, Description("A prompt to make the assistant act as a pirate.")]
        public static ChatMessage PiratePrompt() =>
            new(ChatRole.System, "You are a friendly pirate who says 'Ahoy!' a lot.");
    }
    ```

*   **Resources**: Use `[McpServerResourceType]` on a class and `[McpServerResource]` on a method. The attribute specifies a URI template.

    ```csharp
    [McpServerResourceType]
    public class MyResources
    {
        [McpServerResource("file:///{path}"), Description("Reads file content.")]
        public static async Task<string> ReadFile(string path)
        {
            return await File.ReadAllTextAsync(path);
        }
    }
    ```

### Server Configuration

The server is configured using a fluent builder pattern starting with `AddMcpServer()` on an `IServiceCollection`.

```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()        // Use standard I/O for communication
    .WithTools<Calculator>()           // Add tools from the Calculator class
    .WithPrompts<SystemPrompts>()      // Add prompts from the SystemPrompts class
    .WithResources<MyResources>();     // Add resources from the MyResources class
```

### Transports

Transports define how clients and servers communicate.

*   **Stdio Transport**: (`StdioServerTransport`, `StdioClientTransport`)
    *   Communicates over `stdin`/`stdout`.
    *   Used when the client launches the server as a child process.
    *   Supports a single session per process.

*   **HTTP Transport**: (`WithHttpTransport`, `HttpClientTransport`)
    *   Communicates over HTTP, supporting multiple concurrent sessions.
    *   `WithHttpTransport()` on the server maps the required endpoints.
    *   `HttpClientTransport` supports three modes:
        *   `HttpTransportMode.AutoDetect`: (Default) Tries Streamable HTTP first, then falls back to SSE.
        *   `HttpTransportMode.StreamableHttp`: A bidirectional streaming transport over HTTP.
        *   `HttpTransportMode.Sse`: Uses Server-Sent Events for server-to-client messages.

### Elicitation

Elicitation allows a server tool to request additional, structured information from the user during its execution.

*   **Server-Side**: A tool can call `server.ElicitAsync()` and provide a schema for the required information.

    ```csharp
    [McpServerTool]
    public async Task<string> OrderPizza(McpServer server, CancellationToken token)
    {
        var schema = new ElicitRequestParams.RequestSchema
        {
            Properties = { ["Topping"] = new ElicitRequestParams.StringSchema() }
        };
        var response = await server.ElicitAsync(new() { Message = "What topping?", RequestedSchema = schema }, token);

        if (response.IsAccepted)
        {
            return $"Ordering a pizza with {response.Content?["Topping"]}.";
        }
        return "Order cancelled.";
    }
    ```

*   **Client-Side**: The client provides an `ElicitationHandler` in `McpClientOptions` to handle these requests, typically by prompting the user.

    ```csharp
    var options = new McpClientOptions
    {
        Handlers = new()
        {
            ElicitationHandler = async (request, token) =>
            {
                Console.WriteLine(request.Message);
                Console.Write("Topping: ");
                var topping = Console.ReadLine();
                return new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["Topping"] = JsonSerializer.SerializeToElement(topping)
                    }
                };
            }
        }
    };
    await using var mcpClient = await McpClient.CreateAsync(transport, options);
    ```

### Progress Notifications

For long-running operations, a server can send progress updates to the client.

*   **Server-Side**: A tool can accept an `IProgress<ProgressNotificationValue>` parameter and call `Report()` to send updates. The client must have supplied a `progressToken` in the request.

    ```csharp
    [McpServerTool]
    public async Task LongRunningTool(
        RequestContext<CallToolRequestParams> context,
        McpServer server)
    {
        if (context.Params?.ProgressToken is { } token)
        {
            for (int i = 1; i <= 5; i++)
            {
                await Task.Delay(1000);
                await server.NotifyProgressAsync(token, new()
                {
                    Progress = i,
                    Total = 5,
                    Message = $"Step {i} complete."
                });
            }
        }
    }
    ```

*   **Client-Side**: The client provides an `IProgress<T>` implementation when calling the tool.

    ```csharp
    var progressHandler = new Progress<ProgressNotificationValue>(value =>
    {
        Console.WriteLine($"Progress: {value.Progress}/{value.Total} - {value.Message}");
    });
    await mcpClient.CallToolAsync("long_running_tool", progress: progressHandler);
    ```

### Filters and Authorization

Filters provide a middleware-like pipeline to intercept and process MCP requests. This is particularly useful for cross-cutting concerns like logging, caching, and authorization.

*   **Adding a Filter**: Use the `Add...Filter` extension methods on the `IMcpServerBuilder`.

    ```csharp
    builder.Services.AddMcpServer()
        .AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Before tool call...");
            var result = await next(context, cancellationToken);
            logger.LogInformation("After tool call...");
            return result;
        });
    ```

*   **Built-in Authorization**: The SDK provides built-in filters for authorization in ASP.NET Core.
    1.  Call `AddAuthorizationFilters()` on the `IMcpServerBuilder`.
    2.  Configure authentication and authorization in your `Program.cs`.
    3.  Decorate your tool, prompt, or resource methods with `[Authorize]` or `[AllowAnonymous]`.

    ```csharp
    // In Program.cs
    builder.Services.AddAuthentication(...);
    builder.Services.AddAuthorization();
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .AddAuthorizationFilters() // Enable authorization
        .WithTools<ProtectedTools>();

    var app = builder.Build();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp().RequireAuthorization(); // Protect MCP endpoints
    app.Run();

    // In your tools class
    [McpServerToolType]
    public class ProtectedTools
    {
        [McpServerTool]
        [Authorize(Roles = "Admin")] // Only users in the "Admin" role can call this.
        public static string AdminOnlyOperation() => "Admin operation successful.";

        [McpServerTool]
        [AllowAnonymous] // Anyone can call this.
        public static string PublicOperation() => "Public operation successful.";
    }
    ```

### Authentication

For ASP.NET Core servers, the SDK provides an authentication handler to advertise OAuth 2.0 requirements to clients.

*   **Server-Side**: Configure JWT Bearer authentication and add the MCP authentication handler.

    ```csharp
    builder.Services.AddAuthentication()
        .AddJwtBearer(...)
        .AddMcp(options =>
        {
            options.ResourceMetadata = new()
            {
                Resource = new Uri("http://localhost:3001/"),
                AuthorizationServers = { new Uri("https://my-oauth-server.com") },
                ScopesSupported = ["mcp:tools"],
            };
        });
    ```

*   **Client-Side**: Configure the `HttpClientTransport` with `ClientOAuthOptions`. The SDK will handle the OAuth 2.0 authorization code flow.

    ```csharp
    var transport = new HttpClientTransport(new()
    {
        Endpoint = new Uri("http://localhost:3001/"),
        OAuth = new()
        {
            RedirectUri = new Uri("http://localhost:1234/callback"),
            // The delegate handles opening a browser and capturing the auth code.
            AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            DynamicClientRegistration = new() { ClientName = "MyMcpClient" },
        }
    });
    ```
