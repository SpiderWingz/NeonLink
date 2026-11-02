// Experimental movement adapter. Network input selects values only; this file
// chooses the local placeholder record and owns every entity it creates.
public native func NeonLinkPublish(position: Vector4, rotation: Quaternion) -> Bool
public native func NeonLinkPoll() -> Bool
public native func NeonLinkPosition() -> Vector4
public native func NeonLinkRotation() -> Quaternion
public native func NeonLinkGeneration() -> Uint32
public native func NeonLinkClear() -> Void
public native func NeonLinkIsPreview() -> Bool
public native func NeonLinkPeerConnected() -> Bool
public native func NeonLinkReport(stage: Int32) -> Void
public native func NeonLinkRemoteName() -> String
public native func NeonLinkPublishBuild(values: array<Int32>) -> Bool
public native func NeonLinkPresentation(marker: Bool, label: Bool) -> Void

public class NeonLinkPlayerMappinData extends MappinScriptData {
    public let playerName: String;
    public let labelVisible: Bool;
}

// Let the engine project its existing marker widget. Only Redline's own
// script-data type receives a custom name; ordinary quest/NPC UI is untouched.
@wrapMethod(WorldMappinsContainerController)
public func CreateMappinUIProfile(mappin: wref<IMappin>, mappinVariant: gamedataMappinVariant, customData: ref<MappinControllerCustomData>) -> MappinUIProfile {
    if IsDefined(mappin.GetScriptData() as NeonLinkPlayerMappinData) {
        return MappinUIProfile.Create(r"base\\gameplay\\gui\\widgets\\mappins\\quest\\default_mappin.inkwidget", t"MappinUISpawnProfile.Always", t"WorldMappinUIProfile.Default");
    }
    return wrappedMethod(mappin, mappinVariant, customData);
}

@wrapMethod(QuestMappinController)
protected cb func OnUpdate() -> Bool {
    let data: ref<NeonLinkPlayerMappinData>;
    wrappedMethod();
    if IsDefined(this.GetMappin()) {
        data = this.GetMappin().GetScriptData() as NeonLinkPlayerMappinData;
        if IsDefined(data) {
            data.labelVisible = this.GetDistanceToPlayer() <= 80.0 && !this.IsClamped();
            this.SetRootVisible(data.labelVisible);
            if inkWidgetRef.IsValid(this.displayName) {
                inkWidgetRef.SetVisible(this.displayName, data.labelVisible);
                inkTextRef.SetLetterCase(this.displayName, textLetterCase.OriginalCase);
                inkTextRef.SetText(this.displayName, data.playerName);
                inkWidgetRef.SetTintColor(this.displayName, new HDRColor(0.35, 0.95, 1.0, 1.0));
            } else {
                this.NeonLinkUpdateName(data.playerName, data.labelVisible);
                data.labelVisible = data.labelVisible && IsDefined(this.neonLinkNameLabel);
            }
        }
    }
}

@addField(QuestMappinController)
private let neonLinkNameLabel: ref<inkText>;

@addMethod(QuestMappinController)
private func NeonLinkUpdateName(name: String, visible: Bool) -> Void {
    let parent = this.GetRootWidget() as inkCompoundWidget;
    if !IsDefined(parent) { return; }
    if !IsDefined(this.neonLinkNameLabel) {
        this.neonLinkNameLabel = new inkText();
        this.neonLinkNameLabel.SetName(n"RedlinePartnerName");
        this.neonLinkNameLabel.SetFontFamily("base\\gameplay\\gui\\fonts\\raj\\raj.inkfontfamily");
        this.neonLinkNameLabel.SetFontStyle(n"Semi-Bold");
        this.neonLinkNameLabel.SetFontSize(28);
        this.neonLinkNameLabel.SetSize(420.0, 38.0);
        this.neonLinkNameLabel.SetAnchor(inkEAnchor.TopCenter);
        this.neonLinkNameLabel.SetAnchorPoint(new Vector2(0.5, 1.0));
        this.neonLinkNameLabel.SetMargin(new inkMargin(0.0, -20.0, 0.0, 0.0));
        this.neonLinkNameLabel.SetContentHAlign(inkEHorizontalAlign.Center);
        this.neonLinkNameLabel.SetTintColor(new HDRColor(0.35, 0.95, 1.0, 1.0));
        this.neonLinkNameLabel.SetInteractive(false);
        this.neonLinkNameLabel.Reparent(parent);
    }
    this.neonLinkNameLabel.SetText(name);
    this.neonLinkNameLabel.SetVisible(visible);
}

