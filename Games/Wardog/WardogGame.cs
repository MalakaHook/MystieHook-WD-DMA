using MystieHook.Gui;
using ImGuiNET;
using System.Diagnostics;
using static MystieHook.OverlayWindow;

namespace MystieHook.Games.Wardog;

// Menu adapter only; the monitor and overlay can be reused by a replacement UI.
public sealed partial class WardogGame : IGame
{
    private readonly WardogAimController _aimController = new();
    private bool _captureAimKey;
    private WardogArtillerySettings _artillery = new();
    public string Name => "Wardog";
    public bool IsAttached => _monitor.IsAttached;
    public WardogReadStatistics.Sample[] ReadStatistics=>_monitor.ReadStatistics;
    public WardogVisualSettings PreviewVisuals => _monitor.Visuals;
    public bool PreviewEnabled => _overlay;
    private string _processName = "WardogsClient-Win64-Shipping.exe";
    private readonly WardogMonitor _monitor = new();
    private bool _overlay = true;
    public bool AutoConnect { get; set; } = true;
    private long _nextAttempt;
    public void Reconnect()
    {
        _reconnectRequested = true;
        _monitor.Stop();
        _nextAttempt = 0;
    }
    public MystieHook.Services.WardogPreferences CapturePreferences() => new()
    {
        ProcessName = _processName, Overlay = _overlay,
        ReadHz = _monitor.TargetHz, Visuals = _monitor.Visuals, Aim = _monitor.Aim, Radar = _monitor.Radar, Artillery = _artillery, Vehicles = _monitor.VehicleSettings,
        Rcs = _monitor.Rcs,
    };

    public void ApplyPreferences(MystieHook.Services.WardogPreferences settings)
    {
        if (_processName != settings.ProcessName) Stop();
        _processName = settings.ProcessName;
        _overlay = settings.Overlay;
        _monitor.TargetHz = settings.ReadHz;
        _monitor.VehicleSettings = settings.Vehicles;
        _monitor.Radar = settings.Radar;
        _monitor.Aim = settings.Aim;
        _monitor.Rcs = settings.Rcs;
        _calibrationResetBaseline = settings.Aim.Calibration;
        _artillery = settings.Artillery;
        _monitor.Visuals = settings.Visuals with
        {
            SkeletonHz = 0 // Legacy setting; poses now follow every player-reader cycle.
        };
    }
    public void Initialize() { }
    public void Tick()
    {
        if (!AutoConnect || _monitor.IsRunning || Environment.TickCount64 < _nextAttempt) return;
        _nextAttempt = Environment.TickCount64 + 3000;
        Start();
    }
    public void Start()
    {
        if (!_monitor.IsRunning) { _connectionAttempts++; _reconnectRequested = false; }
        _monitor.Start(_processName);
    }
    public void Stop() { _webRadar.Stop(); _calibration.Cancel(); _aimController.Stop(); _monitor.Stop(); }
    public void Draw(ImGuiWindowFlags winFlags)
    {
        DrawOverlay();
        if (!UiVisibility.MenusHidden) DrawVisuals();
    }

