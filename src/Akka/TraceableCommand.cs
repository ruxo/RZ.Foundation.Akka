using JetBrains.Annotations;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

[PublicAPI]
public record TraceableCommand<T>(ActivityId? ActivityId) where T: TraceableCommand<T>
{
    public T SwapActivity(ActivityId? activityId) => (T) this with { ActivityId = activityId };
}