@wrapMethod(QuestMappinController)
protected cb func OnUninitialize() -> Bool {
    if IsDefined(this.neonLinkNameLabel) {
        (this.GetRootWidget() as inkCompoundWidget).RemoveChild(this.neonLinkNameLabel);
        this.neonLinkNameLabel = null;
    }
    wrappedMethod();
}

@wrapMethod(WorldMapTooltipController)
public func SetData(const data: script_ref<WorldMapTooltipData>, menu: ref<WorldMapMenuGameController>) -> Void {
    let playerData: ref<NeonLinkPlayerMappinData>;
    wrappedMethod(data, menu);
    if IsDefined(Deref(data).mappin) {
        playerData = Deref(data).mappin.GetScriptData() as NeonLinkPlayerMappinData;
        if IsDefined(playerData) {
            inkTextRef.SetText(this.m_titleText, playerData.playerName);
            inkTextRef.SetText(this.m_descText, NeonLinkIsPreview() ? "Redline simulated test partner" : "Redline connected player");
        }
    }
}

@if(ModuleExists("Codeware"))
public class NeonLinkMovementTick extends DelayCallback {
    public let service: wref<NeonLinkMovementService>;
    public let token: Uint32;

    public func Call() -> Void {
        if IsDefined(this.service) {
            this.service.Tick(this.token);
        }
    }
}

@if(ModuleExists("Codeware"))
public class NeonLinkMovementService extends ScriptableService {
    private let player: wref<PlayerPuppet>;
    private let partner: wref<NPCPuppet>;
    private let entitySystem: wref<DynamicEntitySystem>;
    private let entityID: EntityID;
    private let remoteGeneration: Uint32;
    private let token: Uint32;
    private let running: Bool;
    private let delayID: DelayID;
    private let pendingTicks: Int32;
    private let retryTicks: Int32;
    private let connected: Bool;
    private let markerID: NewMappinID;
    private let markerData: ref<NeonLinkPlayerMappinData>;
    private let metadataTicks: Int32;

    private cb func OnLoad() {
        NeonLinkReport(0);
    }

    private cb func OnInitialize() {
        this.Register();
    }

    private cb func OnReload() {
        this.Stop();
        this.Register();
    }

    private func Register() -> Void {
        let callbacks = GameInstance.GetCallbackSystem();
        callbacks.UnregisterCallback(n"Session/Ready", this);
        callbacks.UnregisterCallback(n"Session/BeforeEnd", this);
        callbacks.RegisterCallback(n"Session/Ready", this, n"OnReady", true);
        callbacks.RegisterCallback(n"Session/BeforeEnd", this, n"OnBeforeEnd");
    }

    private cb func OnReady(event: ref<GameSessionEvent>) {
        if event.IsPreGame() {
            return;
        }
        this.Stop();
        this.player = GetPlayer(GetGameInstance());
        this.entitySystem = GameInstance.GetDynamicEntitySystem();
        if !IsDefined(this.player) || !IsDefined(this.entitySystem) {
            return;
        }
        // The tag is exclusive to transient NeonLink proxy entities.
        this.entitySystem.DeleteTagged(n"NeonLink.MovementProxy");
        this.running = true;
        this.token += 1u;
        this.Tick(this.token);
    }

    private cb func OnBeforeEnd(event: ref<GameSessionEvent>) {
        this.Stop();
    }

    private cb func OnUninitialize() {
        // BeforeEnd has already removed the proxy while the world was valid.
        // Do not access disposed game systems during application shutdown.
        this.running = false;
        this.token += 1u;
        this.player = null;
        this.partner = null;
        this.entitySystem = null;
        NeonLinkClear();
    }

    private func Stop() -> Void {
        this.running = false;
        this.token += 1u;
        if IsDefined(this.player) {
            GameInstance.GetDelaySystem(this.player.GetGame()).CancelCallback(this.delayID);
        }
        this.RemovePartner();
        this.RemovePresentation();
        this.player = null;
        this.partner = null;
        this.entitySystem = null;
        this.remoteGeneration = 0u;
        this.connected = false;
        this.metadataTicks = 0;
        NeonLinkClear();
    }

