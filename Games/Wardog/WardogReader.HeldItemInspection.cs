using System.Diagnostics;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    public sealed record HeldItemInspection(ulong World, ulong Controller, ulong Pawn, long SampleStarted, string Report);

    // A single local held-item capture, with no object enumeration or memory writes.
    public HeldItemInspection InspectHeldItem(Frame frame)
    {
        if (!frame.IsFresh || !Pointer(frame.LocalPawn)) return null;
        long started = Stopwatch.GetTimestamp();
        if (!Read(frame.LocalPawn+O.PlayerInventory,out ulong inventory) || !Pointer(inventory)
            || !Read(inventory+O.InventoryHeldItem,out ulong held)) return null;
        if (held == 0 && O.InventoryActiveItem == 0) return null;
        ulong offset=held==0?O.InventoryActiveItem:O.InventoryHeldItem;
        if (!Read(inventory+offset,out ulong item) || !Pointer(item)) return null;
        byte[] identity=new byte[0x1C];
        if (!read(item+0xC,identity)) return null;
        ulong type=BitConverter.ToUInt64(identity,4);
        var fields=new List<string> {"Held-item read-only capture v1", $"Selection: {(held==0?"active fallback":"held")}",
            $"Item: 0x{item:X}; object header: {Convert.ToHexString(identity)}",
            $"Display pool validated: {DecodeDisplayName(0)=="None"}"};
        bool tagRead=Read(item+O.ItemId,out ulong tag);
        if(tagRead) Describe("ItemId",tag); else fields.Add("ItemId: unreadable");
        Describe("Object name",BitConverter.ToUInt64(identity,12));
        ulong className=0;
        bool classRead=Pointer(type) && Read(type+O.UObjectNameId,out className);
        if(classRead) Describe("Class name",className);
        else fields.Add("Class name: unreadable");
        fields.Add("Current overlay label: "+Display(ReadCurrentItem(frame.LocalPawn)));
        byte[] finalIdentity=new byte[identity.Length];
        if (!Read(frame.LocalPawn+O.PlayerInventory,out ulong finalInventory) || finalInventory!=inventory
            || !Read(inventory+O.InventoryHeldItem,out ulong finalHeld) || finalHeld!=held
            || !Read(inventory+offset,out ulong finalItem) || finalItem!=item
            || !read(item+0xC,finalIdentity) || !identity.AsSpan().SequenceEqual(finalIdentity)
            || Read(item+O.ItemId,out ulong finalTag)!=tagRead || tagRead && finalTag!=tag
            || classRead && (!Read(type+O.UObjectNameId,out ulong finalClassName) || finalClassName!=className)
            || !Read(moduleBase+O.GWorld,out ulong world) || world!=frame.WorldAddress) return null;
        return new(frame.WorldAddress,frame.LocalController,frame.LocalPawn,started,string.Join("\n",fields));

        static string Display(string value)=>string.IsNullOrEmpty(value)?"<unresolved>":value;
        void Describe(string label,ulong name)
        {
            uint id=(uint)name;
            fields.Add($"{label}: 0x{name:X16}; index={id}; number={name>>32}");
            fields.Add($"  Current decoder: {Display(DecodeDisplayName(id))}; display fallback: {Display(DisplayObjectName(id))}");
            if((id>>16)>=4096)return;
            ulong block=Ptr(moduleBase+O.GNames+0x20+(id>>16)*8UL);
            if(block==0){fields.Add("  Pool block unreadable");return;}
            byte[] prefix=new byte[3];
            if(read(block+(id&65535)*8UL+8,prefix))
                fields.Add($"  Header: 0x{BitConverter.ToUInt16(prefix):X4}; kind: {prefix[2]}");
            else fields.Add("  Entry header unreadable");
        }
    }

    public static bool HeldItemInspectionMatches(HeldItemInspection sample,Frame frame)
        => sample!=null && frame.IsFresh && sample.World==frame.WorldAddress
            && sample.Controller==frame.LocalController && sample.Pawn==frame.LocalPawn
            && Stopwatch.GetElapsedTime(sample.SampleStarted).TotalMilliseconds is >=0 and <250;
}
