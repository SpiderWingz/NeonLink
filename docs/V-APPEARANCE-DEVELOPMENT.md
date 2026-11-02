# Remote V development checkpoint — 2026-11-02

## Later rendering trials and unexpected restart

In an isolated local trial, the proxy contained both Judy's original meshes
and 28 fresh V-derived components. The user observed a visibly mixed Judy/V
character. The captured log confirms the component overlap. Merely changing
`isEnabled` during extraction did not visibly remove the original appearance.

`source/runtime/diagnostics/RedlineAppearanceTrial.reds` is an **unshipped,
local-only experiment**. After attachment it checks the locally owned proxy ID
and NPC type, keeps its replacement components by identity, and calls
`IComponent.Toggle(false)` on the remaining skinned and morph components. It
leaves animation/rig components in place. The script compiled against the 2.5
game bundle and Codeware 1.21.2, but its visual result has not been verified.
It does not connect to transport or accept peer data. Duplicate component names,
gender-specific rigs and exact face morphs remain unresolved.

The next game run crashed while loading. The game report says GPU crash for
unknown reasons; Windows then recorded an unexpected system restart. These
records do not establish whether this trial caused either event. Stop live tests
until the failure is investigated; do not claim that the visibility fix works.
Afterwards the game was closed, the installed 0.8.1 DLL and main script were
restored and hash-verified, and the experimental script removed. ManualSave-12
was backed up and hash-verified before loading; earlier backups remain.

The published 0.8.1 preview still renders a Judy movement proxy. Remote V
appearance is not enabled in the transport or player package. No new release
has been published from this work.

## Implemented and tested

`source/runtime/Appearance.hpp` defines a value-only appearance descriptor with
at most 96 skinned/morph meshes and 128 morph weights. The maximum frame is
10,276 bytes. It carries resource/name hashes and bounded integer transforms;
it contains no entity IDs, engine pointers, paths, serialized engine objects,
inventory, progression, or customization-system setters.

Decode checks counts and exact lengths before allocations. It rejects unknown
mesh kinds, invalid quaternions, oversized local translations, duplicate mesh or
morph entries, and invalid generations/revisions. Its mailbox clears on
disconnect and samples only the requested character generation. The existing
transport does not send or accept these appearance frames yet. Enabling them
will require a new negotiated capability/version and a bounded frame-size change.

46 appearance checks passed, including truncation, malformed fields, maximum
size, 10,000 randomized inputs, and mailbox lifecycle cases. The full native
development runner also passed the existing 52 protocol/profile, 30 movement
integration, 21 transport and 38 direct-host checks: 187 checks in this run.
The ordinary gameplay script compiled against the installed game bundle and
Codeware 1.21.2. The launcher was not rebuilt.

## Actual game observations

Read-only inspection in Cyberpunk 2077 2.5 exposed the local V's body, head,
eyes, eyebrows, eyelashes, beard, hair, clothing and holstered Mantis Blade
render components and their mesh/material selectors. The customization menu's
`GetUnitedOptions` returned an empty array during gameplay. The SDK morph
manager's weight-entry array was also empty at the capture point; selected face
morphs have not been recovered or replicated.

A locally owned, nonpersistent Judy entity spawned. Adding cloned V components
during the extraction callback did not produce a visually verified V. Its
appearance remained Judy in the observed scene. This is an engine-adapter
failure, not a successful remote-player test. No appearance exchange between two
clients has been tested.

`Entity/Extract` uses `EntityBuilderEvent`; `Entity/Assemble` uses
`EntityLifecycleEvent`. Dynamic tag filtering is insufficient to establish
ownership during early entity construction. Every future write must check the
explicit locally created entity ID first, independently of callback filters.
Never operate on the local player or change global customization state.

