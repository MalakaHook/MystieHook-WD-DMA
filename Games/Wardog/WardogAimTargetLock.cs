using System.Numerics;

namespace MystieHook.Games.Wardog;

// One identity per hold. Temporary missing samples pause output without releasing identity.
// Single output-worker owner; immutable identity can also constrain render snapshots.
public sealed class WardogAimTargetLock
{
    public sealed record Identity(ulong World, ulong Controller, ulong LocalPawn, ulong Address, ulong State, ulong Mesh, ulong Asset)
    {
        public WardogReader.Frame Filter(WardogReader.Frame frame)
        {
            if (!frame.IsFresh || frame.WorldAddress != World || frame.LocalController != Controller || frame.LocalPawn != LocalPawn)
                return WardogReader.Frame.Empty("Aim target context changed");
            var player = frame.Players.FirstOrDefault(p => p.Address == Address && p.SourceState == State && p.SourceMesh == Mesh && p.SourceAsset == Asset);
            return player == null ? WardogReader.Frame.Empty("Aim target lost") : frame with { Players = [player] };
        }
    }
    public Identity Target { get; private set; }
    private bool _blocked;
    public bool AwaitingRelease=>_blocked;
    private int _key;
    public void Block() { _blocked = true; Target = null; }

    public Vector2? Select(WardogReader.Frame frame, WardogAimSettings settings, int width, int height,
        bool held, bool outputAllowed, out ulong address)
    {
        address = 0;
        if (!held) { _blocked = false; Target = null; _key = settings.Key; return null; }
        if (!settings.Enabled || (_key != 0 && _key != settings.Key)) Block();
        _key = settings.Key;
        if (_blocked) return null;
        if (!outputAllowed || !frame.IsFresh) return null;
        if (Target == null)
        {
            var acquisition = settings.UsePrediction ? settings with {TeamCheck=true} : settings;
            if (WardogAimSettings.SelectPoint(frame, acquisition, width, height, out var player, out _) == null)
            { return null; }
            if(player.SourceState==0||player.SourceMesh==0||player.SourceAsset==0)return null;
            Target = new(frame.WorldAddress, frame.LocalController, frame.LocalPawn, player.Address, player.SourceState, player.SourceMesh, player.SourceAsset);
        }
        var locked = frame.Players.FirstOrDefault(p => p.Address == Target.Address);
        if (frame.WorldAddress != Target.World || frame.LocalController != Target.Controller || frame.LocalPawn != Target.LocalPawn
            || (locked != null && ((locked.SourceState!=0&&locked.SourceState != Target.State) || (locked.SourceMesh!=0&&locked.SourceMesh != Target.Mesh) || (locked.SourceAsset!=0&&locked.SourceAsset != Target.Asset)
                || locked.Dead || locked.Health == 0 || ((settings.TeamCheck || settings.UsePrediction) && locked.TeamRelation == WardogReader.Relation.Friendly))))
        { Block(); return null; }
        var point = WardogAimSettings.Select(Target.Filter(frame), settings, width, height, out address);
        if (point == null) address = 0;
        return point;
    }
}
