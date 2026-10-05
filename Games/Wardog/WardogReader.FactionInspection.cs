using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    public sealed record FactionInspection(ulong World, ulong Controller, ulong Pawn, long SampleStarted,
        string[] Fields);

    // A separate reader owns this diagnostic pass. Never uses the gameplay identity cache.
    public FactionInspection InspectFaction(Frame context, ulong pawn)
    {
        if (!context.IsFresh || !Pointer(pawn)) return null;
        var fields = new List<string>();
        long started = Stopwatch.GetTimestamp();
        ulong state = Ptr(pawn + WardogOffsets.PawnPlayerState);
        if (pawn == context.LocalPawn && state == 0)
            state = Ptr(context.LocalController + WardogOffsets.ControllerPlayerState);
        ulong component = state == 0 ? 0 : Ptr(state + WardogOffsets.PlayerStateFactionComponent);
        ulong actor = component == 0 ? 0 : Ptr(component + WardogOffsets.FactionComponentFaction);
        ulong data = actor == 0 ? 0 : Ptr(actor + WardogOffsets.FactionData);
        fields.Add($"PlayerState: {Address(state)}");
        fields.Add($"Faction component: {Address(component)}");
        fields.Add($"Faction actor: {Address(actor)}");
        fields.Add($"Faction data: {Address(data)}");
        Tag("Component tag +0x110", component, 0x110);
        Tag("Component tag +0x100", component, 0x100);
        Tag("Data tag +0x30", data, 0x30);
        fields.Add(data != 0 && Read(data + 0x108, out byte id)
            ? $"Numeric team ID: {id} (raw byte; no label inferred)" : "Numeric team ID: unavailable / read failed");
        CandidateText("Data FText +0x38", data, 0x38);
        CandidateText("Data FText +0x48", data, 0x48);
        // Reject address reuse or a changing chain instead of publishing a mixture.
        ulong finalState = Ptr(pawn + WardogOffsets.PawnPlayerState);
        if (pawn == context.LocalPawn && finalState == 0)
            finalState = Ptr(context.LocalController + WardogOffsets.ControllerPlayerState);
        if (state != finalState || (state != 0 && Ptr(state + WardogOffsets.PlayerStateFactionComponent) != component)
            || (component != 0 && Ptr(component + WardogOffsets.FactionComponentFaction) != actor)
            || (actor != 0 && Ptr(actor + WardogOffsets.FactionData) != data)) return null;
        return new(context.WorldAddress, context.LocalController, pawn, started, fields.ToArray());

        void Tag(string label, ulong owner, ulong offset)
        {
            if (owner == 0 || !Read(owner + offset, out ulong tag))
            { fields.Add(label + ": unavailable / read failed"); return; }
            string name = Name((uint)tag);
            fields.Add($"{label}: 0x{tag:X16} | index {(uint)tag} | number {tag >> 32} | " +
                (name.Length == 0 ? "name unresolved" : name));
        }
        void CandidateText(string label, ulong owner, ulong offset)
        {
            Span<byte> header = stackalloc byte[16];
            if (owner == 0 || !read(owner + offset, header))
            { fields.Add(label + ": unavailable / read failed"); return; }
            fields.Add(label + " raw: " + Convert.ToHexString(header));
            ulong textData = MemoryMarshal.Read<ulong>(header);
            // Explicit diagnostic hypothesis, not a generic FText decoder: text-data +0x28 FString.
            // Other histories (including string-table text) remain unresolved; never scan pointers.
            if (!Pointer(textData) || !Read(textData + 0x28, out Arr a) || !Pointer(a.Data)
                || a.Count < 1 || a.Count > 256 || a.Capacity < a.Count || a.Capacity > 1024)
            { fields.Add(label + " candidate text: unsupported layout / unreadable"); return; }
            byte[] bytes = new byte[a.Count * 2];
            if (!read(a.Data, bytes) || bytes[^1] != 0 || bytes[^2] != 0)
            { fields.Add(label + " candidate text: read failed / invalid terminator"); return; }
            try
            {
                string value = new UnicodeEncoding(false, false, true).GetString(bytes.AsSpan(0, bytes.Length - 2));
                if (value.Any(char.IsControl)) { fields.Add(label + " candidate text: invalid characters"); return; }
                fields.Add(label + " candidate text (unverified layout): " + value);
            }
            catch (DecoderFallbackException) { fields.Add(label + " candidate text: invalid UTF-16"); }
        }
        static string Address(ulong value) => value == 0 ? "unavailable / read failed" : $"0x{value:X}";
    }

    public static bool InspectionMatches(FactionInspection sample, Frame current, ulong requestedPawn) =>
        sample != null && current.IsFresh && sample.World == current.WorldAddress && sample.Controller == current.LocalController
        && sample.Pawn == requestedPawn && Stopwatch.GetElapsedTime(sample.SampleStarted).TotalSeconds <= 3
        && (requestedPawn == current.LocalPawn || current.Players.Any(p => p.Address == requestedPawn)
            || current.RadarPlayers.Any(p => p.Address == requestedPawn));
}
