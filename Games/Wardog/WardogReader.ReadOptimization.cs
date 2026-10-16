using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    private readonly List<WardogFrameMemory.Range> _focusedRanges = new();
    private readonly HashSet<int> _focusedBones = new();

    private void PrefetchPlayerLinks(ulong[] states)
    {
        _focusedRanges.Clear();
        foreach (ulong state in states)
            if (Pointer(state)) _focusedRanges.Add(new(state + O.PlayerStatePawn, 8));
        MarkReadOperation();
        prefetch(_focusedRanges);
        _focusedRanges.Clear();
        foreach (ulong state in states)
        {
            if (!Pointer(state)) continue;
            ulong actor = Ptr(state + O.PlayerStatePawn);
            if (!Pointer(actor)) continue;
            _focusedRanges.Add(new(actor + O.UObjectClass, 8));
            _focusedRanges.Add(new(actor + O.ActorRootComponent, 8));
        }
        MarkReadOperation();
        prefetch(_focusedRanges);
        _focusedRanges.Clear();
        foreach (ulong state in states)
        {
            if (!Pointer(state)) continue;
            ulong actor = Ptr(state + O.PlayerStatePawn);
            if (!Pointer(actor)) continue;
            ulong root = Ptr(actor + O.ActorRootComponent);
            if (Pointer(root)) _focusedRanges.Add(new(root + O.SceneRelativeLocation, 24));
        }
        MarkReadOperation();
        prefetch(_focusedRanges);
        // Vitality/faction/mesh/pose acquisition now follows the existing demand,
        // range, team, life-state and visibility gates rather than preceding them.
    }

    private void PrefetchSelectedBones(Arr array, int[] selected, BoneMap map,
        ulong sourceOffset, bool full, bool boundsOnly)
    {
        if (!_focusedPlayerReads || prefetch == null) return;
        _focusedRanges.Clear(); _focusedBones.Clear();
        void Add(int index)
        {
            if (index >= 0 && index < array.Count && _focusedBones.Add(index))
                _focusedRanges.Add(new(array.Data + (ulong)index * O.FTransformSize, (int)O.FTransformSize));
        }
        for (int slot = 0; slot < 22; slot++)
        {
            if (slot is 9 or 13 or 17 or 21) continue;
            bool core = slot is 0 or 1 or 2 or 5, foot = slot is 8 or 16;
            if (!core && (!full || (boundsOnly && !foot))) continue;
            int index = selected[slot]; Add(index);
            if (sourceOffset != 0xA08 || map == null || index < 0 || index >= map.Infos.Length) continue;
            int parent = map.Infos[index].Parent, depth = 0;
            while (parent >= 0 && parent < map.Infos.Length && ++depth <= 128)
            { Add(parent); parent = map.Infos[parent].Parent; }
        }
        MarkReadOperation();
        prefetch(_focusedRanges);
    }
}