    public func Tick(token: Uint32) -> Void {
        let callback: ref<NeonLinkMovementTick>;
        let connectedNow: Bool;
        if !this.running || token != this.token || !IsDefined(this.player) {
            return;
        }
        connectedNow = NeonLinkPeerConnected();
        if (connectedNow && !this.connected) || (!connectedNow && this.connected) {
            this.connected = connectedNow;
            this.Notify(this.connected ? "Redline: peer connected. Waiting for their character." : "Redline: peer connection ended.");
        }
        if this.player.IsDead() {
            NeonLinkClear();
            this.RemovePartner();
            this.RemovePresentation();
        } else {
            NeonLinkPublish(this.player.GetWorldPosition(), this.player.GetWorldOrientation());
            if this.metadataTicks <= 0 {
                this.PublishBuild();
                this.metadataTicks = 60;
            }
            this.metadataTicks -= 1;
            this.UpdatePartner();
        }
        callback = new NeonLinkMovementTick();
        callback.service = this;
        callback.token = token;
        // Render interpolation at roughly 60 Hz; native transport coalesces
        // local samples to 20 Hz. There is never an unbounded outgoing queue.
        this.delayID = GameInstance.GetDelaySystem(this.player.GetGame()).DelayCallback(callback, 0.016667);
    }

    private func PublishBuild() -> Void {
        let dev = PlayerDevelopmentSystem.GetData(this.player);
        let state = GameInstance.GetCharacterCustomizationSystem(this.player.GetGame()).GetState();
        let values: array<Int32>;
        if !IsDefined(dev) || !IsDefined(state) { return; }
        // Read only: local V keeps their complete save, inventory, cyberware and
        // perks. A network peer never calls setters on this player's systems.
        values = [
            dev.GetProficiencyLevel(gamedataProficiencyType.Level),
            dev.GetProficiencyLevel(gamedataProficiencyType.StreetCred),
            RoundMath(dev.GetAttributeValue(gamedataStatType.Strength)),
            RoundMath(dev.GetAttributeValue(gamedataStatType.Reflexes)),
            RoundMath(dev.GetAttributeValue(gamedataStatType.TechnicalAbility)),
            RoundMath(dev.GetAttributeValue(gamedataStatType.Intelligence)),
            RoundMath(dev.GetAttributeValue(gamedataStatType.Cool)),
            dev.GetDevPoints(gamedataDevelopmentPointType.Primary),
            dev.GetDevPoints(gamedataDevelopmentPointType.Attribute),
            dev.GetDevPoints(gamedataDevelopmentPointType.Espionage),
            dev.GetProficiencyLevel(gamedataProficiencyType.CoolSkill),
            dev.GetProficiencyLevel(gamedataProficiencyType.IntelligenceSkill),
            dev.GetProficiencyLevel(gamedataProficiencyType.ReflexesSkill),
            dev.GetProficiencyLevel(gamedataProficiencyType.StrengthSkill),
            dev.GetProficiencyLevel(gamedataProficiencyType.TechnicalAbilitySkill),
            state.IsBodyGenderMale() ? 1 : 0
        ];
        NeonLinkPublishBuild(values);
    }

    private func RemovePresentation() -> Void {
        if this.markerID.value != Cast<Uint64>(0) && IsDefined(this.player) {
            GameInstance.GetMappinSystem(this.player.GetGame()).UnregisterMappin(this.markerID);
        }
        this.markerID = new NewMappinID(Cast<Uint64>(0));
        this.markerData = null;
        NeonLinkPresentation(false, false);
    }

    private func UpdatePresentation(position: Vector4) -> Void {
        let data: MappinData;
        let system = GameInstance.GetMappinSystem(this.player.GetGame());
        let name = NeonLinkRemoteName();
        position.Z += 2.2;
        if this.markerID.value == Cast<Uint64>(0) {
            this.markerData = new NeonLinkPlayerMappinData();
            this.markerData.playerName = name;
            data.mappinType = t"Mappins.QuestStaticMappinDefinition";
            data.variant = gamedataMappinVariant.CustomPositionVariant;
            data.active = true;
            data.visibleThroughWalls = true;
            data.debugCaption = name;
            data.scriptData = this.markerData;
            this.markerID = system.RegisterMappin(data, position);
        }
        if IsDefined(this.markerData) { this.markerData.playerName = name; }
        if this.markerID.value != Cast<Uint64>(0) {
            system.SetMappinPosition(this.markerID, position);
        }
        NeonLinkPresentation(this.markerID.value != Cast<Uint64>(0), IsDefined(this.markerData) && this.markerData.labelVisible);
    }

