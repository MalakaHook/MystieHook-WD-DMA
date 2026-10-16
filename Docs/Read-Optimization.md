# Read optimization port

The established read optimization is enabled by default in this GitHub edition,
including both existing publish profiles. Build with
`-p:WardogReadOptimization=false` to compare the original buffer allocation and
large background batch behavior. `MystieHook.exe --print-read-optimization`
reports the compiled setting without opening a game or DMA connection.

The port includes:

- Filtered player-link/root acquisition followed by selected bone transforms,
  including required local-space ancestors, preserving source selection and
  fallback rules. Hidden confirmed teammates skip detail acquisition unless
  aim with Team check off or radar explicitly requests their data.
- Reused bounded page allocations. Every scan clears read success and sample
  stamps, freshly fetches touched pages and drops untouched allocations. Old
  bytes never satisfy a new failed read.
- Vehicle-priority native batches of at most 64 requests / 16 KiB, with a gate
  release between batches. The existing 256-request / 64 KiB maximum chunk
  bounds, priority/fairness and cancellation remain.
- A shared inclusive 1,000 m detail horizon for player ESP, radar/web radar,
  vehicles and remote occupant processing. Discovery/identity/position probes
  remain necessary outside the horizon for re-entry; pages can contain adjacent
  fields. Shorter user display ranges remain effective.
- Bounded per-reader requested bytes, page visits, gate wait and transfer
  timings, visible in Settings and local diagnostics exports. p95 uses the last
  1,024 transfers. Values describe requested work, not measured PCIe/USB
  throughput. InputManager and MemProcFS internal activity are outside these
  counters.

This is a focused port. Existing standalone launch, local-only web radar,
prediction implementation, offsets and application features retain this
edition's behavior. Camera/player/pose/bullet-speed freshness remains
100/250/80/150 ms; no timestamp is renewed to hide slow reads. The existing
renderer retention and player/vehicle publication order are unchanged.

Run regression checks without hardware:

```powershell
dotnet run --project Tests/Optimization.Tests/Optimization.Tests.csproj -c Release
dotnet run --project Tests/Optimization.Tests/Optimization.Tests.csproj -c Release -p:WardogReadOptimization=false
```

Compare the same scene/settings on an affected user's device. Smaller batches
can cost more round trips on some devices; synthetic byte savings and tests
do not establish live flicker removal or a universal speed improvement.
