using FractalMemory.Core.Application;
using FractalMemory.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

internal static class TestEnvironment
{
    public static ServiceProvider CreateServices(DateTimeOffset? now = null)
    {
        var services = new ServiceCollection();
        services.AddFractalMemoryCore();
        services.AddSingleton<IClock>(_ => new FakeClock(now ?? new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero)));
        return services.BuildServiceProvider();
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "fractalmem-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
