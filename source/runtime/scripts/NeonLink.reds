// Experimental movement adapter. Network input selects values only; this file
// chooses the local placeholder record and owns every entity it creates.
public native func NeonLinkPublish(position: Vector4, rotation: Quaternion) -> Bool
public native func NeonLinkPoll() -> Bool
public native func NeonLinkPosition() -> Vector4
public native func NeonLinkRotation() -> Quaternion
public native func NeonLinkGeneration() -> Uint32
public native func NeonLinkClear() -> Void

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

    private cb func OnLoad() {
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
        this.player = null;
        this.partner = null;
        this.entitySystem = null;
        this.remoteGeneration = 0u;
        NeonLinkClear();
    }

    public func Tick(token: Uint32) -> Void {
        let callback: ref<NeonLinkMovementTick>;
        if !this.running || token != this.token || !IsDefined(this.player) {
            return;
        }
        if this.player.IsDead() {
            NeonLinkClear();
            this.RemovePartner();
        } else {
            NeonLinkPublish(this.player.GetWorldPosition(), this.player.GetWorldOrientation());
            this.UpdatePartner();
        }
        callback = new NeonLinkMovementTick();
        callback.service = this;
        callback.token = token;
        // Render interpolation at roughly 60 Hz; native transport coalesces
        // local samples to 20 Hz. There is never an unbounded outgoing queue.
        this.delayID = GameInstance.GetDelaySystem(this.player.GetGame()).DelayCallback(callback, 0.016667);
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

    private func UpdatePartner() -> Void {
        let position: Vector4;
        let rotation: Quaternion;
        let generation: Uint32;
        let spec: ref<DynamicEntitySpec>;
        if !IsDefined(this.entitySystem) || !this.entitySystem.IsReady() {
            return;
        }
        if !NeonLinkPoll() {
            this.RemovePartner();
            return;
        }
        position = NeonLinkPosition();
        rotation = NeonLinkRotation();
        generation = NeonLinkGeneration();
        // Avoid spawning in unloaded sectors when players are far apart.
        if Vector4.Distance(position, this.player.GetWorldPosition()) > 150.0 {
            this.RemovePartner();
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
            return;
        }
        if !EntityID.IsDefined(this.entityID) {
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
            }
            return;
        }
        if !IsDefined(this.partner) {
            this.partner = this.entitySystem.GetEntity(this.entityID) as NPCPuppet;
            if !IsDefined(this.partner) {
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
        }
        GameInstance.GetTeleportationFacility(this.player.GetGame()).Teleport(this.partner, position, Quaternion.ToEulerAngles(rotation));
    }
}
