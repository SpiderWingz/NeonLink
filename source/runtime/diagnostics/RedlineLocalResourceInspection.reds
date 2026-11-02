// Development-only Codeware adapter. No network or game integration exists.
// The caller must obtain the path and expected hash from trusted LOCAL metadata;
// never pass in a peer-provided path. Path/selector policy and owner checks are
// separate requirements; matching a resource here does not approve an avatar.
public abstract class RedlineLocalResourceInspection {
    public static func Request(localPath: ResRef) -> ref<ResourceToken> {
        let depot = GameInstance.GetResourceDepot();
        if !IsDefined(depot) || !depot.ResourceExists(localPath) { return null; }
        return depot.LoadResource(localPath);
    }

    // -1: failed or wrong type/hash; 0: still loading; 1: loaded and matched.
    // The caller must retain the token and run this on the game thread.
    public static func Inspect(token: ref<ResourceToken>, expectedHash: Uint64, kind: Int32) -> Int32 {
        let resource: ref<CResource>;
        if !IsDefined(token) || expectedHash == Cast<Uint64>(0u) || (kind != 1 && kind != 2) { return -1; }
        if token.GetHash() != expectedHash || token.IsFailed() { return -1; }
        if !token.IsFinished() { return 0; }
        if !token.IsLoaded() { return -1; }
        resource = token.GetResource();
        if !IsDefined(resource) { return -1; }
        if kind == 1 && IsDefined(resource as CMesh) { return 1; }
        if kind == 2 && IsDefined(resource as MorphTargetMesh) { return 1; }
        return -1;
    }
}
