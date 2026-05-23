using FractalMemory.Cli;
using FractalMemory.Core.Application;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFractalMemoryCore();

await using var provider = services.BuildServiceProvider();
return await CliRunner.RunAsync(args, provider);
