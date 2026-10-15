# Wardog full dump audit - 2026-10-15

Supersedes the partial SDK update. Source: Wardog Dump 2026-10-15-06-58-22-942-231360. Full dump completed at 07:15:23, exit 0. SDK declarations, property types, ObjectsDump indices and configured globals were cross-checked. Gameplay validation remains outstanding.

PlayerBaseClassIndex = 6112 (0x17E0); VehicleBaseClassIndex = 3605; ModularVehicleClassIndex = 3301. These are object-table indices, not absolute class pointers.

MoverMesh is 0x3A0: UBHSkeletalMeshComponentBudgeted*. 0x390 is NetworkSmoothingComponent and 0x398 is UCapsuleComponent*. PlayerInventory is 0x758; InventoryHeldItem is 0x8E0. The former active-item fallback at 0xA78 is a TArray and has been disabled in both readers. PlayerName is restored to the derived AWDPlayerState FString at 0x430 at user request; its displayed-name semantics await live validation. SanitizedName remains at the base APlayerState FString at 0x350 and is not used by the current player-name read.

Reflected offsets establish storage location/type; obfuscated field meanings still need live validation. Native health handles, mesh transforms and bone buffers, alternate lookup candidates, and unused CharacterAimingAlpha/BleedoutState retain legacy values where this dump provides no reliable replacement. Configured globals are not independently rediscovered addresses.

