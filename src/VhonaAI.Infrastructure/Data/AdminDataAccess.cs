namespace VhonaAI.Infrastructure.Data;

/// <summary>
/// Explicit bypass for one admin operation. Query filters stay on.
/// SaveChanges allows another business's rows only while this is open and the actor is the signed-in user.
/// AdminConsoleService is the only production caller.
/// </summary>
internal static class AdminDataAccess
{
    private static readonly AsyncLocal<Guid?> Actor = new();

    public static Guid? ActorUserId => Actor.Value;

    public static IDisposable Open(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }

        var previous = Actor.Value;
        Actor.Value = actorUserId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public Scope(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Actor.Value = _previous;
        }
    }
}
