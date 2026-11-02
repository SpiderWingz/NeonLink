// Isolated local rendering experiment. Never installed by the player bundle.
public native func NeonLinkAppearanceLog(message: String) -> Void

public class RedlineAppearanceTrialTick extends DelayCallback {
    public let service: wref<RedlineAppearanceTrial>;
    public func Call() -> Void { if IsDefined(this.service) { this.service.Start(); } }
}

public class RedlineAppearanceTrial extends ScriptableService {
    private let trialID: EntityID;
    private let visuals: array<ref<IComponent>>;

    private cb func OnInitialize() {
        GameInstance.GetCallbackSystem().RegisterCallback(n"Entity/Extract", this, n"OnExtract");
        GameInstance.GetCallbackSystem().RegisterCallback(n"Entity/Initialize", this, n"OnEntityInitialize");
        GameInstance.GetCallbackSystem().RegisterCallback(n"Entity/AfterAttach", this, n"OnAfterAttach");
        GameInstance.GetCallbackSystem().RegisterCallback(n"Session/Ready", this, n"OnReady", true);
        GameInstance.GetCallbackSystem().RegisterCallback(n"Session/BeforeEnd", this, n"OnEnd");
    }
    private cb func OnReady(event: ref<GameSessionEvent>) {
        let callback: ref<RedlineAppearanceTrialTick>;
        let player = GetPlayer(GetGameInstance());
        if event.IsPreGame() || !IsDefined(player) { return; }
        callback = new RedlineAppearanceTrialTick(); callback.service = this;
        GameInstance.GetDelaySystem(player.GetGame()).DelayCallback(callback, 8.0);
    }
    private cb func OnEnd(event: ref<GameSessionEvent>) {
        if EntityID.IsDefined(this.trialID) { GameInstance.GetDynamicEntitySystem().DeleteEntity(this.trialID); }
        this.trialID = new EntityID();
        ArrayClear(this.visuals);
    }
    private func CopyPlacement(source: ref<entISkinTargetComponent>, target: ref<entISkinTargetComponent>) -> Void {
        let parent = source.parentTransform as entHardTransformBinding;
        let binding: ref<entHardTransformBinding>;
        let skinning: ref<entSkinningBinding>;
        target.name = source.name;
        target.localTransform = source.localTransform;
        target.renderSceneLayerMask = Cast<Uint8>(1u);
        target.boundingBoxLocalBoundsOverride = 2.0;
        if IsDefined(parent) {
            binding = new entHardTransformBinding();
            binding.bindName = parent.bindName;
            binding.slotName = parent.slotName;
            binding.enabled = parent.enabled;
            target.parentTransform = binding;
        }
        if IsDefined(source.skinning) {
            skinning = new entSkinningBinding();
            skinning.bindName = source.skinning.bindName;
            skinning.enabled = true;
            target.skinning = skinning;
        }
        NeonLinkAppearanceLog("FRESH " + NameToString(target.name) + " skin=" + (IsDefined(source.skinning) ? NameToString(source.skinning.bindName) : "none") + " parent=" + (IsDefined(parent) ? NameToString(parent.bindName) : "none"));
    }
    private func Hide(components: array<ref<IComponent>>) -> Void {
        let skin: ref<entSkinnedMeshComponent>;
        let morph: ref<entMorphTargetSkinnedMeshComponent>;
        let i: Int32 = 0;
        while i < ArraySize(components) {
            skin = components[i] as entSkinnedMeshComponent;
            morph = components[i] as entMorphTargetSkinnedMeshComponent;
            if IsDefined(skin) { skin.isEnabled = false; }
            if IsDefined(morph) { morph.isEnabled = false; }
            i += 1;
        }
    }
    private cb func OnExtract(event: ref<EntityBuilderEvent>) {
        let builder = event.GetEntityBuilder();
        let appearances: array<ref<EntityBuilderAppearanceWrapper>>;
        let i: Int32 = 0;
        if !EntityID.IsDefined(this.trialID) || builder.GetEntityID() != this.trialID || ArraySize(this.visuals) == 0 { return; }
        if !Equals(builder.GetEntityType(), n"NPCPuppet") { NeonLinkAppearanceLog("TRIAL wrong class; no writes"); return; }
        NeonLinkAppearanceLog("TRIAL extract template=" + ToString(builder.HasEntity()) + " appearance=" + ToString(builder.HasAppearance()));
        if builder.HasEntity() { this.Hide(builder.GetTemplate().GetComponents()); }
        if builder.HasAppearance() { this.Hide(builder.GetAppearance().GetComponents()); }
        appearances = builder.GetCustomAppearances();
        while i < ArraySize(appearances) { this.Hide(appearances[i].GetComponents()); i += 1; }
        i = 0;
        while i < ArraySize(this.visuals) {
            if builder.HasAppearance() { builder.GetAppearance().AddComponent(this.visuals[i]); }
            else { if builder.HasEntity() { builder.GetTemplate().AddComponent(this.visuals[i]); } }
            i += 1;
        }
        NeonLinkAppearanceLog("TRIAL inserted=" + IntToString(i));
    }
    private cb func OnEntityInitialize(event: ref<EntityLifecycleEvent>) {
        let entity = event.GetEntity();
        let npc: ref<NPCPuppet>;
        let components: array<ref<IComponent>>;
        let i: Int32 = 0;
        if !IsDefined(entity) || !EntityID.IsDefined(this.trialID) || entity.GetEntityID() != this.trialID { return; }
        npc = entity as NPCPuppet;
        if !IsDefined(npc) { return; }
        if IsDefined(npc.GetAIControllerComponent()) { npc.GetAIControllerComponent().Toggle(false); }
        npc.DisableKillReward(true); npc.SetDisableRagdoll(true, true);
        components = entity.GetComponents();
        NeonLinkAppearanceLog("TRIAL initialized components=" + IntToString(ArraySize(components)));
        while i < ArraySize(components) && i < 180 {
            NeonLinkAppearanceLog("TRIAL component=" + NameToString(components[i].name) + " class=" + NameToString(components[i].GetClassName()));
            i += 1;
        }
    }
    private func IsReplacement(component: ref<IComponent>) -> Bool {
        let i: Int32 = 0;
        while i < ArraySize(this.visuals) {
            if component == this.visuals[i] { return true; }
            i += 1;
        }
        return false;
    }
    private cb func OnAfterAttach(event: ref<EntityLifecycleEvent>) {
        let entity = event.GetEntity();
        let components: array<ref<IComponent>>;
        let component: ref<IComponent>;
        let i: Int32 = 0;
        let disabled: Int32 = 0;
        let kept: Int32 = 0;
        let stillEnabled: Int32 = 0;
        let replacementEnabled: Int32 = 0;
        if !IsDefined(entity) || !EntityID.IsDefined(this.trialID) || entity.GetEntityID() != this.trialID { return; }
        if !IsDefined(entity as NPCPuppet) || IsDefined(entity as PlayerPuppet) { return; }
        components = entity.GetComponents();
        while i < ArraySize(components) && i < 512 {
            component = components[i];
            if IsDefined(component) && (IsDefined(component as entSkinnedMeshComponent) || IsDefined(component as entMorphTargetSkinnedMeshComponent)) {
                if this.IsReplacement(component) {
                    kept += 1;
                    if component.IsEnabled() { replacementEnabled += 1; }
                } else {
                    component.Toggle(false);
                    disabled += 1;
                    if component.IsEnabled() { stillEnabled += 1; }
                }
            }
            i += 1;
        }
        NeonLinkAppearanceLog("TRIAL donors toggled=" + IntToString(disabled) + " donors still enabled=" + IntToString(stillEnabled) + " replacements=" + IntToString(kept) + " replacements enabled=" + IntToString(replacementEnabled));
    }
    public func Start() -> Void {
        let player = GetPlayer(GetGameInstance());
        let components: array<ref<IComponent>>;
        let sourceSkin: ref<entSkinnedMeshComponent>;
        let sourceMorph: ref<entMorphTargetSkinnedMeshComponent>;
        let skin: ref<entSkinnedMeshComponent>;
        let morph: ref<entMorphTargetSkinnedMeshComponent>;
        let path: String;
        let spec: ref<DynamicEntitySpec>;
        let i: Int32 = 0;
        if !IsDefined(player) || EntityID.IsDefined(this.trialID) { return; }
        components = player.GetComponents();
        while i < ArraySize(components) && i < 256 && ArraySize(this.visuals) < 96 {
            sourceSkin = components[i] as entSkinnedMeshComponent;
            sourceMorph = components[i] as entMorphTargetSkinnedMeshComponent;
            path = "";
            if IsDefined(sourceSkin) && sourceSkin.isEnabled { path = ResRef.ToString(ResourceAsyncRef.GetPath(sourceSkin.mesh)); }
            if IsDefined(sourceMorph) && sourceMorph.isEnabled { path = ResRef.ToString(ResourceAsyncRef.GetPath(sourceMorph.morphResource)); }
            if StrLen(path) > 0 && !StrContains(path, "shadow") && !StrContains(path, "\\fpp\\") {
                if IsDefined(sourceSkin) {
                    skin = new entSkinnedMeshComponent();
                    skin.mesh = sourceSkin.mesh; skin.meshAppearance = sourceSkin.meshAppearance;
                    skin.chunkMask = sourceSkin.chunkMask; skin.isEnabled = true;
                    this.CopyPlacement(sourceSkin, skin);
                    ArrayPush(this.visuals, skin);
                } else {
                    morph = new entMorphTargetSkinnedMeshComponent();
                    morph.morphResource = sourceMorph.morphResource; morph.meshAppearance = sourceMorph.meshAppearance;
                    morph.chunkMask = sourceMorph.chunkMask; morph.isEnabled = true;
                    this.CopyPlacement(sourceMorph, morph);
                    ArrayPush(this.visuals, morph);
                }
            }
            i += 1;
        }
        spec = new DynamicEntitySpec(); spec.recordID = t"Character.Judy";
        spec.position = player.GetWorldPosition() + player.GetWorldForward() * 4.0;
        spec.orientation = Quaternion.BuildFromDirectionVector(-player.GetWorldForward());
        spec.persistState = false; spec.persistSpawn = false; spec.alwaysSpawned = true;
        spec.spawnInView = true; spec.active = true; spec.tags = [n"Redline.AppearanceTrial"];
        this.trialID = GameInstance.GetDynamicEntitySystem().CreateEntity(spec);
        NeonLinkAppearanceLog("TRIAL requested fresh=" + IntToString(ArraySize(this.visuals)));
    }
}
