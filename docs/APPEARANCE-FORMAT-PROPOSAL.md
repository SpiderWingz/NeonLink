# Remote appearance descriptor: proposed next revision

This is a design proposal, not an implemented or negotiated protocol. The
existing experimental appearance frame remains disconnected from transport and
the game adapter. Do not interpret these proposed fields as permission to
construct engine objects from peer data.

## What is missing

The current descriptor holds resource and material selectors, binding names,
chunk masks and transforms, but cannot identify a component's local role or
distinguish a garment component from an ordinary skinned mesh. During the local
trial, multiple components also shared names. The installed Codeware 1.21.2
bindings define `entGarmentSkinnedMeshComponent` as a subclass of
`entSkinnedMeshComponent`, and expose morph targets as a separate component.
Substituting an ordinary mesh for a garment may lose garment-specific behavior.
Face morph weights, body-specific rigs and some binding state are still not
captured or verified; adding fields alone will not solve those problems.

## Candidate fixed-width layout

Negotiate a new capability and a new message kind or version before accepting
any revised frame. Never infer support from a client version string. Retain a
separate legacy movement path for 0.8.1 peers; the current hello parser expects
an exact capability value, so compatibility requires an explicit design and
tests, not a silent change. Reject unknown versions and unsupported kinds.

As a starting point for review, a bounded big-endian payload could use:

* Six 32-bit header fields: schema version, world generation, revision, body
  variant, mesh count and morph count. Counts must be checked before allocation.
* Up to 96 fixed-width 92-byte mesh entries: 32-bit component kind, 32-bit
  role, 64-bit local component selector, the six existing 64-bit resource,
  material, skinning, parent, slot and chunk values, and seven existing
  signed 32-bit quantized transform values.
* Up to 128 existing 20-byte morph entries. The current -1 to 1 range is only a
  conservative placeholder: Codeware examples include a 5.0 morph value.
  Establish actual safe engine bounds and a reliable capture source before
  changing it; never silently clamp and claim faithful reproduction.

With the existing 16-byte envelope this candidate's maximum plaintext frame is
11,432 bytes. The current runtime allows at most 512 ciphertext bytes including
secretstream authentication overhead and has fixed-size buffers. A new
negotiated, bounded transport design must account for encryption overhead,
concurrent peers, rate limits and allocation before raising those limits.
Do not allocate according to an unchecked peer length or enlarge limits for
legacy messages merely to accommodate this proposed frame.

## Local authority and naming

The role and component selector are opaque peer values until a trusted local
catalog matches the **entire** tuple: body, resource path/hash, resolved engine
resource class, actual locally supported component subtype, allowed role,
appearance/material, binding and slot, transform and chunk mask. The catalog
must come from independently verified local metadata, not from the sender.
Unknown, ambiguous, unavailable or mismatched entries reject the whole update
and keep a safe existing proxy/fallback visible. Current policy intentionally
accepts only ordinary skinned and morph components; it must continue to reject
garments until the resolver proves the subtype and relevant local metadata.

The 64-bit selector must not be cast directly to an arbitrary engine name or
used as an entity ID. Generate any proxy component names locally from verified
metadata, preserving engine-required name prefixes and making names unique
within that one proxy. Verify whether appearance-controller references require
specific original names before choosing a naming scheme. Bind only to the
proxy's locally validated rig and slots. Binding enable masks and visual tags
should come from trusted local metadata; do not serialize raw engine objects or
peer-provided tag lists. Extend the wire only if verified local data proves it
necessary and bounded.

## Applying and testing

Capture is not complete until clothing/cyberware changes refresh the descriptor;
Codeware documents `Entity/Reassemble` for dynamic component changes. On the
receiving game thread, resolve and load all locally approved resources first,
recheck session generation and the specific locally owned proxy ID, then perform
the visual swap with a fallback if setup fails. Avoid mutating local V, saves,
inventory, progression, or the global customization system.

Before enabling the feature, test malformed/oversized frames, unsupported
capabilities and kinds, local catalog collisions, mismatched role/subtype/rig,
replayed revisions, resource load failure and missing-mod fallback, concurrent
disconnect/generation changes, duplicate names, resource cleanup, and repeated
equipment changes. Follow with controlled game testing and two real clients.
One real client and a simulated guest establish only the simulated case.

References: [Codeware lifecycle and component APIs](https://github.com/psiberx/cp2077-codeware/wiki/),
[CDPR modding documentation on components and bindings](https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/blob/main/for-mod-creators-theory/core-mods-explained/archivexl/archivexl-character-creator-additions/ccxl-hairs.md).