    public void DrawOverlay()
    {
        bool webSharing=_webRadar.Status.Active;
        _monitor.SetWebRadarDemand(webSharing);
        _monitor.SetMapDemand(webSharing,webSharing,webSharing && _artillery.AutoMap);
        var size = ImGui.GetMainViewport().Size;
        if (size.Y > 0) _monitor.AspectRatio = size.X / size.Y;
        _monitor.VisualsEnabled = _overlay;
        var players = _monitor.Current;
        var camera = _monitor.Camera;
        var frame = WardogReader.WithCamera(players, camera);
        ObserveDiagnostics(players, camera);
        var aim=_monitor.Aim;
        DrawCalibrationOverlay(size);
        _aimController.Update(frame,aim,(int)size.X,(int)size.Y,!UiVisibility.MenusHidden || _calibration.BlocksAim,camera,_monitor.Rcs);
        bool aimHeld = aim.Enabled && MystieHook.Input.InputManager.IsKeyDown(aim.Key);
        var targetFrame = aimHeld ? _aimController.LockedFrame(frame) : frame;
        if (aim.ShowPrediction)
        {
            var draw = ImGui.GetForegroundDrawList();
            foreach(var player in targetFrame.Players)
            {
                if(WardogPrediction.ForPlayer(targetFrame,player,aim,(int)size.X,(int)size.Y) is not WardogPrediction.Dot dot)continue;
                var point=ImGui.GetMainViewport().Pos+dot.Point;
                draw.AddCircleFilled(point,aim.PredictionDotRadius,aim.HeadPredictionColor|0xFF000000,16);
                if(aim.ShowPredictionDetails)draw.AddText(point+new System.Numerics.Vector2(6,0),0xE6FFFFFF,$"{dot.DistanceMetres:0}m / {dot.FlightSeconds:0.00}s");
            }
        }
        if(aim.Enabled || aim.ShowPrediction)
        {
            var draw=ImGui.GetForegroundDrawList();var origin=ImGui.GetMainViewport().Pos;var center=origin+size/2;
            if(aim.ShowFov)draw.AddCircle(center,aim.Fov,ImGui.GetColorU32(ImGuiCol.CheckMark),96);
            if(aimHeld && aim.TargetLine
                && WardogAimSettings.Select(targetFrame,aim,(int)size.X,(int)size.Y) is System.Numerics.Vector2 target)
                draw.AddLine(center,origin+target,aim.LineColor,1.5f);
        }
        var display = _monitor.ObservedRenderFrame();
        if (_overlay && display.IsFresh) WardogOverlay.Draw(display, _monitor.Visuals);
        WardogVehicleOverlay.Draw(_monitor.Vehicles, display, _monitor.VehicleSettings);
        _monitor.Radar = WardogRadarOverlay.Draw(frame, _monitor.Vehicles, _monitor.Radar, !UiVisibility.MenusHidden);
    }

