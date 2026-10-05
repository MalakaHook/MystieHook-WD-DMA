namespace MystieHook.Services;

public readonly record struct DiagnosticError(DateTimeOffset TimeUtc, string Category, string Description);

internal static class DiagnosticErrors
{
    private static readonly Queue<DiagnosticError> Recent = new();
    private static readonly object Sync = new();
    // Export only fixed descriptions, never raw exception/log text, keys, paths or addresses.
    internal static DiagnosticError? Classify(string message, DateTimeOffset time) => message switch
    {
        var m when m.StartsWith("Unhandled exception:", StringComparison.Ordinal) => new(time, "Application", "Unhandled application exception."),
        var m when m.StartsWith("Startup/application failure:", StringComparison.Ordinal) => new(time, "Startup", "Startup or application failure."),
        var m when m.StartsWith("Camera worker:", StringComparison.Ordinal) => new(time, "Camera", "Camera worker stopped after an error."),
        var m when m.StartsWith("Camera stall:", StringComparison.Ordinal) => new(time, "Camera", "Camera update exceeded its timing budget."),
        _ => null
    };
    internal static void Record(string message)
    {
        var entry = Classify(message, DateTimeOffset.UtcNow);
        if (entry is null) return;
        lock (Sync) { if (Recent.Count == 64) Recent.Dequeue(); Recent.Enqueue(entry.Value); }
    }
    public static DiagnosticError[] Snapshot() { lock (Sync) return Recent.ToArray(); }
}
