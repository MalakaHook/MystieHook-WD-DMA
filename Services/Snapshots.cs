using System.Threading;

namespace MystieHook.Services;

public static class Snapshots
{
    private static AppSnapshot _current = AppSnapshot.Empty;

    public static AppSnapshot Current => Volatile.Read(ref _current);

    public static void Publish(AppSnapshot s) => Volatile.Write(ref _current, s);

    // Functional update helper
    public static void Mutate(System.Func<AppSnapshot, AppSnapshot> mutate)
        => Publish(mutate(Current));
}