    public void DrawAim()
    {
        var s=_monitor.Aim;
        bool enabled=s.Enabled,team=s.TeamCheck,line=s.TargetLine,fovShown=s.ShowFov,prediction=s.ShowPrediction,aimPrediction=s.UsePrediction;
        float fov=s.Fov,distance=s.Distance;int bone=s.Bone,key=s.Key,targetMode=(int)s.AimTarget;
        var color=ImGui.ColorConvertU32ToFloat4(s.LineColor);
        MenuTools.Checkbox("Enable aim",ref enabled);
        SafeText.Wrapped($"Last aim attempt: {_aimController.LastAttempt}");
        SafeText.Disabled("Camera calibration supports ADS zoom using the current FOV. Separate in-game ADS sensitivity may change the feel.");
        if(!s.UseCalibration || s.Calibration is not {Valid:true})
            ImGui.TextWrapped("Aim movement requires an enabled calibration profile. Calibrate below or apply a saved Aim configuration.");
        MenuTools.Combo("Target bone",ref bone,"Head\0Neck\0Chest\0Pelvis\0Smart\0");
        MenuTools.Combo("Aim Target",ref targetMode,"Closest to crosshair\0Closest to me\0");
        if(targetMode==(int)WardogAimTarget.ClosestToMe)SafeText.Wrapped("Chooses the nearest player inside the aim FOV; equal distances favor the player closest to your crosshair.");
        SafeText.Wrapped("Locks one player per key hold. Temporary missing data pauses aim; it resumes only on that player. Release and press again to select another player.");
        MenuTools.Checkbox("Team check",ref team);
        MenuTools.SliderFloat("Maximum distance (m)",ref distance,1,WardogReadRange.MaximumMetres,"%.0f");
        MenuTools.SliderFloat("Aim FOV (px)",ref fov,1,600,"%.0f");MenuTools.Checkbox("Show FOV",ref fovShown);
        MenuTools.Checkbox("Target line",ref line);if(line)MenuTools.ColorEdit4("Target line color",ref color,ImGuiColorEditFlags.NoInputs);
        MenuTools.Checkbox("Show prediction", ref prediction);
        MenuTools.Checkbox("Aim prediction", ref aimPrediction);
        if (aimPrediction) SafeText.Wrapped("Prediction follows Target bone. Smart chooses the eligible bone closest to the crosshair. Missing live speed, bone or movement data pauses aim until fresh data returns.");
        if (prediction || aimPrediction)
        {
            var snapshot = _monitor.Current;
            if(ImGui.Button("Refresh bullet-speed cache"))_monitor.RefreshPredictionSpeed();
            SafeText.Disabled(snapshot.IsFresh && snapshot.HasPredictionSpeed ? $"{(snapshot.BulletSpeedCached?"Cached":"Live")} bullet speed: {snapshot.LocalBulletSpeed/100:0.#} m/s" : snapshot.PredictionSpeedReadStatus);
            float gravity=s.BulletGravityCm,radius=s.PredictionDotRadius;bool details=s.ShowPredictionDetails;
            var dotColor=ImGui.ColorConvertU32ToFloat4(s.HeadPredictionColor);
            MenuTools.SliderFloat("Bullet gravity estimate (cm/s2)",ref gravity,0,5000,"%.0f");
            MenuTools.SliderFloat("Prediction dot radius",ref radius,1,20,"%.1f");
            MenuTools.Checkbox("Show prediction details",ref details);
            MenuTools.ColorEdit4("Prediction color",ref dotColor,ImGuiColorEditFlags.NoInputs|ImGuiColorEditFlags.NoAlpha);
            s=s with {BulletGravityCm=gravity,PredictionDotRadius=radius,ShowPredictionDetails=details,HeadPredictionColor=ImGui.ColorConvertFloat4ToU32(dotColor)|0xFF000000};
            SafeText.Wrapped("Selected-bone prediction for confirmed enemies using live projectile speed. Gravity is an explicit estimate, not a decoded projectile value. Bounded five-second flight; no drag or zeroing compensation.");
        }
        if(ImGui.Button(_captureAimKey ? "Press aim key..." : "Set aim key"))_captureAimKey=true;
        else if(_captureAimKey)
        {
            for(int vk=1;vk<=254;vk++)if(MystieHook.Input.InputManager.IsKeyPressed(vk))
            {if(vk!=27)key=vk;_captureAimKey=false;break;}
        }
        ImGui.SameLine();ImGui.TextUnformatted(MystieHook.Input.VkNames.Name(key)??"Unknown key");
        if(_captureAimKey&&ImGui.Button("Cancel key capture"))_captureAimKey=false;
        ImGui.TextWrapped("Hold the aim key with the menu hidden. Uses Makcu connected in Settings; no local mouse fallback. Team check requires a confirmed enemy. Unresolved factions and unreliable bones can prevent or misplace targets.");
        ImGui.TextUnformatted(MystieHook.Input.Device.connected ? "Makcu connected" : "Makcu disconnected: movement disabled");
        _monitor.Aim=s with {Enabled=enabled,ShowPrediction=prediction,UsePrediction=aimPrediction,AimTarget=(WardogAimTarget)targetMode,Fov=fov,ShowFov=fovShown,Key=key,Bone=bone,TargetLine=line,LineColor=ImGui.ColorConvertFloat4ToU32(color),TeamCheck=team,Distance=distance};
        DrawCalibrationControls();
    }

