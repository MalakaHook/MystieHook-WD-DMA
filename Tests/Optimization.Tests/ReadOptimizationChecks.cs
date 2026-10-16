using System.Diagnostics;
using MystieHook.Games.Wardog;
using O = MystieHook.Games.Wardog.WardogOffsets;

internal static class ReadOptimizationChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (bool optimized in new[] { false, true })
        {
            byte value = 17; bool fail = false; int transfers = 0;
            var fetched = new List<WardogReadBatch.Request>();
            bool Direct(ulong address, Span<byte> bytes)
            { bytes.Fill(value); return !fail; }
            var memory = new WardogFrameMemory(Direct, requests =>
            {
                foreach (var r in requests)
                {
                    check(!r.Success && r.SampleStarted == 0, "page fetch starts with cleared success/stamp: " + optimized);
                    transfers++; fetched.Add(r); r.Bytes.AsSpan().Fill(value); r.Success = !fail;
                    r.SampleStarted = r.Success ? Stopwatch.GetTimestamp() : 0;
                }
            }, optimized);
            var output = new byte[12];
            memory.BeginFrame();
            check(memory.Read(0x10100, output) && output.All(b => b == 17), "fresh page contents: " + optimized);
            check(memory.Read(0x10108, output) && transfers == 1 && memory.CacheHits == 1,
                "overlapping same-frame reads share one transfer: " + optimized);
            var first = fetched[0]; memory.EndFrame(); value = 23;
            memory.BeginFrame();
            check(!first.Success && first.SampleStarted == 0 || !optimized, "retained buffers lose success and timestamp");
            check(memory.Read(0x10100, output) && output.All(b => b == 23) && transfers == 2,
                "each frame fetches changed bytes from the source: " + optimized);
            check(ReferenceEquals(first, fetched[1]) == optimized && memory.NewPageBuffers == (optimized ? 0 : 1),
                "only the experiment reuses page allocation: " + optimized);
            memory.EndFrame(); memory.BeginFrame(); fail = true;
            check(!memory.Read(0x10100, output) && output.All(b => b == 0),
                "failed fresh page/direct retry cannot expose previous contents: " + optimized);
            memory.EndFrame(); memory.BeginFrame(); fail = false;
            check(memory.Read(0x10FFC, output) && output.All(b => b == 23), "cross-page reads remain complete: " + optimized);
            memory.EndFrame();
            if (optimized)
            {
                memory.BeginFrame(); memory.EndFrame();
                check(memory.RetainedPageBuffers == 0, "unused page allocations leave after a scan");
                for (int i = 0; i < 80; i++)
                { memory.BeginFrame(); memory.Read(0x20000UL + (ulong)i * 4096, output); memory.EndFrame(); }
                check(memory.RetainedPageBuffers == 1, "moving page demand does not grow the retained pool");
            }
        }
        {
            var memory = new WardogFrameMemory((ulong _, Span<byte> b) => true,
                _ => throw new OperationCanceledException(), true);
            bool cancelled = false;
            try { memory.BeginFrame(); memory.Read(0x10000, new byte[8]); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "optimized page prefetch propagates cancellation");
        }

        static (WardogReader.Frame Frame, long Bytes, int Transfers) Capture(bool optimized, WardogVisualSettings settings,
            bool enabled = true, bool friendly = false, bool dead = false)
        {
            var f = new Fixture(); f.Bones(); f.Teams();
            if (friendly) f.Put(Fixture.RemoteFaction + O.FactionComponentTag, 111UL);
            if (dead) f.Put(Fixture.HealthAttribute + 0x30, 0.0);
            // Independent live buffers plus cached fallback. The selector chooses 0x620.
            f.Array(Fixture.Mesh + 0x610, 0x800000, 149);
            f.Array(Fixture.Mesh + 0x620, 0x810000, 149); f.Put(Fixture.Mesh + 0x630, 0);
            for (int i = 0; i < 149 * 96; i++)
            {
                byte b = f.Memory[Fixture.Poses + (ulong)i];
                f.Memory[0x800000UL + (ulong)i] = b; f.Memory[0x810000UL + (ulong)i] = b;
            }
            foreach (ulong page in f.Memory.Keys.Select(a => a & ~4095UL).Distinct().ToArray())
                for (ulong n = 0; n < 4096; n++) f.Memory.TryAdd(page + n, 0);
            long bytes = 0; int transfers = 0;
            bool Direct(ulong a, Span<byte> b) { bytes += b.Length; transfers++; return f.Read(a, b); }
            var memory = new WardogFrameMemory(Direct, requests =>
            {
                transfers++;
                foreach (var r in requests) { bytes += r.Bytes.Length; r.Success = f.Read(r.Address, r.Bytes); }
            }, optimized);
            var reader = new WardogReader(Fixture.Base, memory.Read, Direct, () => f.Now,
                memory.ScheduleNext, memory.Prefetch, optimizedReads: optimized);
            memory.BeginFrame(); var frame = reader.Capture(settings, visualsEnabled: enabled); memory.EndFrame();
            return (frame, bytes, transfers);
        }
        foreach (var settings in new[]
        {
            new WardogVisualSettings(ShowName:false,ShowHealth:false),
            new WardogVisualSettings(ShowName:false,ShowHealth:false) {ShowBoxes=true},
            new WardogVisualSettings(ShowBones:true,ShowName:false,ShowHealth:false)
        })
        {
            var baseline = Capture(false, settings); var optimized = Capture(true, settings);
            var a = baseline.Frame.Players.Single(); var b = optimized.Frame.Players.Single();
            check(baseline.Frame.Valid && optimized.Frame.Valid && a.HeadPose.HasHead && b.HeadPose.HasHead
                && a.Position.Equals(b.Position) && a.Health == b.Health && a.TeamRelation == b.TeamRelation
                && a.HeadPose.Valid.SequenceEqual(b.HeadPose.Valid) && a.HeadPose.Points.SequenceEqual(b.HeadPose.Points),
                "focused prefetch preserves selected live pose for head/box/skeleton mode: " + settings.ShowBones + "/" + settings.ShowBoxes);
            check(optimized.Bytes < baseline.Bytes, $"fixture requests fewer bytes ({baseline.Bytes} -> {optimized.Bytes}), transfer calls {baseline.Transfers} -> {optimized.Transfers}; synthetic, not a hardware benchmark");
        }
        var far = new WardogVisualSettings(ShowBones:true) {MaximumDistance=1};
        check(Capture(true, far).Frame.Players.Length == 0 && Capture(true, far).Bytes < Capture(false, far).Bytes,
            "out-of-range players avoid prefetching unused vitality/team/pose buffers");
        var all = new WardogVisualSettings(ShowBones:true, ShowTeammates:false);
        check(Capture(true, all, friendly:true).Frame.Players.Length == 0
            && Capture(false, all, friendly:true).Frame.Players.Length == 0
            && Capture(false, all, friendly:true).Bytes < Capture(false, all with {ShowTeammates=true}, friendly:true).Bytes,
            "both builds filter confirmed friends before detail prefetch when teammates are hidden");
        check(Capture(true, all, enabled:false).Frame.Players.Length == 0
            && Capture(true, all, enabled:false).Bytes < Capture(false, all with {ShowTeammates=true}, enabled:false).Bytes,
            "disabled player visuals avoid speculative detail prefetch");
        check(Capture(true, new(ShowBones:true), dead:true).Frame.Players.Single().Dead
            && !Capture(true, new(ShowBones:true), dead:true).Frame.Players.Single().HeadPose.HasHead,
            "dead players retain name-only behavior without a pose");
        {
            var f = new Fixture(); f.Bones();
            f.Array(Fixture.Mesh + 0x620, 0x820000, 149); f.Put(Fixture.Mesh + 0x630, 0);
            for (int i = 0; i < 149 * 96; i++) f.Memory[0x820000UL + (ulong)i] = 0;
            var reader = new WardogReader(Fixture.Base, f.Read, clockMilliseconds:()=>f.Now,
                prefetch: _ => { }, optimizedReads:true);
            check(reader.Capture(new(ShowBones:true)).Players.Single().Pose.HasHead,
                "focused invalid live source retains the established cached-pose fallback");
            f.Array(Fixture.Mesh + 0x620, Fixture.Poses, 149);
            f.Array(Fixture.Mesh + 0xA18, 0, 0);
            bool changed = false;
            reader = new WardogReader(Fixture.Base, f.Read, clockMilliseconds:()=>f.Now, prefetch:ranges =>
            {
                if (!changed && ranges.Any(r => r.Address >= Fixture.Poses && r.Address < Fixture.Poses + 149 * 96))
                { changed = true; f.Array(Fixture.Mesh + 0x620, 0, 0); }
            }, optimizedReads:true);
            check(changed == false && !reader.Capture(new(ShowBones:true)).Players.Single().Pose.HasHead && changed,
                "changed selected array header is rejected after focused acquisition");
        }
        {
            var f = new Fixture(); f.Bones(); f.LocalSpaceBones();
            var ranges = new List<WardogFrameMemory.Range>();
            var reader = new WardogReader(Fixture.Base,f.Read,clockMilliseconds:()=>f.Now,
                prefetch:r => ranges.AddRange(r),optimizedReads:true);
            var settings = new WardogVisualSettings(ShowName:false,ShowHealth:false);
            for(int i=0;i<3;i++){reader.Capture(settings);f.Now+=10;}
            ranges.Clear(); var frame = reader.Capture(settings);
            var poseRanges = ranges.Where(r => r.Address>=Fixture.Poses && r.Address<Fixture.Poses+20*96).ToArray();
            check(frame.Players.Single().HeadPose.HasHead && frame.Players.Single().HeadPose.Points[0].Z==170
                && poseRanges.Any(r=>r.Address==Fixture.Poses) && poseRanges.All(r=>r.Size==96)
                && poseRanges.Select(r=>r.Address).Distinct().Count()==poseRanges.Length,
                "focused local-space poses include each required ancestor once and preserve world coordinates");
        }
        {
            var f = new Fixture(); f.Bones();
            foreach(ulong page in f.Memory.Keys.Select(a=>a&~4095UL).Distinct().ToArray())
                for(ulong n=0;n<4096;n++)f.Memory.TryAdd(page+n,0);
            bool flipWorld=false;
            var memory = new WardogFrameMemory(f.Read, requests =>
            {
                foreach(var r in requests)
                {
                    if(flipWorld && r.Address>=Fixture.Poses && r.Address<Fixture.Poses+149*96)
                        f.Put(Fixture.Base+O.GWorld,Fixture.World+0x10000);
                    r.Success=f.Read(r.Address,r.Bytes);
                }
            },true);
            var reader = new WardogReader(Fixture.Base,memory.Read,f.Read,()=>f.Now,memory.ScheduleNext,memory.Prefetch,optimizedReads:true);
            memory.BeginFrame();var frame=reader.Capture(new());memory.EndFrame();
            f.Transform(Fixture.Poses+9*96,0,0,190);f.Now=10;
            memory.BeginFrame();frame=reader.Capture(new());memory.EndFrame();
            check(frame.Players.Single().HeadPose.Points[0].Z==190 && frame.PoseFallbacks==0,
                "reused page buffers fetch current motion rather than extending old poses");
            flipWorld=true;f.Now=20;memory.BeginFrame();frame=reader.Capture(new());memory.EndFrame();
            check(!frame.Valid && frame.Status.Contains("World changed"),
                "focused page pipeline preserves the independent final world proof");
        }
        {
            var requests = Enumerable.Range(0, 300).Select(i => new WardogReadBatch.Request(0x10000UL + (ulong)i * 4096, 512)).ToArray();
            var chunks = WardogDmaChunks.Partition(requests, WardogReadOptimization.BackgroundRequests,
                WardogReadOptimization.BackgroundBytes).ToArray();
            check(chunks.All(c => c.Count <= 64 && c.Sum(r => r.Bytes.Length) <= 16384)
                && chunks.SelectMany(c => c).SequenceEqual(requests), "background partitions preserve every range within smaller caps");
            check(WardogDmaChunks.Partition(Enumerable.Range(0,100).Select(_=>new WardogReadBatch.Request(0x10000,8)),64,16384)
                .First().Count == 64, "tiny background ranges obey the request cap");
            bool rejected = false;
            try { WardogDmaChunks.Partition(requests,0,16384).ToArray(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "invalid chunk settings reject instead of hanging");
        }
        {
            var stats = new WardogReadStatistics();
            for (int i=1;i<=100;i++) stats.Record(WardogReadStream.Loot,2,16,1,i,i*2);
            stats.Record(WardogReadStream.Loot,1,8,1,double.NaN,1);
            var sample = stats.Snapshot().Single();
            check(sample.Transfers==100 && sample.Ranges==200 && sample.RequestedBytes==1600
                && sample.VirtualPageVisits==100 && sample.WaitP95==95 && sample.TransferP95==190
                && sample.LongestTransfer==200, "per-reader statistics retain accurate counts and p95 timings");
            check(ReferenceEquals(stats.Snapshot(),stats.Snapshot()), "UI statistics snapshots are throttled to once per second");
            var rolling = new WardogReadStatistics();
            rolling.Record(WardogReadStream.Camera,1,8,1,1000,1000);
            for(int i=0;i<2048;i++)rolling.Record(WardogReadStream.Camera,1,8,1,1,2);
            sample=rolling.Snapshot().Single();
            check(sample.WaitP95==1 && sample.TransferP95==2 && sample.LongestTransfer==1000 && sample.Transfers==2049,
                "timing ring stays bounded while retaining cumulative totals and maximum");
            check((new WardogDmaGate().Statistics is not null) == WardogReadOptimization.Enabled,
                "read telemetry is enabled only in the isolated experiment");
        }
    }
}
