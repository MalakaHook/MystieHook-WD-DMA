using System.Runtime.InteropServices;
using System.Text;
using MystieHook.Games.Wardog;
using O = MystieHook.Games.Wardog.WardogOffsets;

internal sealed class PredictionFixture
{
    public const ulong Base=0x10000000, World=0x200000, Instance=0x210000, Locals=0x220000, Local=0x230000,
        Controller=0x240000, Pawn=0x250000, Root=0x260000, GameState=0x270000, States=0x280000,
        State=0x290000, Actor=0x300000, ActorRoot=0x310000, Chunks=0x320000, Chunk=0x330000,
        PlayerClass=0x400000, Text=0x410000, Manager=0x420000, LocalState=0x450000,
        LocalFaction=0x460000, LocalFactionObject=0x470000, LocalData=0x480000,
        RemoteFaction=0x490000, RemoteFactionObject=0x4A0000, RemoteData=0x4B0000,
        HealthVitality=0x700000, HealthAttribute=0x710000, HealthControl=0x720000,
        Mesh=0x500000, Poses=0x510000, Asset=0x600000, Infos=0x610000, NameBlock=0x620000;
    public readonly Dictionary<ulong,byte> Memory=new();
    public readonly Dictionary<ulong,int> Reads=new();
    public long Now;
    public int Calls;
    public void Put<T>(ulong address,T value) where T:unmanaged
    {
        var bytes=MemoryMarshal.AsBytes(new[]{value}.AsSpan());
        for(int i=0;i<bytes.Length;i++) Memory[address+(ulong)i]=bytes[i];
    }
    public void Array(ulong address,ulong data,int count)
    { Put(address,data); Put(address+8,count); Put(address+12,count); }
    public bool Read(ulong address,Span<byte> data)
    {
        Calls++; Reads[address]=Reads.GetValueOrDefault(address)+1;
        for(int i=0;i<data.Length;i++) if(!Memory.TryGetValue(address+(ulong)i,out data[i])) return false;
        return true;
    }
    public WardogReader Reader() => new(Base,Read,clockMilliseconds:()=>Now);
    public void Rename(string name,ulong state=State,ulong text=Text)
    {
        Array(state+O.PlayerName,text,name.Length+1);
        byte[] bytes=Encoding.Unicode.GetBytes(name+"\0");
        for(int i=0;i<bytes.Length;i++) Memory[text+(ulong)i]=bytes[i];
    }
    public PredictionFixture()
    {
        Put(Base+O.GWorld,World); Put(World+O.GameInstance,Instance); Array(Instance+O.LocalPlayers,Locals,1);
        Put(Locals,Local); Put(Local+O.LocalPlayerController,Controller); Put(Controller+O.ControllerPawnCurrent,Pawn);
        Put(Pawn+O.ActorRootComponent,Root); Put(Root+O.SceneRelativeLocation,new WardogReader.Vec());
        Put(World+O.WorldGameState,GameState); Array(GameState+O.GameStatePlayerArray,States,1); Put(States,State);
        Put(Base+O.GObjects,Chunks); Put(Chunks,Chunk); Put(Chunk+O.PlayerBaseClassIndex*O.ObjectItemSize+0x10,PlayerClass);
        Name("None",0); Name("WDMoverCharacter",4096); Put(PlayerClass+O.UObjectNameId,4096UL);
        Put(State+O.PlayerStatePawn,Actor); Put(Actor+O.UObjectClass,PlayerClass); Put(Actor+O.ActorRootComponent,ActorRoot);
        Put(ActorRoot+O.SceneRelativeLocation,new WardogReader.Vec { X=1000 });
        Rename("Test"); Put(Actor+0x808,50f); Put(Actor+0x80C,100f);
        // Literal offsets independently exercise the latest attribute layout.
        Put(Actor+0x748,HealthVitality); Put(HealthVitality+0x134,100f); Put(HealthVitality+0x130,50f);
        Put(HealthVitality+0x140,HealthAttribute); Put(HealthVitality+0x148,HealthControl);
        Put(HealthAttribute+0x30,50.0); Put(HealthAttribute+0x28,4u); Put(HealthControl+0x08,4u);
        Put(Controller+O.CameraManager,Manager);
        for(ulong i=0;i<0x60;i++) Memory[Manager+0x1E40+i]=0;
        Put(Manager+0x1E40+0x40,90f);
    }
    public void Teams()
    {
        Put(Pawn+O.PawnPlayerState,LocalState);
        Put(LocalState+O.PlayerStateFactionComponent,LocalFaction); Put(LocalFaction+O.FactionComponentFaction,LocalFactionObject);
        Put(LocalFactionObject+O.FactionData,LocalData); Put(LocalData+O.GenericTeamId,(byte)0); Put(LocalFaction+O.FactionComponentTag,111UL);
        Put(State+O.PlayerStateFactionComponent,RemoteFaction); Put(RemoteFaction+O.FactionComponentFaction,RemoteFactionObject);
        Put(RemoteFactionObject+O.FactionData,RemoteData); Put(RemoteData+O.GenericTeamId,(byte)1); Put(RemoteFaction+O.FactionComponentTag,222UL);
    }
    public void Transform(ulong address,double x,double y,double z)
    {
        for(ulong i=0;i<96;i++) Memory[address+i]=0;
        Put(address+24,1.0); Put(address+32,x); Put(address+40,y); Put(address+48,z);
        Put(address+64,1.0); Put(address+72,1.0); Put(address+80,1.0);
    }
    public void Bones()
    {
        Put(Actor+O.MoverMesh,Mesh); Transform(Mesh+0x1D0,1000,0,0);
        Array(Mesh+0xA18,Poses,149);
        for(int i=0;i<149;i++) Transform(Poses+(ulong)i*96,0,0,80);
        Transform(Poses+9*96,0,0,170); Transform(Poses+7*96,0,0,150); Transform(Poses+6*96,0,0,130);
    }
    public void MappedBones()
    {
        Put(Mesh+O.SkinnedAsset, Asset); Array(Asset+O.RawRefBoneInfo, Infos, 149);
        Name("None", 0); Name("unused", 64);
        int[] indexes = [9,7,6,4,2,1,145,146,147,10,11,12,122,123,124,64,65,66];
        string[] names = ["head","neck_01","spine_05","spine_03","spine_01","pelvis","thigh_l","calf_l","foot_l","clavicle_l","upperarm_l","lowerarm_l","thigh_r","calf_r","foot_r","clavicle_r","upperarm_r","lowerarm_r"];
        for (int i=0; i<149; i++)
        {
            int slot = System.Array.IndexOf(indexes, i);
            uint id = slot < 0 ? 64u : (uint)(128+slot*64);
            if (slot >= 0) Name(names[slot], id);
            Put(Infos+(ulong)i*12, id); Put(Infos+(ulong)i*12+4, 0u); Put(Infos+(ulong)i*12+8, -1);
        }
    }
    public void Name(string value, uint id, ulong block = NameBlock)
    {
        Put(Base+O.GNames, block);
        Put(block+id, (ushort)(value.Length<<6));
        var bytes = Encoding.UTF8.GetBytes(value);
        for(int i=0;i<bytes.Length;i++) Put(block+id+2+(ulong)i,bytes[i]);
    }
    public void LocalSpaceBones()
    {
        Put(Mesh+O.SkinnedAsset,Asset); Array(Asset+O.RawRefBoneInfo,Infos,20); Put(Base+O.GNames,NameBlock);
        Name("None", 0);
        for(int i=0;i<20;i++)
        {
            uint id=(uint)((i+1)*64);
            string name=i switch {1=>"pelvis",2=>"spine_03",3=>"neck_01",4=>"head",_=>$"bone_{i}"};
            Put(Infos+(ulong)i*12,id); Put(Infos+(ulong)i*12+4,0u); Put(Infos+(ulong)i*12+8,i==0 ? -1 : i<=4 ? i-1 : 0);
            Put(NameBlock+id,(ushort)(name.Length<<6));
            byte[] bytes=Encoding.UTF8.GetBytes(name);
            for(int j=0;j<bytes.Length;j++) Memory[NameBlock+id+2+(ulong)j]=bytes[j];
        }
        for(ulong i=0;i<16;i++) Memory.Remove(Mesh+0xA18+i);
        Array(Mesh+0xA08,Poses,20);
        for(int i=0;i<20;i++) Transform(Poses+(ulong)i*96,0,0,0);
        Transform(Poses+96,0,0,80); Transform(Poses+2*96,0,0,50);
        Transform(Poses+3*96,0,0,20); Transform(Poses+4*96,0,0,20);
    }
}

