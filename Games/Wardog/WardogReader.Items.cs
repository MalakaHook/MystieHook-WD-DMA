using System.Text;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    private readonly Dictionary<uint, string> _displayNames = new();
    private bool _displayPoolValidated;

    private string DisplayObjectName(uint id)
    {
        // Supplied dumper: GNames=CE6CA30, blocks+20, 8-byte slots,
        // header+8, kind+A, text+C. Shared by item/vehicle/map display
        // readers; does not alter bone mappings or faction resolution.
        if (!_displayPoolValidated)
            _displayPoolValidated = DecodeDisplayName(0) == "None";
        if (!_displayPoolValidated) return Name(id);
        if (_displayNames.TryGetValue(id, out string cached)) return cached;
        string value = DecodeDisplayName(id);
        if (value.Length == 0) return ""; // Failed reads must be retried.
        if (_displayNames.Count >= 2048) _displayNames.Clear();
        _displayNames[id] = value;
        return value;
    }

    private string DecodeDisplayName(uint id)
    {
        if ((id >> 16) >= 4096) return "";
        ulong block = Ptr(moduleBase + O.GNames + 0x20 + (id >> 16) * 8UL);
        if (block == 0) return "";
        ulong entry = block + (id & 0xFFFF) * 8UL;
        Span<byte> prefix = stackalloc byte[3];
        if (!read(entry + 8, prefix)) return "";
        ushort header = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        byte kind = prefix[2];
        if (kind > 2) return "";
        int length = kind == 2 ? 22 : header >> 6;
        if (length is <= 0 or > 512) return "";
        bool wide = (header & 1) != 0;
        int byteCount = length * (wide ? 2 : 1);
        // A name cannot extend beyond its allocated pool block.
        if ((id & 0xFFFF) * 8UL + 12UL + (ulong)byteCount > 65536UL * 8) return "";
        byte[] bytes = new byte[byteCount];
        if (!read(entry + 12, bytes)) return "";
        string value = (wide ? Encoding.Unicode : Encoding.UTF8).GetString(bytes);
        return value.Any(char.IsControl) || value.Contains('\uFFFD') ? "" : value;
    }
}