An initial development-only native function returning an unregistered struct
array failed RED4ext's script validation. The open error dialog was followed by
a 120-second engine watchdog report. The test was replaced with primitive
return types and passed subsequent startup validation. The unsuccessful morph
reader was subsequently removed: it depended on a locally adjusted SDK header
and is not a reproducible capture solution. The optional `-AppearanceDiagnostics`
build flag now exposes only the bounded local logging helper; ordinary builds
omit it. Old experimental probe scripts requiring the removed natives must not
be installed. Diagnostic probe scripts are not player payloads.

The test game is closed. The installed DLL and main script were restored from
the unchanged 0.8.1 payload, and the extra development probe script was removed.
Existing save backups were retained, and the newer open-area AutoSave-11 was
also copied and hash-verified before it was used for testing. No deliberate save
or quest progression was performed; an ordinary game autosave occurred during
an earlier idle session.

## User-directed reuse investigation

The user asked to reuse ChoomLink resources. There are two distinct projects:

* [The released ChoomLink mod by visualduck](https://www.nexusmods.com/cyberpunk2077/mods/35338)
  links to [hatfieldnoah-source/choomlink-releases](https://github.com/hatfieldnoah-source/choomlink-releases).
  At this checkpoint, its Nexus permissions require author permission for
  modification and asset reuse and prohibit repacking. The releases repository
  contains distribution metadata rather than the implementation source. No
  ChoomLink release code, binaries, assets or credentials have been imported.
* [FOX-cOSMIC/choomlink-core](https://github.com/FOX-cOSMIC/choomlink-core)
  is a separate MIT-licensed fork of [TDUniverse/Cyberverse](https://github.com/TDUniverse/Cyberverse).
  Inspected commit: `dae3815735a88ca0ed6a00126a42baa8c438a5dc`.
  It has a reproducible stripped-NPC template generator and puppet equipment
  routines, but its inspected spawn path also uses a Judy-derived record. It
  does not provide a drop-in, verified solution for reproducing the remote V.
  Retain its MIT notice if adapting its code. Do not redistribute extracted game
  entity/animation assets from that repository.

ChoomLink's public requirements include Codeware, ArchiveXL and TweakXL.
[ArchiveXL](https://github.com/psiberx/cp2077-archive-xl) supports custom resources
and component overrides; [TweakXL](https://github.com/psiberx/cp2077-tweak-xl)
supports custom records and hot reload. Both advertise Cyberpunk 2077 2.5
support and MIT licenses. These are candidates for reuse without replacing
Redline's runtime with the separately distributed ChoomLink mod. No third-party
implementation has been imported or integrated in this checkpoint.

ArchiveXL was inspected at `8210dae6577842eee3c39cd7dc73a89f0b62b75f`.
Its `scripts/DynamicAppearance.reds` exposes `ConvertAppearanceNameToTPP`:
ordinary names replace `&FPP` with `&TPP`; dynamic names instead override the
`camera=tpp` condition while retaining other conditions and the suffix hash.
The future capture adapter should call this framework API for clothing rather
than inventing another appearance-name parser. Its native appearance changer
also targets a specific puppet, but contains internal ABI/address dependencies;
do not copy those offsets into Redline or treat them as validated by this review.

PhotoMode-EX was inspected at `384d0a250853957eb5bfaae27ee173566972332e`.
Its spawn service hooks the photo-mode system and keeps photo-mode character
state; the inspected path is not a drop-in unpaused remote-player spawner. Its
NPC-limit patch modifies engine instructions and does not help capture V's face.
Do not add this dependency merely for a feature Redline does not use.

CyberpunkMP's core has a competing-product restriction, so it is not a source
to adopt for Redline. PhotoMode-EX is MIT-licensed and remains another candidate
for entity-specific character/appearance APIs. `npv-build` documents a local
save-to-NPC pipeline, but no license was found at its repository root; do not
copy its implementation without establishing reuse permission.

The next implementation step is to choose and verify a reusable, per-entity
appearance path. A useful rendering trial would construct fresh mesh components
and new local bindings from values, preserving required component-name prefixes
and normal scene visibility, rather than cloning attached runtime components.
The existing schema still needs local resource/type/path validation before any
incoming selector can be applied. Keep this off the shipped package until that
adapter passes a real game test.