    public void DrawRadar()
    {
        ImGui.SeparatorText("Map minimap");
        var r=_monitor.Radar.ApplyDetectedMap(_monitor.Current);
        bool enabled=r.Enabled,players=r.Players,teammates=r.Teammates,names=r.Names,vehicles=r.Vehicles,auto=r.AutoMap,debug=r.DebugNames;
        float zoom=r.Zoom,size=r.Size;int map=r.Map;
        MenuTools.Checkbox("Enable minimap",ref enabled);
        MenuTools.SliderFloat("Minimap zoom",ref zoom,1,128,"%.1fx");
        MenuTools.SliderFloat("Minimap size (px)",ref size,180,700,"%.0f");
        MenuTools.Checkbox("Draw players",ref players);
        MenuTools.Checkbox("Draw teammates",ref teammates);
        MenuTools.Checkbox("Draw vehicles",ref vehicles);
        MenuTools.Checkbox("Player names",ref names);
        MenuTools.Checkbox("Detect minimap automatically",ref auto);
        if(MenuTools.Combo("Minimap battlefield",ref map,"Bakurani\0Ozeti\0Zestafona\0"))auto=false;
        SafeText.Disabled(!auto?"Manual map selection":WardogArtillerySettings.HasDetectedMap(_monitor.Current)
            ?"Detected: "+WardogArtilleryData.Maps[_monitor.Current.DetectedMap].Name:"Map unknown: keeping your selected battlefield.");
        SafeText.Wrapped("Always centered on you and rotated with your game camera. Drag the minimap while the menu is open to move it. Its position is remembered automatically. Map imagery: wardogs.tech. Teammates applies to players and occupied vehicles; unknown teams remain visible. Save in Config to keep these settings.");
        if(ImGui.Button("Reset minimap position"))r=r with {X=20,Y=80};
        MenuTools.Checkbox("Map debug names",ref debug);
        _monitor.Radar=r with {Enabled=enabled,Players=players,Teammates=teammates,Names=names,Vehicles=vehicles,Zoom=zoom,Size=size,AutoMap=auto,Map=map,DebugNames=debug};
        if(debug)
        {
            var snapshot=_monitor.Current;var info=snapshot.MapDebug;
            bool fresh=snapshot.IsFresh && info!=null && Stopwatch.GetElapsedTime(info.SampleStarted).TotalSeconds<2;
            SafeText.Disabled(fresh?"Map debug: latest scheduled sample":"Map debug: waiting for fresh names");
            if(fresh && info!=null){SafeText.Wrapped(info.Report);if(ImGui.Button("Copy map debug report"))ImGui.SetClipboardText(info.Report);}
        }
        ImGui.Separator();
        DrawWebRadar();
    }

    public void DrawVehicles()
    {
        var settings = _monitor.VehicleSettings;
        bool show = settings.ShowVehicles, teammates = settings.ShowTeammateVehicles;
        float distance = settings.MaximumDistance;
        bool changed = MenuTools.Checkbox("Show Vehicle", ref show);
        changed |= MenuTools.SliderFloat("Maximum distance (m)", ref distance, 0, WardogReadRange.MaximumMetres, "%.0f");
        changed |= MenuTools.Checkbox("Show teammate vehicle", ref teammates);
        if (changed) _monitor.VehicleSettings = new(show, teammates, distance);
        ImGui.TextWrapped("When teammate vehicles are hidden, vehicles with a confirmed friendly occupant are excluded. Empty vehicles and unknown occupants remain visible.");
        ImGui.TextDisabled(show ? "Known NameIDs show Helicopter, Transport or Heavy. Unmapped vehicles show their raw identifiers." : "Vehicle reading is disabled.");
    }

