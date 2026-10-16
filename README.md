# MystieHook Wardog — 


## Build and launch

Windows x64, .NET 9 SDK and the existing supported DMA hardware/drivers are required to build. Install Node.js **22 or newer** and make `node` available on PATH to use web radar. No npm packages are required.

```powershell
dotnet publish MystieHook.csproj -p:PublishProfile=Properties/PublishProfiles/DmaPortable.pubxml -c Release
```

Run `bin\Release\Wardog-DMA\MystieHook.exe`, or `Start-Wardog.cmd` in that folder for troubleshooting. Copy the **whole published folder**, including Assets, WebRadar, and native libraries. Launch goes directly into the application. The published .NET runtime is self-contained; Node.js remains a separate prerequisite for web radar only.

## Read optimization

Read optimizations are enabled in both publish profiles: filtered bone reads,
fresh reusable page buffers, smaller background batches and a shared 1,000 m
detail cutoff. Settings and exported diagnostics include per-reader timings.
See [Read optimization](Docs/Read-Optimization.md) for validation and comparison
instructions. The existing 100/250 ms camera/player freshness limits remain.

## Maps: manual installation

For a compact distribution, publish with `dotnet publish MystieHook.csproj -p:PublishProfile=Properties/PublishProfiles/DmaCompact.pubxml -c Release`.
The output is `artifacts\compact-portable\Wardog-DMA\MystieHook.exe`.
This bundles the runtime, native dependencies, Assets and WebRadar into the executable.
On launch, .NET extracts the bundled files into its per-user cache; it needs writable temporary storage.
DMA drivers, Node.js for web radar, and map tile packages remain separate prerequisites.
Run `MystieHook.exe --diagnose-startup` to check assets and native libraries without opening DMA hardware.

Copy the existing map package folders into `%LOCALAPPDATA%\MystieHook\Maps` on the computer running MystieHook. Preserve the existing structure:

```text
Maps/
  bakurani/<package SHA-256>/0/0/0.webp
  ozeti/<package SHA-256>/0/0/0.webp
  zestafona/<package SHA-256>/0/0/0.webp
```

Keep all zoom levels and tiles, not just the example tile. Expected version folder names are listed in `Services/LocalMaps.cs` and `WebRadar/server.mjs`. No download or installation check blocks startup. The desktop has low-resolution bundled fallback maps; web radar needs the local tile packages to display its background. Missing tiles do not trigger downloads. Map attribution remains wardogs.tech.

## Local web radar

In Radar, press **Start web radar**, then **Open radar**. MystieHook starts a hidden local Node process automatically and stops it when sharing stops or the application exits normally. The browser address is `http://127.0.0.1:3080/<session>`; `http://127.0.0.1:3080` opens the active session too. Port 3080 must be available.

Loopback is accessible only on the **same computer**. This edition does not expose radar to phones, other PCs, or the Internet. Per-session upload tokens and optional viewer passwords remain; they are local session controls, not license checks. Snapshot expiry and transport freshness checks remain unchanged.

## Repository contents

Source, UI assets, project/publish files, native runtime dependencies, third-party notices and the local web radar are included. Personal configs, credentials, license backend/deployment files, game dumps, build output, logs and original Git history are excluded. The original project is unchanged.

Native libraries and other bundled third-party assets retain their own redistribution terms; see `Native/Dma` and `ThirdParty`. No new license has been assigned to your original source code by this copy operation.