| Constant | Value | Evidence / status |
| --- | --- | --- |
| GNames | 0xD050CC0 | Capture configuration; not independently rediscovered |
| GWorld | 0xD2AFC08 | Capture configuration; not independently rediscovered |
| GObjects | 0xD1234E0 | Capture configuration; not independently rediscovered |
| NumElements | GObjects + 0x14 | Derived: GObjects + configured GUObjectArray.NumElements (0x14) |
| GEngine | 0xD2B2EC0 | Capture configuration; not independently rediscovered |
| PlayerBaseClassIndex | 6112 | ObjectsDump: Class WDGame.WDMoverCharacter |
| PlayerClassObjectPointer | 0x10 | Retained legacy/native layout or fallback; not independently verified |
| VehicleBaseClassIndex | 3605 | ObjectsDump: Class Mechanized.BHBaseVehiclePawn |
| ModularVehicleClassIndex | 3301 | ObjectsDump: Class ModularVehicles.ModularVehicle |
| ActorClass | 0x10 | Retained legacy/native layout or fallback; not independently verified |
| ActorRootComponent | 0x1C0 | SDK field layout: AActor.RootComponent |
| SceneRelativeLocation | 0x168 | SDK field layout: USceneComponent.RelativeLocation |
| SceneRelativeRotation | 0x180 | SDK field layout: USceneComponent.RelativeRotation |
| MoverMesh | 0x3A0 | SDK field layout: ABHMoverPawn._aaabgaipgikagifemecgl |
| VehicleMesh | 0x760 | SDK field layout: ABHBaseVehiclePawn.Mesh |
| WeaponBehaviorComponent | 0x780 | SDK field layout: AWDMoverCharacter.WeaponBehaviorComponent |
| WeaponAdsMultiplier | 0x49C | Retained legacy/native layout or fallback; not independently verified |
| WeaponAdsBaseMultiplier | 0x4A0 | Retained legacy/native layout or fallback; not independently verified |
| WeaponAimingHint | 0x25C | Retained legacy/native layout or fallback; not independently verified |
| CharacterAimingAlpha | 0x81C | Retained legacy/native layout or fallback; not independently verified |
| PlayerInventory | 0x758 | SDK field layout: AWDMoverCharacter._aaabioeinobcihbnhdcaa |
| InventoryHeldItem | 0x8E0 | SDK field layout: UWDPlayerInventoryComponent._aaaailhochocjpenbfkcf |
| InventoryActiveItem | 0 | Disabled: old 0xA78 is array storage, not an item pointer |
| ItemId | 0x328 | SDK field layout: ABHItem.ItemId |
| MeshComponentToWorld | 0x1D0 | Retained legacy/native layout or fallback; not independently verified |
| SkinnedAsset | 0x5B0 | SDK field layout: USkinnedMeshComponent.SkinnedAsset |
| SkinnedAssetSecondary | 0x5A8 | SDK field layout: USkinnedMeshComponent.SkeletalMesh |
| SquadComponent | 0x5A8 | SDK field layout: AWDPlayerStateSession.SquadComponent |
| SquadId | 0x140 | SDK field layout: UWDSquadComponent._aaaahcplfddneehgopkhp |
| ControllerPlayerState | 0x2C0 | SDK field layout: AController.PlayerState |
| RawRefBoneInfo | 0x2E0 | Retained legacy/native layout or fallback; not independently verified |
| MeshBoneInfoSize | 0x0C | Retained legacy/native layout or fallback; not independently verified |
| PawnPlayerState | 0x2D8 | SDK field layout: APawn.PlayerState |
| ControllerPawn | 0x2F8 | SDK field layout: AController.Pawn |
| PlayerStatePawn | 0x330 | SDK field layout: APlayerState._aaaalhobkmjlooclobngj |
| ControllerPawnCurrent | 0x360 | SDK field layout: APlayerController._aaabajjjhggbdedppgfhj |
| PlayerStateFactionComponent | 0x5A0 | SDK field layout: AWDPlayerStateSession.FactionComponent |
| VitalityComponent | 0x748 | SDK field layout: AWDMoverCharacter.VitalityComponent |
| BleedoutState | 0x810 | Retained legacy/native layout or fallback; not independently verified |
| VehiclePlayerState | 0x890 | SDK field layout: ABHBaseVehiclePawn._aaaamgldgeckbnhdpcjng |
| StationaryVehicleFactionComponent | 0x0B18 | SDK field layout: AWDStationaryVehicle._aaabhjhgblplplkpdcdkd |
| AirplaneVehicleFactionComponent | 0x0BF8 | SDK field layout: AWDAirplaneVehicle._aaabhjhgblplplkpdcdkd |
| RotaryVehicleFactionComponent | 0x0BD8 | SDK field layout: AWDRotaryVehicle._aaabhjhgblplplkpdcdkd |
| TrackedVehicleFactionComponent | 0x0C38 | SDK field layout: AWDTrackedVehicle._aaabhjhgblplplkpdcdkd |
| WheeledVehicleFactionComponent | 0x0C28 | SDK field layout: AWDWheeledVehiclePawn._aaabhjhgblplplkpdcdkd |
| SavedNetworkAddress | 0x308 | SDK field layout: APlayerState._aaabddakkdpnnfnmcdcao |
| PlayerName | 0x430 | AWDPlayerState._aaaakbnbofjijpjaklakp (FString); user-selected name field, awaiting live validation |
| SanitizedName | 0x350 | SDK field layout: APlayerState._aaabbddpjdbloeogieacb |
| FactionComponentFaction | 0x128 | SDK field layout: UWDFactionComponent._aaaanlipcjeoacoemjbeg |
| FactionComponentTag | 0x110 | SDK field layout: UWDFactionComponent._aaaakmgkkkffomhlbcekh |
| FactionComponentTagAlt | 0x100 | SDK field layout: UWDFactionComponent._aaabbamfojekddkikpgca |
| FactionData | 0x2D0 | SDK field layout: AWDFaction.FactionData |
| FactionTag | 0x30 | SDK field layout: UWDFactionData._aaaakmgkkkffomhlbcekh |
| GenericTeamId | 0x108 | Retained legacy/native layout or fallback; not independently verified |
| CurrentHealthHandle | 0x140 | Retained legacy/native layout or fallback; not independently verified |
| AttributeValue | 0x30 | Retained legacy/native layout or fallback; not independently verified |
| AttributeValidity | 0x28 | Retained legacy/native layout or fallback; not independently verified |
| AttributeControlValidity | 0x08 | Retained legacy/native layout or fallback; not independently verified |
| VitalityCurrent | 0x130 | SDK field layout: UWDVitalityComponent.BaseHealth |
| VitalityMax | 0x134 | SDK field layout: UWDVitalityComponent.MaxHealth |
| CameraManager | 0x370 | SDK field layout: APlayerController.PlayerCameraManager |
| ControllerControlRotation | 0x330 | SDK field layout: AController.ControlRotation |
| CurrentCameraCache | 0x1560 | SDK field layout: APlayerCameraManager._aaabcehddoicbpbpidahp |
| CameraPov | 0x10 | Engine_struct.h: FCameraCacheEntry.POV at 0x10 |
| GameInstance | 0x0228 | SDK field layout: UWorld._aaabcjelfalhkeipancog |
| GameInstanceAlt | 0x12C8 | Retained legacy/native layout or fallback; not independently verified |
| WorldPersistentLevel | 0x30 | SDK field layout: UWorld.PersistentLevel |
| WorldLevels | 0x1C8 | SDK field layout: UWorld.Levels |
| WorldLevelsAlt1 | 0x120 | Retained legacy/native layout or fallback; not independently verified |
| WorldLevelsAlt2 | 0x130 | Retained legacy/native layout or fallback; not independently verified |
| WorldGameState | 0x1B0 | SDK field layout: UWorld.GameState |
| GameStatePlayerArray | 0x2D0 | SDK field layout: AGameStateBase._aaaalfkoioacmckkddban |
| GameStatePlayerArrayCount | 0x2D8 | Derived: GameStatePlayerArray + 8-byte TArray count offset |
| LevelActorsContainer | 0xE0 | Retained legacy/native layout or fallback; not independently verified |
| LevelActorsArray | 0x28 | Retained legacy/native layout or fallback; not independently verified |
| LevelActorsCount | 0x30 | Retained legacy/native layout or fallback; not independently verified |
| UObjectClass | 0x10 | Retained legacy/native layout or fallback; not independently verified |
| UObjectNameId | 0x18 | Retained legacy/native layout or fallback; not independently verified |
| ObjectItemSize | 0x18 | Retained legacy/native layout or fallback; not independently verified |
| ObjectItemObject | 0x10 | Retained legacy/native layout or fallback; not independently verified |
| ObjectsPerChunk | 0x10000 | Retained legacy/native layout or fallback; not independently verified |
| ClassNameId | 0x18 | Retained legacy/native layout or fallback; not independently verified |
| ClassSuper | 0x40 | Retained legacy/native layout or fallback; not independently verified |
| ObjectArrayNumChunks | 0x1C | Retained legacy/native layout or fallback; not independently verified |
| UObjectFlags | 0x8 | Retained legacy/native layout or fallback; not independently verified |
| ControllerPawnAlt | 0x308 | SDK field layout: AController.Character |
| LocalPlayers | 0x38 | SDK field layout: UGameInstance._aaaamacnlbgdkcnaikfep |
| LocalPlayerController | 0x30 | SDK field layout: UPlayer.PlayerController |
| NamePoolBlocks | 0x10 | Retained legacy/native layout or fallback; not independently verified |
| NameEntryHeader | 0x08 | Retained legacy/native layout or fallback; not independently verified |
| NameEntryData | 0x0C | Retained legacy/native layout or fallback; not independently verified |
| LevelActorContainer | LevelActorsContainer | Retained legacy/native layout or fallback; not independently verified |
| ActorArray | LevelActorsArray | Retained legacy/native layout or fallback; not independently verified |
| ActorArrayCount | LevelActorsCount | Retained legacy/native layout or fallback; not independently verified |
| BoneArray | 0x620 | Retained legacy/native layout or fallback; not independently verified |
| BoneArrayCache | 0x630 | Retained legacy/native layout or fallback; not independently verified |
| DirectBoneArrayFallbackCount | 256 | Retained legacy/native layout or fallback; not independently verified |
| FTransformTranslation | 0x20 | Retained legacy/native layout or fallback; not independently verified |
| FTransformSize | 0x60 | Retained legacy/native layout or fallback; not independently verified |

Source fingerprints (SHA-256):

- result.txt: 4D62760BA031D8BECD0A9878344CC53B2781551BD60DD041E8DF19210522D65B
- config.ini: B9CB1E02061CC3FF4F03BAF76F75533E110B6894442BC5C5592BA821A9BB193C
- Capture\ObjectsDump.txt: B2E8EA032B5BD79F9B9722E53DBAF26873C2D15D07B2C90ED9009868A92C0CAE
- Capture\DUMP\WDGame_classes.h: 536D8D4156A5367AF47383F0E87C04621E945B5ACCB5D3FADA629F2D23102075
- Capture\DUMP\BHMover_classes.h: BFD70508E42997D9D6C9FFD553DD639E62FF120FE417106D5F64780338A2CD74
- Capture\DUMP\Mechanized_classes.h: F390C4F92EC11CF18B0AF9E938C71B8C70AF3BBA86150097FC057E0854464AF3
- Capture\DUMP\Engine_classes.h: 225A7BD61AD6D93CEEAC99698AF1C9D11C2C68E16442A2C4D22DAFB63A3FDFB4