    private func RemovePartner() -> Bool {
        if !EntityID.IsDefined(this.entityID) {
            this.partner = null;
            return true;
        }
        if !IsDefined(this.entitySystem) || !this.entitySystem.IsReady() {
            return false;
        }
        if this.entitySystem.IsManaged(this.entityID) && this.entitySystem.IsTagged(this.entityID, n"NeonLink.MovementProxy") {
            if !this.entitySystem.DeleteEntity(this.entityID) {
                return false;
            }
        }
        this.entityID = new EntityID();
        this.partner = null;
        this.pendingTicks = 0;
        return true;
    }

    private func Notify(message: String) -> Void {
        let notification: SimpleScreenMessage;
        if !this.running || !IsDefined(this.player) {
            return;
        }
        notification.isShown = true;
        notification.isInstant = true;
        notification.duration = 5.0;
        notification.message = message;
        notification.type = SimpleMessageType.Undefined;
        GameInstance.GetBlackboardSystem(this.player.GetGame()).Get(GetAllBlackboardDefs().UI_Notifications).SetVariant(GetAllBlackboardDefs().UI_Notifications.OnscreenMessage, ToVariant(notification), true);
    }

    private func UpdatePartner() -> Void {
        let position: Vector4;
        let rotation: Quaternion;
        let generation: Uint32;
        let spec: ref<DynamicEntitySpec>;
        if !IsDefined(this.entitySystem) || !this.entitySystem.IsReady() {
            NeonLinkReport(0);
            return;
        }
        if !NeonLinkPoll() {
            if IsDefined(this.partner) {
                this.Notify("Redline: partner character removed.");
            }
            this.RemovePartner();
            this.RemovePresentation();
            NeonLinkReport(1);
            return;
        }
        position = NeonLinkPosition();
        rotation = NeonLinkRotation();
        generation = NeonLinkGeneration();
        // The map position exists independently of the range-limited proxy.
        this.UpdatePresentation(position);
        // Avoid spawning in unloaded sectors when players are far apart.
        if Vector4.Distance(position, this.player.GetWorldPosition()) > 150.0 {
            this.RemovePartner();
            NeonLinkReport(2);
            return;
        }
        if generation != this.remoteGeneration {
            if !this.RemovePartner() {
                return;
            }
            this.remoteGeneration = generation;
        }
        if this.retryTicks > 0 {
            this.retryTicks -= 1;
            NeonLinkReport(5);
            return;
        }
        if !EntityID.IsDefined(this.entityID) {
            NeonLinkReport(3);
            spec = new DynamicEntitySpec();
            spec.recordID = t"Character.Judy";
            spec.position = position;
            spec.orientation = rotation;
            spec.persistState = false;
            spec.persistSpawn = false;
            spec.alwaysSpawned = true;
            spec.spawnInView = true;
            spec.active = true;
            spec.tags = [n"NeonLink.MovementProxy"];
            this.entityID = this.entitySystem.CreateEntity(spec);
            this.pendingTicks = 0;
            if !EntityID.IsDefined(this.entityID) {
                this.retryTicks = 300;
                NeonLinkReport(5);
            }
            return;
        }
        if !IsDefined(this.partner) {
            this.partner = this.entitySystem.GetEntity(this.entityID) as NPCPuppet;
            if !IsDefined(this.partner) {
                NeonLinkReport(3);
                this.pendingTicks += 1;
                if this.pendingTicks > 600 && this.RemovePartner() {
                    this.retryTicks = 300;
                }
                return;
            }
            if IsDefined(this.partner.GetAIControllerComponent()) {
                this.partner.GetAIControllerComponent().Toggle(false);
            }
            this.partner.DisableKillReward(true);
            this.partner.SetDisableRagdoll(true, true);
            this.Notify(NeonLinkIsPreview() ? "Redline: simulated test partner spawned." : "Redline: partner character spawned (movement preview).");
        }
        NeonLinkReport(this.entitySystem.IsSpawned(this.entityID) ? 4 : 3);
        GameInstance.GetTeleportationFacility(this.player.GetGame()).Teleport(this.partner, position, Quaternion.ToEulerAngles(rotation));
    }
}
