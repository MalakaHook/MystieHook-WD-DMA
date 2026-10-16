using MystieHook.Games.Wardog;
using O=MystieHook.Games.Wardog.WardogOffsets;
internal static class HiddenTeammateChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(bool optimized in new[]{false,true})
        {
            var f=new Fixture();f.Bones();f.Teams();f.Put(Fixture.RemoteFaction+O.FactionComponentTag,111UL);
            foreach(ulong page in f.Memory.Keys.Select(a=>a&~4095UL).Distinct().ToArray())
                for(ulong n=0;n<4096;n++)f.Memory.TryAdd(page+n,0);
            var pages=new HashSet<ulong>();
            bool Direct(ulong address,Span<byte> bytes){pages.Add(address&~4095UL);return f.Read(address,bytes);}
            var memory=new WardogFrameMemory(Direct,requests=>
                {foreach(var r in requests){pages.Add(r.Address);r.Success=f.Read(r.Address,r.Bytes);}},optimized);
            var reader=new WardogReader(Fixture.Base,memory.Read,Direct,()=>f.Now,memory.ScheduleNext,memory.Prefetch,optimizedReads:optimized);
            WardogReader.Frame Capture(bool friends,WardogRadarSettings radar=null)
            {pages.Clear();memory.BeginFrame();try{return reader.Capture(new(ShowBones:true,ShowTeammates:friends),radar:radar);}finally{memory.EndFrame();}}
            var hidden=Capture(false);
            check(hidden.Players.Length==0&&!pages.Contains(Fixture.Poses&~4095UL)&&!pages.Contains(Fixture.Text&~4095UL),
                "hidden confirmed teammate fetches neither pose-buffer nor name-buffer pages: "+optimized);
            var shown=Capture(true);
            check(shown.Players.Single().Pose.HasHead&&pages.Contains(Fixture.Poses&~4095UL)&&pages.Contains(Fixture.Text&~4095UL),
                "enabling teammates acquires fresh bones and name bytes: "+optimized);
            f.Now+=1100;hidden=Capture(false);
            check(hidden.Players.Length==0&&!pages.Contains(Fixture.Poses&~4095UL),
                "hiding a previously visible teammate stops pose page transfers on the next scan: "+optimized);
            // Name cache belongs to the actor identity. A new reader proves the
            // radar's explicit name demand independently of its prior visual cache.
            reader=new(Fixture.Base,memory.Read,Direct,()=>f.Now,memory.ScheduleNext,memory.Prefetch,optimizedReads:optimized);
            var radar=Capture(false,new(){Enabled=true,Names=true,LocalMap=true,Players=true,Teammates=true});
            check(radar.Players.Length==0&&radar.RadarPlayers.Single().Name=="Test"&&pages.Contains(Fixture.Text&~4095UL)
                &&!pages.Contains(Fixture.Poses&~4095UL),"radar name demand survives hidden player visuals without demanding bones: "+optimized);
            f.Put(Fixture.RemoteFaction+O.FactionComponentTag,222UL);f.Now+=1100;
            check(Capture(false).Players.Single().Pose.HasHead,"enemy bones remain available while teammates are hidden: "+optimized);
            f.Put(Fixture.RemoteFaction+O.FactionComponentTag,0UL);f.Now+=1100;
            check(Capture(false).Players.Single().TeamRelation==WardogReader.Relation.Unknown,
                "unclassified teams remain visible rather than being silently treated as friends: "+optimized);
        }
    }
}