    public void DrawSettings()
    {
        var frame = _monitor.Current;
        bool fresh = frame.IsFresh;
        double age = frame.SampleStarted == 0 ? 0 : Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds;
        DrawConnectionRecovery();
        int targetHz = _monitor.TargetHz;
        if (MenuTools.SliderInt("Read target (Hz)", ref targetHz, 30, 120))
            _monitor.TargetHz = targetHz;
        ImGui.TextUnformatted($"Overlay: {ImGui.GetIO().Framerate:0} FPS | Read target: {_monitor.TargetHz} Hz");
        if (frame.SampleStarted != 0)
            ImGui.TextUnformatted($"Refresh: {frame.RefreshHz:0.0} Hz | Read: {frame.ReadMilliseconds:0.0} ms | Age: {age:0} ms | Batch: {frame.BatchedReads} | Direct: {frame.DirectReads}");
        ImGui.TextUnformatted($"DMA: {frame.DmaMilliseconds:0.0} ms | Waiting: {frame.DmaWaitMilliseconds:0.0} ms | Calls: {frame.DmaOperations}");
        var camera = _monitor.Camera;
        var cs = _monitor.CameraStats;
        if (cs != null)
        {
            string cameraAge = camera.SampleStarted == 0 ? "unavailable" : $"{Stopwatch.GetElapsedTime(camera.SampleStarted).TotalMilliseconds:0.0} ms";
            ImGui.TextUnformatted($"Camera: {cs.Hz:0} Hz | Read: {cs.ReadMs:0.0} ms | Wait: {cs.WaitMs:0.0} ms | Age: {cameraAge}");
            SafeText.Wrapped(cs.Status);
        }
        ImGui.TextUnformatted(_monitor.RenderStatus);
        if (WardogReadOptimization.Active)
        {
            ImGui.Separator();
            SafeText.Wrapped("Read optimization | Timing samples cover the last 1024 transfers per reader.");
            foreach (var s in _monitor.ReadStatistics)
                SafeText.Wrapped($"{s.Stream}: requested {s.RequestedKiBPerSecond:0.0} KiB/s average | wait p95 {s.WaitP95:0.00} ms | transfer p95 {s.TransferP95:0.00} ms | max {s.LongestTransfer:0.00} ms");
        }

        DrawHeldItemInspector();
        DrawFactionInspector();
    }

