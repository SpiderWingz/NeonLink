// Read-only local capture experiment. Not wired into networking or a payload.
// Paths and names stay local until they can be mapped to verified selectors.
public struct RedlineLocalMeshSample {
    public let kind: Int32; // 1 skinned, 2 morph target, 3 garment.
    public let componentName: CName;
    public let path: ResRef;
    public let material: CName;
    public let chunks: Uint64;
    public let skinning: CName;
    public let parent: CName;
    public let slot: CName;
    public let localPosition: Vector4;
    public let localOrientation: Quaternion;
}

public class RedlineLocalAppearanceSnapshot extends IScriptable {
    public let meshes: array<RedlineLocalMeshSample>;
}

public abstract class RedlineLocalAppearanceCapture {
    // Never pass an entity obtained from a peer. No player or save writes.
    // Returns null for unsupported bindings, missing resources, or overflow.
    public static func Capture(player: ref<PlayerPuppet>) -> ref<RedlineLocalAppearanceSnapshot> {
        let result: ref<RedlineLocalAppearanceSnapshot>;
        let components: array<ref<IComponent>>;
        let source: ref<entISkinTargetComponent>;
        let skin: ref<entSkinnedMeshComponent>;
        let morph: ref<entMorphTargetSkinnedMeshComponent>;
        let parent: ref<entHardTransformBinding>;
        let path: ResRef;
        let pathText: String;
        let sample: RedlineLocalMeshSample;
        let i: Int32 = 0;
        if !IsDefined(player) { return null; }
        components = player.GetComponents();
        if ArraySize(components) > 512 { return null; }
        result = new RedlineLocalAppearanceSnapshot();
        while i < ArraySize(components) {
            skin = components[i] as entSkinnedMeshComponent;
            morph = components[i] as entMorphTargetSkinnedMeshComponent;
            source = null;
            if IsDefined(skin) && skin.isEnabled && skin.IsEnabled() { source = skin; }
            if IsDefined(morph) && morph.isEnabled && morph.IsEnabled() { source = morph; }
            if IsDefined(source) {
                sample.skinning = n""; sample.parent = n""; sample.slot = n"";
                if IsDefined(skin) {
                    sample.kind = IsDefined(skin as entGarmentSkinnedMeshComponent) ? 3 : 1;
                    path = ResourceAsyncRef.GetPath(skin.mesh);
                    sample.material = skin.meshAppearance; sample.chunks = skin.chunkMask;
                } else {
                    sample.kind = 2;
                    path = ResourceAsyncRef.GetPath(morph.morphResource);
                    sample.material = morph.meshAppearance; sample.chunks = morph.chunkMask;
                }
                pathText = ResRef.ToString(path);
                if StrLen(pathText) == 0 { return null; }
                if !StrContains(pathText, "shadow") && !StrContains(pathText, "\\fpp\\") {
                    if ArraySize(result.meshes) >= 96 { return null; }
                    parent = source.parentTransform as entHardTransformBinding;
                    if IsDefined(source.parentTransform) && !IsDefined(parent) { return null; }
                    sample.componentName = source.name; sample.path = path;
                    if IsDefined(source.skinning) { sample.skinning = source.skinning.bindName; }
                    if IsDefined(parent) { sample.parent = parent.bindName; sample.slot = parent.slotName; }
                    sample.localPosition = source.GetLocalPosition();
                    sample.localOrientation = source.GetLocalOrientation();
                    ArrayPush(result.meshes, sample);
                }
            }
            i += 1;
        }
        if ArraySize(result.meshes) == 0 { return null; }
        return result;
    }
}
