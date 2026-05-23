using FractalMemory.Core.Application;
using FractalMemory.McpServer;
using FractalMemory.McpServer.Prompts;
using FractalMemory.McpServer.Resources;
using FractalMemory.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddFractalMemoryCore();

builder.Services.AddSingleton<IMcpRepositoryContext, McpRepositoryContext>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<MemoryTools>()
    .WithResources<MemoryResources>()
    .WithPrompts<MemoryPrompts>();

var app = builder.Build();
await app.RunAsync();