    public void DrawVisuals()
    {
        var frame = _monitor.Current;
        bool fresh = frame.IsFresh;
        var visuals = _monitor.Visuals;
        MenuTools.Checkbox("Enable Player ESP", ref _overlay);
        bool teammates=visuals.ShowTeammates, bones=visuals.ShowBones, dot=visuals.ShowHeadDot;
        bool names=visuals.ShowName, health=visuals.ShowHealth, faction=visuals.ShowFaction;
        bool currentItem = visuals.ShowCurrentItem;
        bool dead = visuals.ShowDeadPlayers;
        bool downed = visuals.ShowDownedPlayers;
        bool distances = visuals.ShowDistances, boxes = visuals.ShowBoxes;
        int boxStyle = (int)visuals.BoxStyle, healthPosition = (int)visuals.HealthPosition;
        float nameSize=visuals.NameTextSize, distanceSize=visuals.DistanceTextSize, factionSize=visuals.FactionTextSize;
        float distance=visuals.MaximumDistance;
        float dotSize = visuals.HeadDotSize, boneThickness = visuals.BoneThickness;
        var dotColor = ImGui.ColorConvertU32ToFloat4(visuals.HeadDotColor ?? 0xFF7070FFu);
        var boneColor = ImGui.ColorConvertU32ToFloat4(visuals.BoneColor ?? 0xFF7070FFu);
        var boxColor = ImGui.ColorConvertU32ToFloat4(visuals.BoxColor ?? 0xFF7070FFu);
        bool dotColorChanged = false, boneColorChanged = false, boxColorChanged = false;
        bool changed=MenuTools.Checkbox("Show teammates", ref teammates);
        if (!teammates) ImGui.TextDisabled("Confirmed teammates are hidden; enemies and unknown factions are shown.");
        changed |= MenuTools.Checkbox("Show dead players", ref dead);
        changed |= MenuTools.Checkbox("Show downed players", ref downed);
        changed |= MenuTools.Checkbox("Show distances", ref distances);
        changed |= MenuTools.Checkbox("Show Boxes", ref boxes);
        ImGui.SameLine(); ImGui.SetNextItemWidth(170);
        changed |= MenuTools.Combo("##BoxStyle", ref boxStyle, "Cornered Boxes\0Normal Boxes\0");
        if (boxes)
        {
            boxColorChanged = MenuTools.ColorEdit4("Box color", ref boxColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar);
            changed |= boxColorChanged;
            if (visuals.BoxColor is null) ImGui.TextDisabled("Using team colors until a custom color is selected.");
        }
        changed |= MenuTools.Checkbox("Show bones", ref bones);
        if (bones)
        {
            changed |= MenuTools.SliderFloat("Bone thickness (px)", ref boneThickness, 1, 10, "%.1f");
            boneColorChanged = MenuTools.ColorEdit4("Bone color", ref boneColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar);
            changed |= boneColorChanged;
            if (visuals.BoneColor is null) ImGui.TextDisabled("Using team colors until a custom color is selected.");
            ImGui.TextDisabled("Bones and boxes update together at the player read rate.");
        }
        changed |= MenuTools.Checkbox("Head dot", ref dot);
        if (dot)
        {
            changed |= MenuTools.SliderFloat("Head dot radius (px)", ref dotSize, 1, 10, "%.1f");
            dotColorChanged = MenuTools.ColorEdit4("Head dot color", ref dotColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar);
            changed |= dotColorChanged;
            if (visuals.HeadDotColor is null) ImGui.TextDisabled("Using team colors until a custom color is selected.");
        }
        changed |= MenuTools.Checkbox("Player name", ref names);
        changed |= MenuTools.Checkbox("Show faction", ref faction);
        changed |= MenuTools.Checkbox("Health bar", ref health);
        ImGui.SameLine(); ImGui.SetNextItemWidth(140);
        changed |= MenuTools.Combo("##HealthPosition", ref healthPosition, "Left\0Right\0Top\0Bottom\0");
        changed |= MenuTools.Checkbox("Show current Item in hand", ref currentItem);
        changed |= MenuTools.SliderFloat("Maximum distance (m)", ref distance, 5, WardogReadRange.MaximumMetres);
        ImGui.SeparatorText("Text size");
        changed |= MenuTools.SliderFloat("Player name (px)", ref nameSize, 10, 32, "%.0f");
        changed |= MenuTools.SliderFloat("Player distance (px)", ref distanceSize, 10, 32, "%.0f");
        changed |= MenuTools.SliderFloat("Player faction (px)", ref factionSize, 10, 32, "%.0f");
        if (changed)
        {
            visuals = visuals with
            {
                NameTextSize = nameSize, DistanceTextSize = distanceSize, FactionTextSize = factionSize,
                ShowTeammates = teammates, ShowBones = bones, ShowHeadDot = dot,
                ShowName = names, ShowFaction = faction, ShowHealth = health, MaximumDistance = distance,
                ShowCurrentItem = currentItem, ShowDeadPlayers = dead, ShowDownedPlayers = downed,
                ShowDistances = distances, ShowBoxes = boxes, BoxStyle = (WardogBoxStyle)boxStyle,
                HealthPosition = (WardogHealthPosition)healthPosition,
                SkeletonHz = 0,
                HeadDotSize = dotSize, BoneThickness = boneThickness,
                HeadDotColor = dotColorChanged ? ImGui.ColorConvertFloat4ToU32(dotColor) : visuals.HeadDotColor,
                BoneColor = boneColorChanged ? ImGui.ColorConvertFloat4ToU32(boneColor) : visuals.BoneColor,
                BoxColor = boxColorChanged ? ImGui.ColorConvertFloat4ToU32(boxColor) : visuals.BoxColor
            };
            _monitor.Visuals = visuals;
        }

        if (!fresh) return;
        ImGui.TextUnformatted($"Visible players: {frame.Players.Count(p => visuals.Allows(p.Position, frame.LocalPosition, p.TeamRelation, p.Dead, p.Downed))}");
        if (ImGui.BeginTable("WardogPlayers", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Player"); ImGui.TableSetupColumn("Distance");
            ImGui.TableSetupColumn("Health");
            ImGui.TableHeadersRow();
            foreach (var player in frame.Players)
            {
                if (!visuals.Allows(player.Position, frame.LocalPosition, player.TeamRelation, player.Dead, player.Downed)) continue;
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(player.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{player.Position.Distance(frame.LocalPosition)/100:0} m");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(player.Health < 0 ? "Unknown" : $"{player.Health}%");
            }
            ImGui.EndTable();
        }
    }
}

