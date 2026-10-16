using System.Diagnostics;
using O=MystieHook.Games.Wardog.WardogOffsets;
using L=MystieHook.Games.Wardog.WardogPredictionSpeedLayout;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    public sealed record PredictionSpeedProof(ulong Address,byte[] Bytes);
    public sealed record PredictionSpeedSample(ulong World,ulong Controller,ulong Pawn,float Speed,long Stamp,long CheckedAt,string Status)
    {
        public PredictionSpeedProof[] Guards {get;init;}=[];
        public PredictionSpeedProof[] Identity {get;init;}=[];
        public bool CanRetain {get;init;}
        public bool InitialReadable {get;init;}
        public bool ModifierReadable {get;init;}
        public float InitialSpeed {get;init;}
        public float Modifier {get;init;}
        public double CaptureMilliseconds {get;init;}
        public bool CachedForWeapon {get;init;}
        public ulong HeldWeapon {get;init;}
        public ulong HeldItemId {get;init;}
        public ulong InitialAddress {get;init;}
        public ulong ModifierAddress {get;init;}
        // Cached numerical values have a separate lifetime. Consumers must still
        // freshly prove the current held identity before using one in a frame.
        public bool HasUsableSpeed=>!CachedForWeapon?IsFresh:Stamp!=0&&CheckedAt!=0
            &&float.IsFinite(Speed)&&Speed is >=2000 and <=200000&&Guards.Length>0&&Identity.Length>0
            &&Stopwatch.GetElapsedTime(Stamp).TotalMilliseconds>=0&&Stopwatch.GetElapsedTime(CheckedAt).TotalMilliseconds>=0;
        public bool IsFresh=>Stamp!=0&&float.IsFinite(Speed)&&Speed is >=2000 and <=200000
            &&Stopwatch.GetElapsedTime(Stamp).TotalMilliseconds is >=0 and <=150;
        public string FreshnessFailure
        {
            get
            {
                if(Stamp==0)return Status;
                double age=Stopwatch.GetElapsedTime(Stamp).TotalMilliseconds;
                if(age<0)return "Live bullet-speed sample timestamp is in the future";
                if(age>150)return FormattableString.Invariant($"Live bullet-speed sample expired ({age:F0} ms old; capture {CaptureMilliseconds:F0} ms; limit 150 ms)");
                return "Live bullet-speed sample value invalid";
            }
        }
        public bool Matches(Frame frame)=>frame is {IsFresh:true}&&frame.WorldAddress==World
            &&frame.LocalController==Controller&&frame.LocalPawn==Pawn&&CheckedAt!=0
            &&Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds is >=0 and <250
            &&Stopwatch.GetElapsedTime(CheckedAt).TotalMilliseconds>=0
            &&(CachedForWeapon||Stopwatch.GetElapsedTime(CheckedAt).TotalMilliseconds<=150);
        public static PredictionSpeedSample Empty(string status="Waiting for live bullet speed")=>new(0,0,0,0,0,0,status);
        public PredictionSpeedSample Retain(PredictionSpeedSample previous)
        {
            if(!CanRetain||previous==null||!previous.IsFresh||World!=previous.World||Controller!=previous.Controller||Pawn!=previous.Pawn
                ||Identity.Length==0||Identity.Length!=previous.Identity.Length
                ||InitialReadable&&InitialSpeed!=previous.InitialSpeed||ModifierReadable&&Modifier!=previous.Modifier)
                return this;
            for(int index=0;index<Identity.Length;index++)
                if(Identity[index].Address!=previous.Identity[index].Address||!Identity[index].Bytes.AsSpan().SequenceEqual(previous.Identity[index].Bytes))return this;
            return this with {Speed=previous.Speed,Stamp=previous.Stamp,InitialSpeed=previous.InitialSpeed,Modifier=previous.Modifier,CaptureMilliseconds=previous.CaptureMilliseconds,
                Status="Retained original bullet-speed sample after unreadable field"};
        }
    }

    // Attachment/worker-owned, bounded and memory-only. The key distinguishes
    // different instances of the same weapon model and different held ItemIds.
    public sealed class PredictionSpeedCache
    {
        public const int Capacity=16;
        private readonly Dictionary<(ulong Held,ulong ItemId),PredictionSpeedSample> _entries=new();
        private readonly List<(ulong Held,ulong ItemId)> _recent=new();
        private (ulong World,ulong Controller,ulong Pawn) _context;
        public int Count=>_entries.Count;
        public void Clear(){_entries.Clear();_recent.Clear();_context=default;}
        internal void Context(Frame frame)
        {
            var next=(frame.WorldAddress,frame.LocalController,frame.LocalPawn);
            if(next!=_context){Clear();_context=next;}
        }
        internal PredictionSpeedSample Find(ulong held,ulong itemId)
        {
            var key=(held,itemId);
            if(!_entries.TryGetValue(key,out var sample))return null;
            _recent.Remove(key);_recent.Add(key);return sample;
        }
        internal void Remove(ulong held,ulong itemId)
        {var key=(held,itemId);_entries.Remove(key);_recent.Remove(key);}
        internal bool Contains(PredictionSpeedSample sample)=>sample!=null&&sample.CachedForWeapon
            &&_entries.TryGetValue((sample.HeldWeapon,sample.HeldItemId),out var stored)
            &&stored.Stamp==sample.Stamp&&stored.Speed==sample.Speed;
        internal PredictionSpeedSample Store(PredictionSpeedSample sample)
        {
            if(!sample.IsFresh||!sample.InitialReadable||!sample.ModifierReadable||sample.Guards.Length==0
                ||sample.Identity.Length==0||!Pointer(sample.HeldWeapon))return sample;
            var key=(sample.HeldWeapon,sample.HeldItemId);
            if(!_entries.ContainsKey(key)&&_entries.Count==Capacity){_entries.Remove(_recent[0]);_recent.RemoveAt(0);}
            var cached=sample with {CachedForWeapon=true,CanRetain=false,Status="Weapon bullet speed cached"};
            _entries[key]=cached;_recent.Remove(key);_recent.Add(key);return cached;
        }
    }

    private sealed class PredictionSpeedBudgetException:Exception;
    // The monitor invokes this only with prediction demand, at 20 Hz, through
    // Vehicle priority. Code is data; no actor/global enumeration or execution.
    public PredictionSpeedSample CapturePredictionSpeed(Frame frame,bool enabled=true,long captureStarted=0,PredictionSpeedCache cache=null)
    {
        long started=captureStarted==0?Stopwatch.GetTimestamp():captureStarted;
        if(!enabled)return PredictionSpeedSample.Empty("Bullet-speed sampling disabled");
        if(frame is not {IsFresh:true}||Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds<0
            ||!Pointer(frame.WorldAddress)||!Pointer(frame.LocalController)||!Pointer(frame.LocalPawn))
            return PredictionSpeedSample.Empty("Player context unavailable or expired for bullet-speed sampling");
        double initialAge=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if(started<=0||initialAge<0)return PredictionSpeedSample.Empty("Bullet-speed capture timestamp is in the future or invalid");
        if(initialAge>=150)return PredictionSpeedSample.Empty("Original bullet-speed context expired before sampling");
        cache?.Context(frame);
        int calls=0,bytes=0;
        void Budget(int count,int length)
        {
            if((calls+=count)>256||(bytes+=length)>65536)throw new PredictionSpeedBudgetException();
        }
        bool Bounded(ulong address,Span<byte> output)
        {
            if(output.Length>4096)throw new PredictionSpeedBudgetException();
            Budget(1,output.Length);return read(address,output);
        }
        void Verify(IReadOnlyCollection<WardogReadBatch.Request> requests)
        {
            Budget(requests.Count,requests.Sum(r=>r.Bytes.Length));verifyScatter(requests);
        }
        try{return new WardogReader(moduleBase,Bounded,verifyScatter:verifyScatter==null?null:Verify,
            readSampleStarted:readSampleStarted).CapturePredictionSpeedCore(frame,started,cache);}
        catch(PredictionSpeedBudgetException){return PredictionSpeedSample.Empty("Bullet-speed read budget reached; sample discarded");}
    }

    private PredictionSpeedSample CapturePredictionSpeedCore(Frame frame,long started,PredictionSpeedCache cache)
    {
        string detail="";
        var proofs=new Dictionary<ulong,PredictionSpeedProof>();var guards=new HashSet<ulong>();
        PredictionSpeedSample Reject(string reason)=>PredictionSpeedSample.Empty(reason);
        byte[] Proof(ulong address,int length,string label,bool guard=false)
        {
            if(guard)guards.Add(address);
            if(proofs.TryGetValue(address,out var previous)&&previous.Bytes.Length==length)return previous.Bytes;
            var output=new byte[length];
            if(!Pointer(address)||!Pointer(address+(ulong)length-1)||!read(address,output)){detail=label+" unreadable";return null;}
            proofs[address]=new(address,output);return output;
        }
        ulong Link(ulong address,string label,bool guard=false)
        {
            var value=Proof(address,8,label,guard);if(value==null)return 0;
            ulong pointer=BitConverter.ToUInt64(value);if(!Pointer(pointer))detail=label+" unavailable";return pointer;
        }
        bool Header(ulong address,string label,bool guard=false)
        {
            if(!Pointer(address)){detail=label+" unavailable";return false;}
            var header=Proof(address+8,32,label+" header",guard);if(header==null)return false;
            int index=BitConverter.ToInt32(header,4);
            if((BitConverter.ToUInt32(header)&0x40000000)!=0||index is <0 or >2000000||!Pointer(BitConverter.ToUInt64(header,8)))
            {detail=label+" header invalid or pending destruction";return false;}
            ulong chunks=Link(moduleBase+O.GObjects,"Object-table root");if(!Pointer(chunks))return false;
            ulong chunk=Link(chunks+(ulong)index/O.ObjectsPerChunk*8,"Object-table chunk")&~0xFUL;if(!Pointer(chunk))return false;
            if(Link(chunk+(ulong)index%O.ObjectsPerChunk*O.ObjectItemSize+O.ObjectItemObject,label+" object slot",guard)!=address)
            {detail=label+" object-slot identity mismatch";return false;}return true;
        }
        if(Link(moduleBase+O.GWorld,"Current world")!=frame.WorldAddress)return Reject("World changed during bullet-speed sampling");
        ulong instance=Link(frame.WorldAddress+O.GameInstance,"Game instance");if(!Pointer(instance))return Reject(detail);
        var locals=Proof(instance+O.LocalPlayers,16,"Local-player list");if(locals==null)return Reject(detail);
        ulong list=BitConverter.ToUInt64(locals);int count=BitConverter.ToInt32(locals,8),capacity=BitConverter.ToInt32(locals,12);
        if(!Pointer(list)||count is <1 or >4||capacity<count||capacity>4)return Reject("Local-player list bounds unsupported");
        ulong local=Link(list,"Local player");
        if(!Pointer(local)||Link(local+O.LocalPlayerController,"Local controller")!=frame.LocalController)return Reject("Local controller changed");
        if(!Header(frame.LocalController,"Local controller")||!Header(frame.LocalPawn,"Local pawn"))return Reject(detail);
        // Match the pawn offset selected by the existing player reader.
        ulong pawnOffset=0;
        foreach(ulong candidate in new[]{O.ControllerPawnCurrent,O.ControllerPawn,O.ControllerPawnAlt})
        {
            ulong current=Link(frame.LocalController+candidate,"Controller pawn");
            if(!Pointer(current))continue;
            if(current!=frame.LocalPawn)return Reject("Local pawn changed during bullet-speed sampling");
            pawnOffset=candidate;break;
        }
        if(pawnOffset==0)return Reject("Local pawn changed or unavailable");guards.Add(frame.LocalController+pawnOffset);
        ulong inventory=Link(frame.LocalPawn+O.PlayerInventory,"Weapon inventory",true);if(!Header(inventory,"Weapon inventory",true))return Reject(detail);
        ulong held=Link(inventory+O.InventoryHeldItem,"Held weapon",true);if(!Header(held,"Held weapon",true))return Reject(detail);
        var itemId=Proof(held+O.ItemId,8,"Held weapon ItemId",true);if(itemId==null)return Reject(detail);
        ulong heldId=BitConverter.ToUInt64(itemId);
        var cached=cache?.Find(held,heldId);
        if(cached!=null)
        {
            var expected=cached.Identity.ToDictionary(p=>p.Address);
            bool sameInitial=proofs.Values.All(p=>expected.TryGetValue(p.Address,out var old)
                &&p.Bytes.AsSpan().SequenceEqual(old.Bytes));
            if(sameInitial)
            {
                var checks=cached.Identity.Select(p=>new WardogReadBatch.Request(p.Address,p.Bytes.Length)).ToArray();
                if(checks.Length>256||checks.Sum(r=>r.Bytes.Length)>65536||checks.Any(r=>r.Bytes.Length>4096))
                    return Reject("Cached weapon identity exceeds read bounds");
                if(verifyScatter!=null)verifyScatter(checks);
                else foreach(var check in checks)check.Success=read(check.Address,check.Bytes);
                // Unreadable identities pause consumers; they never authorize a
                // different weapon, and do not by themselves destroy its value.
                if(checks.Any(check=>!check.Success))return Reject("Cached weapon identity unreadable");
                bool unchanged=true;
                for(int i=0;i<checks.Length;i++)unchanged&=checks[i].Bytes.AsSpan().SequenceEqual(cached.Identity[i].Bytes);
                if(unchanged)
                {
                    if(!frame.IsFresh||Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds<0
                        ||Stopwatch.GetElapsedTime(started).TotalMilliseconds>=150)
                        return Reject("Cached weapon identity verification expired");
                    return cached with {CheckedAt=started,Status="Cached weapon bullet speed ready"};
                }
            }
            cache!.Remove(held,heldId);
            return Reject("Cached weapon identity changed; resampling bullet speed");
        }
        var dos=Proof(moduleBase,64,"DOS header");if(dos==null)return Reject(detail);
        int pe=BitConverter.ToInt32(dos,0x3C);
        if(BitConverter.ToUInt16(dos)!=0x5A4D||pe is <64 or >1048576)return Reject("Bullet-speed PE64 image bounds unavailable");
        var nt=Proof(moduleBase+(ulong)pe,88,"PE header");if(nt==null)return Reject(detail);
        uint size=BitConverter.ToUInt32(nt,80);
        if(BitConverter.ToUInt32(nt)!=0x4550||BitConverter.ToUInt16(nt,4)!=0x8664||BitConverter.ToUInt16(nt,20)<64
            ||BitConverter.ToUInt16(nt,24)!=0x20B||size>0x40000000||size<(ulong)pe+88||!Pointer(moduleBase+size-1))
            return Reject("Bullet-speed PE64 image bounds unsupported");
        bool Image(ulong address,int length)=>address>=moduleBase&&length>0&&(ulong)length<=size&&address-moduleBase<=size-(ulong)length;
        bool Code(ulong address,byte[] expected,string label)
        {
            if(!Image(address,expected.Length)){detail=label+" outside PE64 image";return false;}
            var code=Proof(address,expected.Length,label);
            if(code==null)return false;
            if(!code.AsSpan().SequenceEqual(expected)){detail=label+" layout unsupported";return false;}return true;
        }
        if(!Image(moduleBase+L.GetterRva,L.Getter.Length))return Reject("Initial bullet-speed getter outside PE64 image");
        var getter=Proof(moduleBase+L.GetterRva,L.Getter.Length,"Initial bullet-speed getter");
        if(getter==null)return Reject(detail);
        if(!L.TryGetterHelper(getter,L.GetterRva,size,out ulong speedHelper))return Reject("Initial bullet-speed getter layout unsupported or helper outside PE64 image");
        if(!Code(moduleBase+speedHelper,L.Helper,"Initial bullet-speed stats helper")
            ||!Code(moduleBase+L.ComponentWrapperRva,L.ComponentWrapper,"Actor component wrapper"))return Reject(detail);
        ulong table=Link(held,"Held actor vtable");
        if(!Pointer(table)||!Image(table+0x738,8))return Reject("Held actor dispatch slot outside PE64 image");
        ulong target=Link(table+0x738,"Held actor component dispatch");
        if(!Code(target,L.ComponentLookup,"Held actor component lookup"))return Reject(detail);
        var collection=Proof(held+0x240,48,"Held actor component collection",true);if(collection==null)return Reject(detail);
        ulong data=BitConverter.ToUInt64(collection);int slots=BitConverter.ToInt32(collection,8),allocated=BitConverter.ToInt32(collection,12);
        int bits=BitConverter.ToInt32(collection,40),bitCapacity=BitConverter.ToInt32(collection,44);
        if(!Pointer(data)||slots is <1 or >24||allocated<slots||allocated>4096||bits!=slots||bitCapacity<bits||bitCapacity>65536)
            return Reject("Weapon component collection bounds unsupported (maximum 24 slots)");
        ulong allocation=BitConverter.ToUInt64(collection,32);
        var occupied=allocation==0?collection.AsSpan(16,16).ToArray():Pointer(allocation)?Proof(allocation,4,"Component allocation bits",true):null;
        if(occupied==null)return Reject("Component allocation bits unavailable");
        var entries=Proof(data,slots*16,"Held actor component entries",true);if(entries==null)return Reject(detail);
        ulong Hint(int index,string name)
        {
            ulong chunks=Link(moduleBase+O.GObjects,"Object-table root");if(!Pointer(chunks))return 0;
            ulong chunk=Link(chunks+(ulong)index/O.ObjectsPerChunk*8,"Object-table chunk")&~0xFUL;if(!Pointer(chunk))return 0;
            ulong type=Link(chunk+(ulong)index%O.ObjectsPerChunk*O.ObjectItemSize+O.ObjectItemObject,name+" class hint");
            if(!Header(type,name+" class"))return 0;
            var id=Proof(type+O.ClassNameId,8,name+" name");if(id==null)return 0;
            if(DisplayObjectName(BitConverter.ToUInt32(id))!=name){detail=name+" class hint does not match exact name";return 0;}return type;
        }
        ulong weaponType=Hint(O.WeaponComponentClassIndex,"WDWeaponComponent");if(!Pointer(weaponType))return Reject(detail);
        ulong extensionType=Hint(O.RangedWeaponExtensionClassIndex,"WDItemExtension_RangedWeapon");if(!Pointer(extensionType))return Reject(detail);
        ulong weapon=0,extension=0;
        for(int index=0;index<slots;index++)
        {
            if((BitConverter.ToUInt32(occupied)&(1u<<index))==0)continue;
            ulong candidate=BitConverter.ToUInt64(entries,index*16);
            if(!Pointer(candidate))return Reject("Occupied weapon component slot invalid");
            ulong type=Link(candidate+O.UObjectClass,"Occupied component class");if(!Pointer(type))return Reject(detail);
            if(type==weaponType){if(weapon!=0&&weapon!=candidate)return Reject("Multiple matching weapon components");weapon=candidate;}
            if(type==extensionType){if(extension!=0&&extension!=candidate)return Reject("Multiple matching ranged extensions");extension=candidate;}
        }
        if(extension!=0)
        {
            if(!Header(extension,"Ranged extension",true))return Reject(detail);
            if(Link(extension+0xC8,"Ranged extension owner",true)!=held)return Reject("Ranged extension owner mismatch");
            ulong linked=Link(extension+O.RangedExtensionWeaponComponent,"Ranged extension weapon",true);if(!Pointer(linked))return Reject(detail);
            if(weapon!=0&&weapon!=linked)return Reject("Direct and ranged-extension weapon links disagree");weapon=linked;
        }
        if(!Header(weapon,"Weapon component",true))return Reject(detail);
        if(Link(weapon+O.UObjectClass,"Weapon component class")!=weaponType)return Reject("Weapon component exact class mismatch");
        if(Link(weapon+0xC8,"Weapon component owner",true)!=held)return Reject("Weapon component owner mismatch");
        // Identity excludes numerical fields so only an unreadable numerical
        // sample can reuse a still-fresh original sample after all proofs pass.
        var identity=proofs.Values.ToArray();
        var initial=Proof(weapon+L.InitialSpeedOffset,4,"Weapon initial-speed field");
        var modifier=Proof(weapon+L.ModifierOffset,4,"Weapon speed modifier");
        float raw=initial==null?0:BitConverter.ToSingle(initial),scale=modifier==null?0:BitConverter.ToSingle(modifier),speed=raw*scale;
        bool validRaw=initial==null||float.IsFinite(raw)&&raw>0;
        bool validModifier=modifier==null||float.IsFinite(scale)&&scale>0;
        bool validSpeed=initial==null||modifier==null||float.IsFinite(speed)&&speed is >=2000 and <=200000;
        bool known=initial!=null&&modifier!=null;
        long numericalStarted=started;
        if(known&&readSampleStarted!=null)
        {
            long initialStarted=readSampleStarted(weapon+L.InitialSpeedOffset,4),modifierStarted=readSampleStarted(weapon+L.ModifierOffset,4);
            if(initialStarted<started||modifierStarted<started)return Reject("Original numerical bullet-speed timestamp unavailable or from an earlier scan");
            if(Stopwatch.GetElapsedTime(initialStarted).TotalMilliseconds<0||Stopwatch.GetElapsedTime(modifierStarted).TotalMilliseconds<0)
                return Reject("Original numerical bullet-speed timestamp is in the future");
            numericalStarted=Math.Min(initialStarted,modifierStarted);
        }
        string status=!validRaw?FormattableString.Invariant($"Weapon initial-speed field invalid ({raw:G9})")
            :!validModifier?FormattableString.Invariant($"Weapon speed modifier invalid ({scale:G9})")
            :!validSpeed?FormattableString.Invariant($"Effective weapon bullet speed invalid ({speed:G9}); expected 2000-200000 cm/s")
            :initial==null?"Weapon initial-speed field unreadable":modifier==null?"Weapon speed modifier unreadable":"Live bullet speed ready";
        var requests=proofs.Values.Select(p=>new WardogReadBatch.Request(p.Address,p.Bytes.Length)).ToArray();
        if(verifyScatter!=null)verifyScatter(requests);
        else foreach(var request in requests)request.Success=read(request.Address,request.Bytes);
        for(int index=0;index<requests.Length;index++)
            if(!requests[index].Success||!requests[index].Bytes.AsSpan().SequenceEqual(proofs[requests[index].Address].Bytes))
                return Reject("Weapon speed context, identity, code or values changed during recheck");
        if(!frame.IsFresh||Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds<0||Stopwatch.GetElapsedTime(started).TotalMilliseconds>=150)
            return Reject("Original bullet-speed context expired during sampling");
        bool valid=known&&validRaw&&validModifier&&validSpeed;
        var sample=new PredictionSpeedSample(frame.WorldAddress,frame.LocalController,frame.LocalPawn,valid?speed:0,valid?numericalStarted:0,started,status)
        {
            Guards=identity.Where(p=>guards.Contains(p.Address)).ToArray(),Identity=identity,
            CanRetain=!known&&validRaw&&validModifier&&validSpeed,
            CaptureMilliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            InitialReadable=initial!=null,ModifierReadable=modifier!=null,InitialSpeed=raw,Modifier=scale,
            HeldWeapon=held,HeldItemId=heldId,InitialAddress=weapon+L.InitialSpeedOffset,ModifierAddress=weapon+L.ModifierOffset
        };
        return cache?.Store(sample)??sample;
    }
}
