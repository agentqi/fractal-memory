using FractalMemory.Core.Application.Services;

namespace FractalMemory.Core.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
