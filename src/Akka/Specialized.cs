using Akka.Actor;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

/// <summary>
/// A message that knows how to turn an <see cref="ErrorInfo"/> into the reply object that should be
/// sent back to a requester (for example a failed <c>Outcome&lt;T&gt;</c>).
/// </summary>
[PublicAPI]
public interface ICanRaiseError
{
    /// <summary>Builds the reply object that represents the given <paramref name="error"/>.</summary>
    /// <param name="error">The error to convert into a reply.</param>
    /// <returns>The reply object describing the failure.</returns>
    object RaiseError(ErrorInfo error);
}

/// <summary>Extension helpers for sending error replies from <see cref="ICanRaiseError"/> messages.</summary>
[PublicAPI]
public static class CanRaiseErrorExtensions
{
    /// <summary>
    /// Converts <paramref name="error"/> into a reply via <see cref="ICanRaiseError.RaiseError(ErrorInfo)"/> and
    /// tells it to <paramref name="target"/>.
    /// </summary>
    /// <param name="i">The message that produces the error reply.</param>
    /// <param name="target">The actor the error reply is sent to.</param>
    /// <param name="error">The error to signal.</param>
    /// <returns>The reply message that was sent.</returns>
    public static object RaiseError(this ICanRaiseError i, IActorRef target, ErrorInfo error) {
        var message = i.RaiseError(error);
        target.Tell(message);
        return message;
    }
}

/// <summary>
/// Base record for request/response messages whose reply is an <c>Outcome&lt;T&gt;</c>. Provides helpers to
/// ask an actor for the response and to reply with a success, an existing outcome, or an error.
/// </summary>
/// <typeparam name="T">The type of the successful response value.</typeparam>
[PublicAPI]
public abstract record CanResponse<T> : ICanRaiseError
{
    /// <summary>
    /// Asks <paramref name="target"/> with this message and returns the resulting <c>Outcome&lt;T&gt;</c>. Never
    /// throws: any exception (including ask timeouts) is captured and returned as a failed outcome.
    /// </summary>
    /// <param name="target">The actor to ask.</param>
    /// <param name="timeout">The ask timeout; defaults to <see cref="AkkaInstaller.DefaultAskTimeout"/> seconds when not specified.</param>
    /// <returns>The actor's <c>Outcome&lt;T&gt;</c> reply, or a failed outcome built from any thrown exception.</returns>
    public async ValueTask<Outcome<T>> RequestTo(ICanTell target, TimeSpan? timeout = null) {
        try{
            return await target.Ask<Outcome<T>>(this, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
        }
        catch (Exception e){
            return ErrorFrom.Exception(e);
        }
    }

    /// <summary>
    /// Wraps <paramref name="data"/> in a success <c>Outcome&lt;T&gt;</c> and tells it to <paramref name="target"/>
    /// with no sender.
    /// </summary>
    /// <param name="target">The actor to reply to.</param>
    /// <param name="data">The successful response value.</param>
    /// <returns>The success outcome that was sent.</returns>
    public Outcome<T> Respond(IActorRef target, T data) {
        var message = SuccessOutcome(data);
        target.Tell(message, ActorRefs.NoSender);
        return message;
    }

    /// <summary>Tells an existing <c>Outcome&lt;T&gt;</c> reply to <paramref name="target"/> with no sender.</summary>
    /// <param name="target">The actor to reply to.</param>
    /// <param name="data">The outcome (success or failure) to send.</param>
    /// <returns>The same outcome that was sent.</returns>
    public Outcome<T> Respond(IActorRef target, Outcome<T> data) {
        target.Tell(data, ActorRefs.NoSender);
        return data;
    }

    /// <summary>Builds a failed <c>Outcome&lt;T&gt;</c> from <paramref name="error"/> as the reply object.</summary>
    /// <param name="error">The error to signal.</param>
    /// <returns>A failed <c>Outcome&lt;T&gt;</c> carrying <paramref name="error"/>.</returns>
    public object RaiseError(ErrorInfo error) => FailedOutcome<T>(error);
}

/// <summary>
/// A traceable message that can produce a copy of itself with a replaced <see cref="ActivityId"/> for
/// distributed-tracing correlation.
/// </summary>
[PublicAPI]
public interface ICanSwapActivity
{
    /// <summary>Returns a copy of this message with its activity id replaced by <paramref name="activityId"/>.</summary>
    /// <param name="activityId">The new activity id, or <c>null</c> to clear it.</param>
    /// <returns>A copy of the message carrying the new activity id.</returns>
    object SwapActivity(ActivityId? activityId);
}

/// <summary>
/// Strongly-typed variant of <see cref="ICanSwapActivity"/> whose activity-swap returns the concrete
/// message type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The self message type returned by the swap.</typeparam>
[PublicAPI]
public interface ICanSwapActivity<out T> : ICanSwapActivity where T : ICanSwapActivity<T>
{
    /// <summary>
    /// Strongly-typed version of <see cref="ICanSwapActivity.SwapActivity(ActivityId?)"/> returning <typeparamref name="T"/>.
    /// </summary>
    /// <param name="activityId">The new activity id, or <c>null</c> to clear it.</param>
    /// <returns>A copy of the message, typed as <typeparamref name="T"/>, carrying the new activity id.</returns>
    T SwapActivityT(ActivityId? activityId);
}

/// <summary>
/// Base record for fire-and-forget messages that carry an <see cref="ActivityId"/> for distributed tracing
/// and can be copied with a different activity id.
/// </summary>
/// <typeparam name="T">The concrete command type (curiously recurring, used as the self type).</typeparam>
/// <param name="ActivityId">The tracing activity id carried by the command, or <c>null</c>.</param>
[PublicAPI]
public abstract record TraceableCommand<T>(ActivityId? ActivityId) : ICanSwapActivity<T> where T: TraceableCommand<T>
{
    /// <inheritdoc />
    public T SwapActivityT(ActivityId? activityId) => (T) this with { ActivityId = activityId };

    /// <inheritdoc />
    public object SwapActivity(ActivityId? activityId) => SwapActivityT(activityId);
}

/// <summary>
/// Base record for request/response messages that also carry an <see cref="ActivityId"/>: combines the
/// <c>Outcome&lt;TRes&gt;</c> reply helpers of <see cref="CanResponse{T}"/> with activity-id swapping for tracing.
/// </summary>
/// <typeparam name="TSelf">The concrete message type (curiously recurring, used as the self type).</typeparam>
/// <typeparam name="TRes">The type of the successful response value.</typeparam>
/// <param name="ActivityId">The tracing activity id carried by the message, or <c>null</c>.</param>
[PublicAPI]
public abstract record TraceableResponder<TSelf,TRes>(ActivityId? ActivityId) : CanResponse<TRes>, ICanSwapActivity<TSelf> where TSelf : TraceableResponder<TSelf,TRes>
{
    /// <inheritdoc />
    public TSelf SwapActivityT(ActivityId? activityId) => (TSelf) this with { ActivityId = activityId };

    /// <inheritdoc />
    public object SwapActivity(ActivityId? activityId) => SwapActivityT(activityId);
}
