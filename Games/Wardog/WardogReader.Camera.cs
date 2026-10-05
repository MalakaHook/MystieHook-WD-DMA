using System.Diagnostics;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    public sealed record CameraFrame(ulong World, ulong Controller, Camera Camera, long SampleStarted)
    {
        public static readonly CameraFrame Empty = new(0, 0, default, 0);
        public bool IsFresh => World != 0 && Controller != 0 && SampleStarted != 0
            && Stopwatch.GetElapsedTime(SampleStarted).TotalMilliseconds < 100;
    }

    // Context validity is independent of player pose age. Rendering still requires both
    // snapshots to be fresh; a slow player scan must not stop camera acquisition.
    public static bool CanCaptureCamera(Frame context) => context.Valid
        && Pointer(context.LocalController) && Pointer(context.WorldAddress);

    // This reader instance belongs solely to the camera worker.
    public CameraFrame CaptureCamera(Frame context)
    {
        long started = Stopwatch.GetTimestamp();
        if (!CanCaptureCamera(context)) return CameraFrame.Empty;
        ulong world = Ptr(moduleBase + O.GWorld);
        if (world != context.WorldAddress) return CameraFrame.Empty;
        if (!ReadCamera(context.LocalController, out var camera)) return CameraFrame.Empty;
        if (Ptr(moduleBase + O.GWorld) != world) return CameraFrame.Empty;
        return new(world, context.LocalController, camera, started);
    }

    public static Frame WithCamera(Frame players, CameraFrame camera)
    {
        if (!players.IsFresh || camera is not { IsFresh: true } || camera.World != players.WorldAddress
            || camera.Controller != players.LocalController) return Frame.Empty("Waiting for matching fresh camera/player data");
        // Preserve the original player timestamp: new camera data never refreshes stale players.
        return players with { Camera = camera.Camera };
    }
}
