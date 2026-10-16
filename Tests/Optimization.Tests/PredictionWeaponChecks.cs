using System.Diagnostics;
using System.Text;
using MystieHook.Games.Wardog;
using O=MystieHook.Games.Wardog.WardogOffsets;
using F=PredictionFixture;
using L=MystieHook.Games.Wardog.WardogPredictionSpeedLayout;

internal static class PredictionWeaponChecks
{
 const ulong SpeedHelper=0x6BBD500,NativeLookup=0x3A93090;
 const ulong Inventory=0x800000,Held=0x810000,Generic=0x840000,Weapon=0x940000,Extension=0x930000,
  WeaponType=0x950000,ExtensionType=0x960000,Entries=0x920000,Pool=0x910000,Table=F.Base+0x1000;
 static Dictionary<string,string> Captured(string name)
 {
  using var stream=typeof(PredictionWeaponChecks).Assembly.GetManifestResourceStream(name)!;
  return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
 }
 static F Build(int slots=20,bool externalBits=false)
 {
  var f=new F();
  void Bytes(ulong address,byte[] bytes){for(int i=0;i<bytes.Length;i++)f.Put(address+(ulong)i,bytes[i]);}
  void Header(ulong address,int index,ulong type)
  {
   Bytes(address,new byte[40]);f.Put(address+0xC,index);f.Put(address+0x10,type);
   f.Put(F.Chunk+(ulong)index*O.ObjectItemSize+O.ObjectItemObject,address);
  }
  void Name(uint index,string name)
  {
   f.Put(Pool+index*8+8,(ushort)(name.Length<<6));f.Put(Pool+index*8+10,(byte)0);
   Bytes(Pool+index*8+12,Encoding.UTF8.GetBytes(name));
  }
  Header(F.Controller,19,Generic);Header(F.Pawn,20,Generic);Header(Inventory,21,Generic);Header(Held,22,Generic);
  Header(WeaponType,O.WeaponComponentClassIndex,Generic);Header(ExtensionType,O.RangedWeaponExtensionClassIndex,Generic);Header(Weapon,23,WeaponType);Header(Extension,24,ExtensionType);
  f.Put(F.Base+O.GNames+0x20,Pool);Name(0,"None");Name(64,"WDWeaponComponent");Name(128,"WDItemExtension_RangedWeapon");
  f.Put(WeaponType+O.ClassNameId,64UL);f.Put(ExtensionType+O.ClassNameId,128UL);
  f.Put(F.Pawn+O.PlayerInventory,Inventory);f.Put(Inventory+O.InventoryHeldItem,Held);f.Put(Held+0x328,0x4ECA3UL);
  f.Put(Weapon+0xC8,Held);f.Put(Extension+0xC8,Held);f.Put(Extension+O.RangedExtensionWeaponComponent,Weapon);
  // Four current independent code windows from the October 16 report. Object
  // identities, sparse slots and numerical fields are synthetic test data.
  var speed=Captured("WeaponSpeed.2026-10-16.json");var wrappers=speed;var native=speed;
  Bytes(F.Base+0x6B990E0,Convert.FromHexString(speed["GetInitialBulletSpeed"]));
  Bytes(F.Base+SpeedHelper,Convert.FromHexString(speed["InitialBulletSpeedStatsHelper"]));
  Bytes(F.Base+0x3AAC1F0,Convert.FromHexString(wrappers["GetComponentByClass"]));
  Bytes(F.Base+NativeLookup,Convert.FromHexString(native["HeldActorComponentLookup"]));
  Bytes(F.Base,new byte[64]);f.Put(F.Base,(ushort)0x5A4D);f.Put(F.Base+0x3C,0x80);
  Bytes(F.Base+0x80,new byte[88]);f.Put(F.Base+0x80,0x4550u);f.Put(F.Base+0x84,(ushort)0x8664);
  f.Put(F.Base+0x94,(ushort)0xF0);f.Put(F.Base+0x98,(ushort)0x20B);f.Put(F.Base+0xD0,0x10000000u);
  f.Put(Held,Table);f.Put(Table+0x738,F.Base+NativeLookup);
  Bytes(Held+0x240,new byte[48]);f.Put(Held+0x240,Entries);f.Put(Held+0x248,slots);f.Put(Held+0x24C,slots);
  uint occupancy=(1u<<slots)-1;f.Put(Held+0x250,occupancy);f.Put(Held+0x268,slots);f.Put(Held+0x26C,slots);
  if(externalBits){f.Put(Held+0x260,0x9F0000UL);f.Put(0x9F0000,occupancy);}
  Bytes(Entries,new byte[slots*16]);
  for(int i=0;i<slots;i++)
  {
   ulong candidate=i==slots-1?Weapon:i==slots-3?Extension:0xA00000UL+(ulong)i*0x100;
   f.Put(Entries+(ulong)i*16,candidate);f.Put(candidate+0x10,i==slots-1?WeaponType:i==slots-3?ExtensionType:Generic);
  }
  // The v4 AK getter ignores both TypicalSpeed and the asset candidate.
  f.Put(Weapon+0x1E4,-1f);f.Put(Weapon+0xEE0,88000f);f.Put(Weapon+0x114,1f);
  f.Put(F.Pawn+O.WeaponBehaviorComponent,0xB00000UL);f.Put(0xB00000+0x440,0xB10000UL);f.Put(0xB10000+0xDC,-1f);f.Put(0xB10000+0xDD8,12345f);
  return f;
 }
 static WardogReader.Frame Context()=>new("fixture",true,default,default,[])
  {WorldAddress=F.World,LocalController=F.Controller,LocalPawn=F.Pawn,SampleStarted=Stopwatch.GetTimestamp()};
 public static void Run(Action<bool,string> check)
 {
  WeaponCacheChecks(check);
  CurrentLookupChecks(check);
  CurrentWrapperChecks(check);
  var independent=Captured("WeaponSpeed.v4.json");var getter=Convert.FromHexString(independent["GetInitialBulletSpeed"]);
  var helper=Convert.FromHexString(independent["InitialBulletSpeedStatsHelper"]);
  check(L.MatchesGetter(getter.AsSpan(0,74))&&L.MatchesHelper(helper.AsSpan(0,9)),"independent live AK v4 proves full getter and nine-byte initial-speed leaf including final return");
  bool mutations=true;
  for(int i=0;i<74;i++){var changed=getter[..74];changed[i]^=1;mutations&=!L.MatchesGetter(changed);}
  for(int i=0;i<9;i++){var changed=helper[..9];changed[i]^=1;mutations&=!L.MatchesHelper(changed);}
  check(mutations&&!L.MatchesGetter(getter.AsSpan(0,73))&&!L.MatchesHelper(helper.AsSpan(0,8)),"changed field, multiplier, helper target, opcode or return and all truncated bodies reject the speed formula");
  var f=Build();var reader=f.Reader();f.Calls=0;
  check(reader.CapturePredictionSpeed(Context(),false).Stamp==0&&f.Calls==0,"disabled background speed sampling performs no reads");
  check(reader.CapturePredictionSpeed(Context() with {SampleStarted=1}).Stamp==0&&f.Calls==0,"expired source context cannot start speed discovery");
  check(reader.CapturePredictionSpeed(Context() with {SampleStarted=Stopwatch.GetTimestamp()+Stopwatch.Frequency}).Stamp==0&&f.Calls==0,"future source context cannot start speed discovery");
  int calls=0,bytes=0,maximum=0;var original=f.Memory.ToDictionary(p=>p.Key,p=>p.Value);
  var measured=new WardogReader(F.Base,(ulong address,Span<byte> output)=>{calls++;bytes+=output.Length;maximum=Math.Max(maximum,output.Length);return f.Read(address,output);});
  var sample=measured.CapturePredictionSpeed(Context());
  check(sample.Speed==88000&&sample.IsFresh&&sample.Status=="Live bullet speed ready","AK effective speed comes from component+EE0 times modifier+114 even with TypicalSpeed -1");
  check(calls<=256&&bytes<=65536&&maximum<=4096&&original.Count==f.Memory.Count&&original.All(p=>f.Memory[p.Key]==p.Value),"runtime speed sampling stays within bounded reads and never writes memory");
  check(!f.Reads.ContainsKey(Weapon+0x1E4)&&!f.Reads.ContainsKey(0xB10000+0xDD8)&&!f.Reads.ContainsKey(F.Pawn+O.WeaponBehaviorComponent),"runtime speed has no TypicalSpeed, stats-asset or weapon-category fallback");
  f.Put(Weapon+0x114,1.25f);sample=reader.CapturePredictionSpeed(Context());
  check(sample.Speed==110000,"non-unit live component modifier participates in effective speed");
  f.Put(Weapon+0xEE0,60000f);f.Put(Weapon+0x114,.5f);
  check(reader.CapturePredictionSpeed(Context()).Speed==30000,"runtime values determine every speed sample with no hard-coded AK speed");
  f.Put(Weapon+0xEE0,88000f);f.Put(Weapon+0x114,1f);sample=reader.CapturePredictionSpeed(Context());
  foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1f,0f,880f,200001f})
  {
   f.Put(Weapon+0xEE0,value);var bad=reader.CapturePredictionSpeed(Context());
   check(bad.Speed==0&&bad.Stamp==0&&!bad.CanRetain,"invalid initial/effective speed "+value+" cannot publish or reuse cache");
  }
  f.Put(Weapon+0xEE0,88000f);
  foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1f,0f,4f})
  {
   f.Put(Weapon+0x114,value);var bad=reader.CapturePredictionSpeed(Context());
   check(bad.Speed==0&&bad.Stamp==0&&!bad.CanRetain,"invalid modifier/effective result "+value+" cannot publish or reuse cache");
  }
  f.Put(Weapon+0x114,1f);sample=reader.CapturePredictionSpeed(Context());f.Memory.Remove(Weapon+0xEE0);
  var missing=reader.CapturePredictionSpeed(Context());var retained=missing.Retain(sample);
  check(missing.CanRetain&&retained.Speed==88000&&retained.Stamp==sample.Stamp,"unreadable numerical field can retain only a proven same-weapon sample with its original timestamp");
  check(missing.Retain(sample with {Stamp=Stopwatch.GetTimestamp()-Stopwatch.Frequency}).Stamp==0,"retained speed expires at the original 150 ms age");
  f.Put(Weapon+0x114,1.1f);
  check(reader.CapturePredictionSpeed(Context()).Retain(sample).Stamp==0,"changed readable modifier blocks reuse when the initial-speed field is unreadable");
  f.Put(Weapon+0x114,1f);f.Put(Held+0x328,123UL);
  check(reader.CapturePredictionSpeed(Context()).Retain(sample).Stamp==0,"changed held ItemId blocks numerical cache reuse");
  f.Put(Held+0x328,0x4ECA3UL);f.Put(Weapon+0xEE0,88000f);
  f.Put(Inventory+O.InventoryHeldItem,0UL);f.Put(Inventory+0xA78,Held);
  check(reader.CapturePredictionSpeed(Context()).Stamp==0,"pending/active item cannot replace an absent held weapon");f.Put(Inventory+O.InventoryHeldItem,Held);
  f.Put(Weapon+0xC8,0xDEAD00UL);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("owner mismatch"),"weapon component must belong to the current held item");f.Put(Weapon+0xC8,Held);
  f.Put(Extension+O.RangedExtensionWeaponComponent,0xDEAD00UL);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("disagree"),"direct component and ranged-extension component must agree");f.Put(Extension+O.RangedExtensionWeaponComponent,Weapon);
  f.Put(F.Chunk+23*O.ObjectItemSize+O.ObjectItemObject,0UL);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("object-slot identity"),"freed component object slots reject speed sampling");f.Put(F.Chunk+23*O.ObjectItemSize+O.ObjectItemObject,Weapon);
  f.Put(Held+8,0x40000000u);
  check(reader.CapturePredictionSpeed(Context()).Stamp==0,"pending destruction of held weapon rejects speed sampling");f.Put(Held+8,0u);
  f.Put(WeaponType+O.ClassNameId,128UL);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("exact name"),"fixed class index alone cannot identify a weapon component");f.Put(WeaponType+O.ClassNameId,64UL);
  f.Put(Held+0x248,25);f.Put(Held+0x24C,25);f.Put(Held+0x268,25);f.Put(Held+0x26C,25);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("maximum 24"),"oversized sparse collections reject before entry traversal");
  f.Put(Held+0x248,20);f.Put(Held+0x24C,20);f.Put(Held+0x268,20);f.Put(Held+0x26C,20);
  var external=Build(24,true);
  check(external.Reader().CapturePredictionSpeed(Context()).Speed==88000,"fully occupied maximum collection and external allocation bits resolve within budget");
  f.Put(Entries+18*16,0xA01200UL);f.Put(0xA01200+0x10,WeaponType);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("Multiple matching"),"ambiguous matching components never select an arbitrary source");f.Put(0xA01200+0x10,Generic);
  f.Put(F.Base+SpeedHelper,(byte)0x90);
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("helper layout unsupported"),"changed native leaf cannot authorize the validated offset");f.Put(F.Base+SpeedHelper,helper[0]);
  f.Put(F.Base+0xD0,(uint)(SpeedHelper+8));f.Reads.Clear();
  check(reader.CapturePredictionSpeed(Context()).Status.Contains("outside PE64 image")&&!f.Reads.ContainsKey(F.Base+SpeedHelper),"partial helper at PE image boundary is rejected before reading code");f.Put(F.Base+0xD0,0x10000000u);
  int changedCalls=0;
  var changedReader=new WardogReader(F.Base,(ulong address,Span<byte> output)=>
   {if(address==Weapon+0x114&&++changedCalls==2)f.Put(address,1.2f);return f.Read(address,output);});
  check(changedReader.CapturePredictionSpeed(Context()).Stamp==0,"modifier changes during recheck discard the complete sample");f.Put(Weapon+0x114,1f);
  int headerReads=0;
  var changedHeader=new WardogReader(F.Base,(ulong address,Span<byte> output)=>
   {if(address==Held+8&&++headerReads==2)f.Put(Held+0xC,99);return f.Read(address,output);});
  check(changedHeader.CapturePredictionSpeed(Context()).Stamp==0,"reused held-object header discards the complete sample");f.Put(Held+0xC,22);
  var slow=new WardogReader(F.Base,(ulong address,Span<byte> output)=>
   {if(address==F.Base+SpeedHelper)Thread.Sleep(80);return f.Read(address,output);});
  check(slow.CapturePredictionSpeed(Context()).Status.Contains("expired"),"slow code/identity sampling never renews original freshness");
  int scatterCalls=0;var scatterReader=new WardogReader(F.Base,f.Read,verifyScatter:requests=>
   {scatterCalls++;foreach(var request in requests)request.Success=f.Read(request.Address,request.Bytes);});
  sample=scatterReader.CapturePredictionSpeed(Context());
  check(sample.Speed==88000&&scatterCalls==1,"background recheck uses one bounded exact-range scatter instead of serial proof reads");
  var player=new WardogReader(F.Base,f.Read,f.Read,()=>f.Now,predictionSpeed:()=>sample,verifyScatter:requests=>
   {scatterCalls++;foreach(var request in requests)request.Success=f.Read(request.Address,request.Bytes);});
  f.Reads.Clear();var live=player.Capture(prediction:true);
  check(live.LocalBulletSpeed==88000&&live.BulletSpeedSampleStarted==sample.Stamp&&scatterCalls==2,"player scan consumes a completed speed snapshot and verifies current weapon guards in one scatter");
  check(!f.Reads.ContainsKey(Weapon+0xEE0)&&!f.Reads.ContainsKey(Weapon+0x114)&&!f.Reads.ContainsKey(F.Base+0x6B990E0),"player scans perform no numerical speed, getter or discovery reads");
  f.Put(Inventory+O.InventoryHeldItem,0xDEAD00UL);
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"switching weapons between background and player snapshots immediately blocks the old speed");f.Put(Inventory+O.InventoryHeldItem,Held);
  f.Put(Held+0x328,321UL);
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"same held pointer with changed ItemId cannot consume the old speed");f.Put(Held+0x328,0x4ECA3UL);
  f.Put(Held+0x250,0u);
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"removing component membership blocks speed even before the next background cycle");f.Put(Held+0x250,(1u<<20)-1);
  sample=sample with {Stamp=Stopwatch.GetTimestamp()-Stopwatch.Frequency};
  var expired=player.Capture(prediction:true);
  check(expired.LocalBulletSpeed==0&&expired.PredictionSpeedBlockReason.Contains("expired")&&expired.PredictionSpeedBlockReason.Contains("limit 150 ms"),"expired completed speed snapshot cannot enter aim prediction and reports actual age separately");
  sample=sample with {Stamp=Stopwatch.GetTimestamp()+Stopwatch.Frequency};
  var future=player.Capture(prediction:true);
  check(future.LocalBulletSpeed==0&&future.PredictionSpeedBlockReason.Contains("in the future")&&!future.PredictionSpeedBlockReason.Contains("expired"),"future completed speed snapshot cannot enter aim prediction and has its own failure reason");
  sample=scatterReader.CapturePredictionSpeed(Context()) with {World=0xDEAD00};
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"mismatched speed world cannot enter aim prediction");
  sample=scatterReader.CapturePredictionSpeed(Context());f.Reads.Clear();int beforeScatter=scatterCalls;
  check(player.Capture(prediction:false).LocalBulletSpeed==0&&scatterCalls==beforeScatter&&!f.Reads.ContainsKey(Weapon+8),"disabled player prediction consumes no cached speed and does not verify weapon guards");
  BatchingChecks(check);
  bool canceled=false;
  try{new WardogReader(F.Base,(ulong _,Span<byte> __)=>throw new OperationCanceledException()).CapturePredictionSpeed(Context());}
  catch(OperationCanceledException){canceled=true;}
  check(canceled,"background speed capture propagates cancellation");
  sample=scatterReader.CapturePredictionSpeed(Context());
  canceled=false;
  try{new WardogReader(F.Base,f.Read,f.Read,predictionSpeed:()=>sample,verifyScatter:_=>throw new OperationCanceledException()).Capture(prediction:true);}
  catch(OperationCanceledException){canceled=true;}
  check(canceled,"player identity verification preserves shared-gate cancellation");
 }

 static void WeaponCacheChecks(Action<bool,string> check)
 {
  var f=Build();var cache=new WardogReader.PredictionSpeedCache();
  var reader=f.Reader();var first=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(first.CachedForWeapon&&first.Speed==88000&&cache.Count==1&&first.HasUsableSpeed,
   "first independently validated weapon speed is stored in the bounded memory-only cache");
  Thread.Sleep(170);f.Reads.Clear();
  var cached=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(cached.CachedForWeapon&&cached.HasUsableSpeed&&!cached.IsFresh&&cached.Stamp==first.Stamp
   &&!f.Reads.ContainsKey(Weapon+0xEE0)&&!f.Reads.ContainsKey(Weapon+0x114),
   "same weapon reuses its original numerical sample beyond 150 ms without reading either numerical field");
  var current=cached;
  var player=new WardogReader(F.Base,f.Read,f.Read,()=>f.Now,predictionSpeed:()=>current,
   verifyScatter:requests=>{foreach(var request in requests)request.Success=f.Read(request.Address,request.Bytes);});
  f.Reads.Clear();var frame=player.Capture(prediction:true) with {SampleStarted=Stopwatch.GetTimestamp()};
  check(frame.LocalBulletSpeed==88000&&frame.BulletSpeedCached&&frame.HasPredictionSpeed&&frame.BulletSpeedSampleStarted==first.Stamp
   &&!f.Reads.ContainsKey(Weapon+0xEE0)&&!f.Reads.ContainsKey(Weapon+0x114),
   "player consumes an aged cached value only after fresh current-weapon guard reads without timestamp renewal");
  check(!(frame with {BulletSpeedCached=false}).HasPredictionSpeed
   &&!(frame with {SampleStarted=Stopwatch.GetTimestamp()-Stopwatch.Frequency}).HasPredictionSpeed
   &&!(frame with {BulletSpeedSampleStarted=Stopwatch.GetTimestamp()+Stopwatch.Frequency}).HasPredictionSpeed,
   "uncached numerical expiry and stale-player/future-source gates remain active with weapon caching");
  current=current with {Stamp=Stopwatch.GetTimestamp()+Stopwatch.Frequency};
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"future numerical timestamps cannot authorize even a cached weapon");current=cached;
  byte oldHeader=f.Memory[Held+8];f.Memory.Remove(Held+8);
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"unreadable current held identity blocks cached prediction");f.Put(Held+8,oldHeader);
  const ulong otherHeld=0xB20000;
  foreach(var range in new[]{(Offset:0UL,Length:40),(Offset:0x240UL,Length:48),(Offset:O.ItemId,Length:8)})
   for(int i=0;i<range.Length;i++)f.Put(otherHeld+range.Offset+(ulong)i,f.Memory[Held+range.Offset+(ulong)i]);
  f.Put(otherHeld+0xC,30);f.Put(F.Chunk+30*O.ObjectItemSize+O.ObjectItemObject,otherHeld);
  f.Put(Inventory+O.InventoryHeldItem,otherHeld);f.Put(Weapon+0xC8,otherHeld);f.Put(Extension+0xC8,otherHeld);f.Put(Weapon+0xEE0,60000f);
  check(player.Capture(prediction:true).LocalBulletSpeed==0,"switching to a different instance immediately blocks the previous cached value");
  f.Reads.Clear();var second=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(second.CachedForWeapon&&second.Speed==60000&&second.HeldWeapon==otherHeld&&cache.Count==2
   &&f.Reads.ContainsKey(Weapon+0xEE0),"same ItemId on another held instance requires an independent validated numerical acquisition");
  f.Put(Inventory+O.InventoryHeldItem,Held);f.Put(Weapon+0xC8,Held);f.Put(Extension+0xC8,Held);f.Put(Weapon+0xEE0,0f);f.Reads.Clear();
  var returned=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(returned.Speed==88000&&returned.Stamp==first.Stamp&&!f.Reads.ContainsKey(Weapon+0xEE0),
   "returning to a previously validated instance reuses its own value, not the last equipped weapon's speed");
  cache.Clear();f.Reads.Clear();var refreshed=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(refreshed.Stamp==0&&refreshed.Status.Contains("initial-speed field invalid")&&cache.Count==0&&f.Reads.ContainsKey(Weapon+0xEE0),
   "explicit cache refresh reacquires values and cannot retain a now-invalid same-weapon speed");
  f.Put(Weapon+0xEE0,91000f);var updated=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(updated.Speed==91000&&updated.CachedForWeapon,"a refreshed valid same-weapon value replaces the old cached calibration");
  f.Put(Weapon+0xC8,otherHeld);f.Reads.Clear();var changed=reader.CapturePredictionSpeed(Context(),cache:cache);
  check(changed.Stamp==0&&cache.Count==0&&!f.Reads.ContainsKey(Weapon+0xEE0),
   "readable component ownership change invalidates its cache before numerical reuse");f.Put(Weapon+0xC8,Held);
  reader.CapturePredictionSpeed(Context(),cache:cache);
  reader.CapturePredictionSpeed(Context() with {LocalPawn=0xDEAD00},cache:cache);
  check(cache.Count==0,"local-pawn/context change discards all previous weapon instances");
  f=Build();cache=new();reader=f.Reader();
  var physical=new List<ulong>();long transferStamp=0;
  void Scatter(IReadOnlyCollection<WardogReadBatch.Request> requests)
  {
   transferStamp=Stopwatch.GetTimestamp();
   foreach(var request in requests){physical.Add(request.Address);request.SampleStarted=transferStamp;request.Success=f.Read(request.Address,request.Bytes);}
  }
  var batch=new WardogReadBatch((ulong address,Span<byte> output)=>
   {physical.Add(address);transferStamp=Stopwatch.GetTimestamp();return f.Read(address,output);},Scatter,()=>transferStamp);
  reader=new WardogReader(F.Base,batch.Read,verifyScatter:Scatter,readSampleStarted:batch.SampleStarted);
  var started=Stopwatch.GetTimestamp();var context=Context();batch.BeginFrame();
  var batched=reader.CapturePredictionSpeed(context,captureStarted:started,cache:cache);
  batch.ForgetRange(batched.InitialAddress,4);batch.ForgetRange(batched.ModifierAddress,4);batch.EndFrame();
  check(batched.CachedForWeapon,"cache can be populated through the actual exact-range producer batching path");
  for(int cycle=0;cycle<3;cycle++)
  {
   physical.Clear();started=Stopwatch.GetTimestamp();context=Context();batch.BeginFrame();
   var hit=reader.CapturePredictionSpeed(context,captureStarted:started,cache:cache);batch.EndFrame();
   check(hit.Speed==88000&&hit.Stamp==batched.Stamp&&!physical.Contains(Weapon+0xEE0)&&!physical.Contains(Weapon+0x114),
    "cached producer cycle "+cycle+" physically excludes numerical fields from prefetch, direct and proof transfers");
  }
  f=Build();cache=new();reader=f.Reader();
  for(ulong id=1;id<=17;id++){f.Put(Held+O.ItemId,id);reader.CapturePredictionSpeed(Context(),cache:cache);}
  check(cache.Count==WardogReader.PredictionSpeedCache.Capacity,"weapon cache remains bounded after more than sixteen distinct held fingerprints");
  f.Put(Held+O.ItemId,1UL);f.Reads.Clear();reader.CapturePredictionSpeed(Context(),cache:cache);
  check(f.Reads.ContainsKey(Weapon+0xEE0),"least-recently-used weapon fingerprint is independently resampled after eviction");
 }

 static void CurrentLookupChecks(Action<bool,string> check)
 {
  var captured=Captured("WeaponSpeed.2026-10-16.json");
  var code=Convert.FromHexString(captured["HeldActorComponentLookup"]);
  var historical=Convert.FromHexString(Captured("WeaponMemory.v3.json")["HeldActorComponentLookup"]);
  check(code.Length==1024&&L.MatchesComponentLookup(code.AsSpan(0,624))
   ,
   "October 16 independently captured complete 624-byte native lookup admits the current sparse layout");
  bool sameInstructions=true;
  for(int i=0;i<624;i++)if(i is not (>=30 and <34) and not (>=564 and <568))sameInstructions&=code[i]==historical[i];
  check(sameInstructions&&!L.MatchesComponentLookup(historical.AsSpan(0,624)),
   "current native lookup differs only in two direct-call operands and requires exact current address bytes");
  bool mutationsRejected=true;
  for(int i=0;i<624;i++)
  {
   var changed=(byte[])code.Clone();changed[i]^=1;
   mutationsRejected&=!L.MatchesComponentLookup(changed.AsSpan(0,624));
  }
  check(mutationsRejected&&!L.MatchesComponentLookup(code.AsSpan(0,623))
   ,
   "all 624 native-byte mutations, including both call operands, and a truncated return reject admission");
  var f=Build();var original=f.Memory.ToDictionary(p=>p.Key,p=>p.Value);int calls=0,bytes=0,maximum=0;
  var reader=new WardogReader(F.Base,(ulong address,Span<byte> output)=>
   {calls++;bytes+=output.Length;maximum=Math.Max(maximum,output.Length);return f.Read(address,output);});
  var sample=reader.CapturePredictionSpeed(Context());
  check(sample.Speed==88000&&sample.IsFresh&&calls<=256&&bytes<=65536&&maximum<=4096
   &&original.Count==f.Memory.Count&&original.All(p=>f.Memory[p.Key]==p.Value),
   "four actual current code bodies resolve synthetic component values within original read caps without writes");
  int nativeReads=0;
  var changedReader=new WardogReader(F.Base,(ulong address,Span<byte> output)=>
   {if(address==F.Base+NativeLookup&&++nativeReads==2)f.Put(address,(byte)0x90);return f.Read(address,output);});
  check(changedReader.CapturePredictionSpeed(Context()).Status.Contains("changed during recheck"),
   "current native code changing during fresh proof discards the complete speed sample");
  f=Build();f.Put(Table+0x738,F.Base+0x10000000-623);f.Reads.Clear();
  check(f.Reader().CapturePredictionSpeed(Context()).Status=="Held actor component lookup outside PE64 image"
   &&!f.Reads.ContainsKey(F.Base+0x10000000-623),
   "current complete native body must fit inside the validated PE64 image before any code read");
 }

 static void CurrentWrapperChecks(Action<bool,string> check)
 {
  var capture=Captured("WeaponSpeed.2026-10-15.json");
  var wrapper=Convert.FromHexString(capture["GetComponentByClass"]);
  var getter=Convert.FromHexString(capture["GetInitialBulletSpeed"]);
  var helper=Convert.FromHexString(capture["InitialBulletSpeedStatsHelper"]);
  check(wrapper.Length==1024&&L.MatchesComponentWrapper(wrapper.AsSpan(0,200))
   ,
   "October 15 independent complete component wrapper validates its unchanged 0x738 dispatch slot");
  var historical=Convert.FromHexString(Captured("WeaponMemory.v2.json")["GetComponentByClass"]);
  int[] operands=[30,53,83,108,130,137,169];bool onlyAddressOperands=true;
  for(int i=0;i<200;i++)if(!operands.Any(start=>i>=start&&i<start+4))onlyAddressOperands&=wrapper[i]==historical[i];
  check(onlyAddressOperands&&!L.MatchesComponentWrapper(historical.AsSpan(0,200)),
   "captured wrapper changes only five call and two RIP-relative operands; runtime still requires the exact current body");
  bool allRejected=true;
  for(int i=0;i<200;i++)
  {
   var changed=(byte[])wrapper.Clone();changed[i]^=1;
   allRejected&=!L.MatchesComponentWrapper(changed.AsSpan(0,200));
  }
  check(allRejected&&!L.MatchesComponentWrapper(wrapper.AsSpan(0,199))
   ,
   "every changed wrapper byte, including address operands, and a truncated return reject lookup admission");
  check(L.TryGetterHelper(getter.AsSpan(0,74),WardogFunctionOffsets.InitialBulletSpeed,0x10000000,out var helperRva)
   &&helperRva==0x6BBD500&&L.MatchesHelper(helper.AsSpan(0,9)),
   "October 15 captured getter actually targets the unchanged nine-byte leaf at 0x6BBD500");
  var f=Build();
  void Bytes(ulong address,byte[] code){for(int i=0;i<code.Length;i++)f.Put(address+(ulong)i,code[i]);}
  Bytes(F.Base+WardogFunctionOffsets.InitialBulletSpeed,getter);Bytes(F.Base+helperRva,helper);
  check(f.Reader().CapturePredictionSpeed(Context()).Speed==88000,
   "independently captured current getter, leaf, wrapper and native lookup compose with synthetic identity/value data");
  const ulong lookupRva=NativeLookup;
  Bytes(F.Base+lookupRva,Enumerable.Repeat((byte)0xCC,1024).ToArray());f.Reads.Clear();
  check(f.Reader().CapturePredictionSpeed(Context()).Status=="Held actor component lookup layout unsupported"
   &&!f.Reads.ContainsKey(Held+0x240),
   "new wrapper cannot authorize collection traversal through an unknown native lookup");
 }

 static void BatchingChecks(Action<bool,string> check)
 {
  var f=Build();int scatterCalls=0,directCalls=0;long firstWave=0,lastWave=0,directStamp=0;
  bool mutate=false;int scatterDelay=15;
  void Scatter(IReadOnlyCollection<WardogReadBatch.Request> requests)
  {
   Thread.Sleep(scatterDelay);lastWave=Stopwatch.GetTimestamp();if(++scatterCalls==1)firstWave=lastWave;
   if(mutate&&scatterCalls==2)f.Put(Weapon+0x114,1.1f);
   foreach(var request in requests){request.SampleStarted=lastWave;request.Success=f.Read(request.Address,request.Bytes);}
  }
  var batch=new WardogReadBatch((ulong address,Span<byte> output)=>
   {directCalls++;Thread.Sleep(4);directStamp=Stopwatch.GetTimestamp();return f.Read(address,output);},Scatter,()=>directStamp);
  var reader=new WardogReader(F.Base,batch.Read,verifyScatter:Scatter,readSampleStarted:batch.SampleStarted);
  long started=Stopwatch.GetTimestamp();var context=Context();batch.BeginFrame();
  var warm=reader.CapturePredictionSpeed(context,captureStarted:started);batch.EndFrame();
  check(warm.Stamp==0&&warm.Status.Contains("expired")&&directCalls>0,"slow serial first discovery expires without stamping old data as fresh");
  WardogReader.PredictionSpeedSample sample=warm;
  var player=new WardogReader(F.Base,f.Read,f.Read,()=>f.Now,predictionSpeed:()=>sample,verifyScatter:requests=>
   {foreach(var request in requests)request.Success=f.Read(request.Address,request.Bytes);});
  for(int cycle=0;cycle<4;cycle++)
  {
   f.Put(Weapon+0xEE0,88000f+cycle*1000);scatterCalls=directCalls=0;firstWave=0;
   started=Stopwatch.GetTimestamp();context=Context();batch.BeginFrame();
   sample=reader.CapturePredictionSpeed(context,captureStarted:started);batch.EndFrame();
   check(sample.Speed==88000f+cycle*1000&&sample.IsFresh&&scatterCalls==2&&directCalls==0,
    "unchanged held weapon cycle "+cycle+" uses two fresh bulk reads instead of delayed serial discovery");
   check(sample.Stamp==firstWave&&sample.Stamp<lastWave&&sample.CheckedAt==started,
    "cycle "+cycle+" preserves original numerical transfer timestamp and never renews it during proof verification");
   check(player.Capture(prediction:true).LocalBulletSpeed==sample.Speed,
    "cycle "+cycle+" keeps completed live speed available to the player prediction consumer");
  }
  mutate=true;scatterCalls=0;started=Stopwatch.GetTimestamp();context=Context();batch.BeginFrame();
  var changed=reader.CapturePredictionSpeed(context,captureStarted:started);batch.EndFrame();
  check(changed.Stamp==0&&changed.Status.Contains("changed during recheck"),"batched numerical values changed between prefetch and fresh proof wave discard the sample");
  mutate=false;f.Put(Weapon+0x114,1f);scatterCalls=0;scatterDelay=160;
  started=Stopwatch.GetTimestamp();context=Context();batch.BeginFrame();int before=f.Calls;
  var delayed=reader.CapturePredictionSpeed(context,captureStarted:started);batch.EndFrame();
  check(delayed.Stamp==0&&delayed.Status.Contains("expired before sampling")&&f.Calls==before,
   "prefetch exceeding capture lifetime rejects before any further reads or timestamp renewal");
  foreach(long invalid in new[]{0L,Stopwatch.GetTimestamp()-Stopwatch.Frequency,Stopwatch.GetTimestamp()+Stopwatch.Frequency})
  {
   var timestampReader=new WardogReader(F.Base,f.Read,readSampleStarted:(_,_)=>invalid);
   var rejected=timestampReader.CapturePredictionSpeed(Context());
   check(rejected.Stamp==0&&rejected.Status.Contains("timestamp"),"absent, earlier-cycle or future numerical transfer timestamps reject publication: "+invalid);
  }
  // Range metadata is reset with byte success; neither scheduled nor failed reads
  // can inherit a timestamp from an earlier batch.
  var metadata=Build();long timestamp=Stopwatch.GetTimestamp();bool success=true;
  var ranges=new WardogReadBatch(metadata.Read,requests=>
   {foreach(var request in requests){request.Success=success&&metadata.Read(request.Address,request.Bytes);request.SampleStarted=timestamp;}},()=>timestamp);
  byte[] output=new byte[4];ranges.BeginFrame(0);ranges.ScheduleNext(100);ranges.Read(Weapon+0xEE0,output);ranges.EndFrame();
  check(ranges.SampleStarted(Weapon+0xEE0,4)==timestamp,"direct range captures original native transfer time");
  ranges.BeginFrame(50);
  check(ranges.SampleStarted(Weapon+0xEE0,4)==0,"not-yet-due range has no inherited numerical timestamp");ranges.EndFrame();
  success=false;ranges.BeginFrame(100);ranges.Read(Weapon+0xEE0,output);
  check(ranges.SampleStarted(Weapon+0xEE0,4)==0,"failed fresh range cannot reuse a successful old numerical timestamp");ranges.EndFrame();
  success=true;timestamp=Stopwatch.GetTimestamp();ranges.BeginFrame(101);ranges.Read(Weapon+0xEE0,output);
  check(ranges.SampleStarted(Weapon+0xEE0,4)==timestamp,"successful scatter preserves provided original transfer timestamp");ranges.EndFrame();
  ranges.BeginFrame(102);ranges.EndFrame();
  check(ranges.SampleStarted(Weapon+0xEE0,4)==0,"unused dropped range cannot expose its previous transfer timestamp");
  var fallback=new WardogReadBatch(metadata.Read,requests=>
   {Thread.Sleep(10);foreach(var request in requests)request.Success=metadata.Read(request.Address,request.Bytes);});
  fallback.BeginFrame();fallback.Read(Weapon+0xEE0,output);fallback.EndFrame();
  started=Stopwatch.GetTimestamp();fallback.BeginFrame();fallback.Read(Weapon+0xEE0,output);
  check(fallback.SampleStarted(Weapon+0xEE0,4)>=started&&Stopwatch.GetElapsedTime(fallback.SampleStarted(Weapon+0xEE0,4)).TotalMilliseconds>=8,
   "scatter without native time metadata uses conservative pre-transfer time rather than completion time");fallback.EndFrame();
 }
}

