# Native library promotion - October 18, 2026

After the user reported the isolated native-library build works well, its exact native bundle was promoted into Native/Dma in both the main Wardog project and the local GitHub edition. MemProcFS is 5.19.0.251, LeechCore is 2.23.4.104, and info.db matches the tested bundle. Both FTDI DLLs and the managed VmmSharpEx wrapper are unchanged. Provenance, SHA256 hashes and licenses ship with both editions.

Rebuilt outputs:
- Main: bin/Release/Wardog-DMA/MystieHook.exe with its accompanying libraries.
- Main release: artifacts/product-upload/Wardog.zip (18 files, all entry hashes match installed main; no personal Data).
- GitHub portable: bin/Release/Wardog-DMA/MystieHook.exe and the whole published folder in the GitHub project.
- GitHub compact release: artifacts/compact-portable/Wardog-DMA/MystieHook.exe in the GitHub project. This executable embeds the updated native libraries; extracted hashes were verified.

The main EXE hash remains 2BFA5C2CFDB83AF5DA072BCB34E99638A9410A10EC3B4579549BBA6AA0BCBE75 because the native libraries are external. Distribute the new complete ZIP, rather than replacing only that EXE. The GitHub compact EXE hash is 0DED8CA3613BAEEBBDEACAB617CF0325D452362394FEA0B0D872B96732E8D9ED. The main ZIP SHA256 is EA10C084A73D4315466D576A2BB1AC3E3736E4CAC1B20FE29B2C3079B20F0B4B.

94 offline native load/import/page-fixture checks passed against installed main. Main loader authorization, optimization-enabled and Lite-disabled flags passed; GitHub portable/compact optimization-enabled and Lite-disabled flags passed. All three package checks passed without opening DMA hardware. Current gameplay code, each edition's existing features, personal settings and web-radar server were preserved. No new read flags, timing, retries, device drivers or firmware were applied. The user-reported successful live test is not an independent stall benchmark; offline checks do not prove every hardware/firmware combination.

Previous files were copied and SHA256-verified under artifacts/Archive/before-native-promotion-20261018 (289 files, manifest.json). Evidence is artifacts/Archive/native-promotion-validation.json and native-promotion-*.log. Existing ordinary-local and usual optimization runtime folders were not replaced. The separate native-library test remains available.

This updates the local GitHub edition only, following the user's existing preference. No Git commit/push, GitHub release upload, server deployment, live DMA session or device/game write was performed.
