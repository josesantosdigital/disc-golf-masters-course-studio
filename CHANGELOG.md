# Changelog

## 0.5.102 development
- Add optional `layoutGroundColliderExclusions` name fragments for automatic imported-map hole sampling. The 711 DGC course now excludes its giant decorative grass `Terrain` collider, which dominated the previous layout candidates and placed holes outside the city. The option is available in Course Studio map settings and only changes auto-layout sampling; excluded surfaces remain collidable during play.

## 0.5.101 development
- Add a Windows GitHub Actions workflow for the no-game offline contributor checks, plus a single documented entry point that prefers the project-local .NET SDK and isolates temporary .NET/NuGet state.
- Clarify the proposed public source and plugin/tool ZIP channels, the separate author-built course ZIP workflow, and the post-gate publication sequence. This identifies what a qualified reviewer should assess without claiming approval or redistribution rights for imported map assets.
- Remove `SoloConfiguration.GetCourses` entirely. The 0.5.100 test still reached the same fatal IL2CPP `AccessViolationException` in that method even though the postfix only queued work. Discover the selector through `CourseSelectionViewController.Awake` alongside the existing view setup/card hooks, so course selection no longer patches the crashing method. This requires a manual check that custom cards appear and that a hole can finish without a crash.
- Refuse Map Explorer deletion when a managed plugin/course/map/editor root is a symbolic link or non-directory, including a second root check before removing files.

## 0.5.100 development
- Restore custom-course selector discovery after 0.5.99 stopped the list-attachment path. The `SoloConfiguration.GetCourses` Harmony postfix now only queues a flag; `CourseLoadingDriver.Update` performs selector lookup and course-entry registration on the next frame, outside the IL2CPP method callback. This preserves the selector trigger while avoiding the immediate Unity lookup and view mutation that ran inside `GetCourses`. Round-completion safety still needs manual confirmation.

## 0.5.99 development
- Remove the `SoloConfiguration.GetCourses` Harmony hook after a completed custom round crashed in the IL2CPP wrapper for that method. The crash log places the fault at the game's course-list call after the round-complete callback; the tee/basket ground snap completed successfully. Custom entries continue to register through the course-selection view hooks. This is a targeted mitigation, not yet confirmed as the crash cause; the next manual round should confirm both round completion and that custom entries still appear.

## 0.5.98 development
- Ground-snap explicit imported tee and basket positions when their height matches the map's default ground Y. This fixes Course Studio-created markers being placed at Y=0 inside raised terrain while preserving manually entered elevations; water, named building surfaces, triggers, and steep supports are filtered, and failed snaps are logged.

## 0.5.97 development
- Avoid constructing the game's invalid IL2CPP `LightmapData` array wrapper while repairing imported maps. The wrapper exception previously aborted the entire material-repair pass on Tree Land, leaving its source shaders and foliage broken; imported materials now continue through the configured repair path. Active lightmap count remains unavailable for diagnostics, while maps that clear lightmaps still clear every imported renderer index.

## 0.5.96 development
- Port the recognized source `Universal Render Pipeline/Lit (PBR Workflow)` material family to DGM's loaded URP Lit shader even when the old shader reports device support. Southside still shows glare with all 175 local lights at zero and 185 base lights disabled; 1,484 materials use that old compiled PBR shader. Pipeline incompatibility is the lighting candidate, not yet a confirmed visual diagnosis.
- Preserve common PBR textures, UV transforms, tint, emission, cutouts, culling, depth/blending and render queues. Remap compatible keywords; omit the source-specific packed detail layer. Leave water/custom shader families and explicit unlit/fallback rendering modes alone. Restore original materials during cleanup; log bounded conversion examples. No course JSON or asset bundle changes.
- Add offline material-transfer/scope/rollback checks. User confirmed the 0.5.95 city-top snap fix; retain its shared terrain-source isolation unchanged. Gameplay lighting verification remains with the user.

## 0.5.95 development
- Isolate hidden base-course `SurfaceMapper` components through their normal disable/restore lifecycle. Native analysis showed that `SurfaceMapper.Under` selects registered heightfields by X/Z even when the corresponding Terrain and TerrainCollider are disabled; `DiscGroundContact` then runs its own solver and post-physics push-out. The latest Southside trace retained a native contact at Y=29.99 after the .94 endpoint correction moved the disc to Y=4.6.
- Detach hidden cached `DiscBehaviour.terrain` references after Throw and before FixedUpdate. Supply actual imported mesh ground distance/grounded fields when no valid Terrain remains, covering direct native field accesses and the inlined GroundAction that getter patches missed. Keep valid imported native terrains/models. Restore cached references and mapper enabled states at cleanup.
- Remove the speculative upward position clamps, grounded getter filters, and endpoint relocations. Jump diagnostics no longer change flight or throw completion. Select the nearest physical imported support, including roofs/bridges, instead of preferring a street underneath by layer or rejecting support by object name/size.
- Prevent completed-throw traces from restarting every physics tick; include mapper and cached terrain source names in contact diagnostics. Add 18 offline ownership/restoration checks against component doubles. Compiled and checked offline; gameplay verification remains with the user.

## 0.5.94 development
- Replace the 0.5.93 throw-completion suppression, which left Southside Hole 2 looping at `OnThrowComplete` for 50+ seconds, with a grounded endpoint correction. When an unsupported completion has a sampled imported ground surface 3–60 m below and no water surface at that XZ, move the disc and `Throw.endPosition` to that ground and let DGM's original completion callback proceed. If no safe ground sample is available, leave native completion untouched; never hold the player in a deferred throw.
- Keep the 3 m imported-support probe and Southside's 0.0 point/spot-light multiplier from 0.5.93.

## 0.5.93 development
- Intercept `PlayerBehaviour.OnThrowComplete` when the disc is still inside the imported play area but DGM reports completion more than 3 m above imported ground with no imported support nearby. Clear the false completion flags and resume downward motion so the player is not moved to a hidden city-height contact point. Successful basket throws and throws outside the imported play bounds are not intercepted.
- Expand the imported support probe from 1.5 m to 3 m to avoid rejecting legitimate near-ground contacts such as Southside's 2.01 m sampled distance.
- Set Southside's point/spot light multiplier to 0.0 after its 175 spotlights at 5% power still produced the reported bright spot. Its generated directional key light remains enabled; verify visual brightness manually.

## 0.5.92 development
- Lower the unsupported upward-snap minimum from 10 m to 6 m. Southside's 0.5.91 trace showed the first 24 m snap being clamped, followed by a second 9.99 m snap to Y≈30 m that narrowly bypassed the old 10 m threshold.
- Treat DGM's `IsGrounded` and `DiscGroundContact.OnGround` as unsupported when imported-map ground sampling finds the real surface more than 2 m below and no imported support collider within 1.5 m. This targets Southside's repeated Y≈30 m false-ground state; user verification is pending.
- Apply imported point/spot light intensity scaling before renderer/material repair, so a later IL2CPP failure cannot skip it. With `preserveLightmaps=true`, leave lightmap indices untouched and skip querying Unity's `LightmapData` array.
- Set Southside's imported local-light multiplier to 0.05 after the previous 0.5 setting had no visible effect; validate lighting manually.

## 0.5.91 development
- Isolate renderer lightmap-index read failures so they do not abort the remaining imported material, particle, and light-repair pass. Southside's renderer scan previously stopped before producing its light summary.
- Capture imported-disc collision enter/stay callbacks through the short completion transition, and log DGM's ground-contact point, normal, and impact speed at throw completion. The 0.5.90 Southside trace still ended with `grounded=true` 8–9 m above sampled imported ground and `collided=true`, while no collider contact was logged; the exact city surface remains unconfirmed.
- Lower Southside's imported local-light intensity multiplier from 1.0 to 0.5 to reduce the reported bright spot. Check map brightness with the user.

## 0.5.90 development
- Clamp impossible upward snaps to the vertical travel supported by the physics step while preserving the disc's horizontal movement and collision-response velocity. A destination collider no longer exempts a physically impossible jump, since an oversized city collider may itself be the false surface. Based on the 0.5.89 Southside Hole 2 trace; user gameplay verification is pending.

## 0.5.89 development
- Preserve the active throw trace's previous position when processing routine `FixedUpdate` pre-samples, so motion between callbacks remains visible to the teleport guard. Continue to force the exact pre-collision sample around `OnCollisionEnter`, where DGM may move the disc inside the handler.

## 0.5.88 development
- Also capture the disc immediately before DGM's `DiscCollision.OnCollisionEnter` handler and inspect its position immediately afterward. The snap traces show collisions preceding the jump, so the teleport guard now covers both in-step movement and collision-handler movement before the post-collision diagnostic can initialize a trace at the snapped position.

## 0.5.87 development
- Capture each disc's position and velocity before DGM's `FixedUpdate`, then seed imported-throw diagnostics from that sample. This lets the upward-teleport guard see a snap during the first physics step, even if DGM or a collision callback would otherwise initialize the throw trace after the disc had already moved.

## 0.5.86 development
- Let DGM's original ground-distance getter run before applying the imported-map override, and include both the original and imported distances in the bounded first-sample diagnostic. This keeps the underlying getter behavior intact while making the Y≈30 m reference correction observable in the user's next manual check.

## 0.5.85 development
- Override DGM's `DiscBehaviour.GroundDistance` during imported rounds using downward ground hits from the active imported scene. Prefer colliders already classified as DGM terrain; exclude the hidden base-course scene, water, disc colliders, named roofs/buildings/sky, and steep or oversized surfaces. Return an unsupported sentinel over areas without imported ground so the hidden Canon Park ground cannot become the reference plane. This targets the repeatable Y≈30 m city snap; keep the velocity-based upward-teleport correction as a fallback. No in-game verification yet.

## 0.5.84 development
- Detect impossible upward disc teleports during imported-course throws. If a 10 m or larger rise exceeds the sampled velocity/gravity travel and has no supporting surface within 4 m, restore the disc to its previous flight position while keeping the collision-response velocity. This is scoped to active imported courses and is intended to prevent the observed city-top snap; corrections are logged for manual verification.

## 0.5.83 development
- Isolate failures reading Unity's active `LightmapData` array so a broken IL2CPP array wrapper no longer aborts the entire imported render/material pass. If the active lightmap count cannot be read, preserve nonnegative source indices and continue repairing renderers, shaders, and lights.
- Before DGM handles an imported-map collision, classify upward-facing imported contacts as terrain using the existing reversible layer mapping. This covers ground surfaces that were not sampled during layout planning and keeps wall-side contacts as obstacles.
- Extend bounded collision diagnostics to include the contacted scene and PhysicMaterial so city snaps and metallic impact sounds can be traced in the user's next manual session.

## 0.5.82 development
- Treat every declared map dependency bundle as required. If a bundle is missing or Unity rejects it, abort map setup and release any bundles already loaded, using the existing setup-failure path instead of continuing with a partial import.

## 0.5.81 development
- Keep the 711-only camera occlusion bypass applied for the full imported round. Disable culling on active and inactive cameras during setup, then rescan every 0.5 seconds so cameras activated or created later cannot re-enable the map's mismatched baked visibility data. Snapshot each camera before changing it and restore its original setting during cleanup.

## 0.5.80 development
- Require a complete DGM hole list and usable tee/basket objects before reporting an imported course as loaded. If a hole entry or grounded grid placement is missing, fail setup so the 0.5.79 rollback restores the base course instead of leaving a partial layout or placing objects at the scene-bound fallback height.

## 0.5.79 development
- If imported-map setup returns false or throws after changing scene state, restore the base-course snapshots and unload the imported scene and bundles. This prevents failed placement or rendering setup from leaving the round partially hidden or mutated.

## 0.5.78 development
- While an imported course is active, disable enabled lights from the hidden base course except the selected base sun when that course explicitly opts to use it. Snapshot and restore the original light states at round cleanup. Log bounded examples and counts to confirm which base-scene lights were isolated; imported-map lights are unchanged.

## 0.5.77 development
- During imported rounds, log unusually large disc position changes between physics updates, then capture nearby colliders from every loaded scene. This is bounded to eight events per throw and diagnostic only; it does not alter the disc or round state. It targets the unresolved 711 DGC wall-contact trace that ended 15 m higher without a nearby imported collider.

## 0.5.76 development
- Add an opt-in `mapLighting.disableCameraOcclusionCulling` setting that disables occlusion culling on active cameras during an imported round and restores their prior settings during cleanup. Enable it only for 711 DGC as a reversible visibility-culling candidate; other courses retain their existing camera settings. Expose the option in Course Studio's Advanced lighting controls.

## 0.5.75 development
- Log the active scene and a bounded sample/count of active cameras' `useOcclusionCulling` state after the imported map becomes active. This helps diagnose additive scenes with their own baked occlusion data; it does not toggle camera or renderer behavior.

## 0.5.74 development
- Add bounded render-scan counts and transform/intensity/range examples for active imported point and spot lights, including when no intensity multiplier is applied. This distinguishes hidden volumetric renderers from their still-active Light components; light state is unchanged when the multiplier is 1.0.

## 0.5.73 development
- For imported materials with no discovered color texture, log up to eight renderer transform paths when the same material is shared by multiple renderers. Cap the number of material-usage entries to keep diagnostics bounded; no texture selection, rendering, or gameplay behavior changes.

## 0.5.72 development
- Include long, explicitly named narrow water surfaces in hazard sampling; this covers Teeny Tiny Isle's 2 m by 31 m `Water` mesh that the prior 4 m minimum rejected. Exclude decorative water decals and fixture names from water-surface discovery, and avoid reading a shared static-batch MeshFilter as a single water footprint. Static-batched explicit water names use their renderer bounds as a fallback. Log a bounded sample of small water candidates rejected by the footprint filter. Water heights, landing tolerance, and normal disc collision remain unchanged.

## 0.5.71 development
- When imported materials have no selected color texture, log non-null texture properties considered by the fallback scan, including the generic `DummyMS_00` placeholder when it is ignored. This adds source-material evidence for the remaining Milsim Forest texture gaps without changing material selection or appearance.

## 0.5.70 development
- When a map preserves baked lightmaps, clear only renderer indices outside the active lightmap array and restore those indices on cleanup; keep valid source lightmaps unchanged. This targets 20 invalid `65534` indices found in 711's 14-entry lightmap table.
- Treat the known 4×4 `DummyMS_00` texture as a placeholder and exclude it from color-texture candidates. Offline slot parsing shows it is assigned as `_MetallicGlossMap` in the Milsim scene; matching-material alias recovery uses a peer's color slot, and the placeholder filter does not by itself unlock that recovery. Materials without a matching source texture remain unresolved for manual review.

## 0.5.68 development
- Skip the renderer-local-bounds repair on static-batched meshes. Their MeshFilter can point at the full shared batch mesh, whose bounds are not per-renderer local bounds; using those bounds can distort culling. Keep Unity's per-renderer bounds and report bounded examples in the render scan.

## 0.5.67 development
- Keep authored collision on static-batched renderers instead of replacing it with the shared batch MeshFilter mesh. A static batch can contain thousands of unrelated submeshes; using it as one object's collider can create invisible walls across the course. Log bounded examples for later manual review.

## 0.5.66 development
- Resolve collider subtypes through `GetComponent<T>` plus matching instance IDs instead of relying on runtime proxy casts. This lets imported water conversion, shallow-water mesh clipping, collider snapshot restoration, and visible-mesh alignment reach the actual BoxCollider/MeshCollider when Unity exposes it through a base Collider wrapper.
- Add collider instance IDs, actual MeshCollider identity and local bounds, mesh triangle indices from ground-ray snapshots, and hierarchy paths to bounded imported-disc collision and ground snapshots. This supports the 711 city launch/landing investigation and Teeny Isle wall-impact stalls.
- Include the local water surface height and disc-to-water delta in final imported-ground snapshots. Rechecking the serialized Isle transforms found two water renderers: a broad `Water Base` plane at about Y=-0.195 m and a narrow `Water` mesh at about Y=-3.5 m; terrain spans about Y=-3.62..2.73 m. The earlier single-plane estimate of -8.83 m was incorrect. Keep the 0.25 m landing allowance until a user-controlled trace identifies an actual shore mismatch.

## 0.5.65 development
- Evaluate imported water hazards and shallow-ground removal at the local height of the water mesh triangle under each point, rather than using the renderer's highest vertical bound for the entire water surface. This improves shoreline and uneven-surface classification. Keep the existing bounds fallback when no footprint triangles can be read; runtime behavior still needs the user's check.

## 0.5.64 development
- During imported-scene rendering repair, expand a static MeshRenderer's local culling bounds to include its assigned MeshFilter mesh when the existing bounds omit part of that mesh. Log before/mesh/expanded bounds for up to eight renderers, then restore the original bounds when the imported scene is cleaned up. This addresses a 711 DGC combined-city-mesh culling lead without changing colliders or disc physics; runtime behavior still needs the user's check.

## 0.5.63 development
- On imported-scene load, record a bounded one-time inventory of TextMeshPro components with a missing font asset, shared material, or atlas. This targets the thousands of repeated TextMeshPro null-reference errors in the last 711 session; it does not disable text or alter gameplay.

## 0.5.62 development
- For throws that remain unresolved past the existing progress interval, capture up to four spaced nearby-collider and downward-ground snapshots whether the disc is airborne or already marked grounded. This is bounded diagnostic logging only; it does not change disc physics, collision, water hazards, or round progression.

## 0.5.61 development
- Report imported renderers whose baked or realtime lightmap indices exceed the active lightmap-data array, with up to ten renderer paths. This helps diagnose imported-scene flicker when source lightmaps are preserved; it does not alter rendering settings.

## 0.5.60 development
- Keep a bounded sequence of imported-disc contacts per throw, including repeat contacts after the disc has moved or changed velocity. Capture nearby colliders and downward ground hits from every loaded scene at changed resting positions and throw completion, so a wall impact followed by a height change can be tied to the actual surface, including a hidden base-course collider. Diagnostics only; disc physics and throw completion remain unchanged.

## 0.5.58 development
- Log imported scene content bounds before applying optional configured gameplay bounds. This exposes scale/extent mismatches such as 711 DGC's small overview relative to its configured bounds without changing placement, out-of-bounds, or collision behavior.

## 0.5.57 development
- Scope imported-map collision diagnostics to each throw instead of suppressing a collider after its first contact for the whole round. Reset each trace's throw origin and contact set when a new throw begins, including when the first observed event is a collision. This improves repeat wall-impact diagnosis without changing physics, scoring, or throw completion.

## 0.5.56 development
- Use each imported hole's configured `lengthMeters` as that hole's target in the long-layout planner instead of applying the same hard-coded profile to every map. Snapshot those targets when the course definition loads, so the actual distance measured after placement cannot change the target profile on a later round. Set distinct target profiles for 711 DGC, Milsim Forest, and Southside; keep Teeny Tiny Isle's water-crossing profile. The planner still scales targets to available ground and recalculates actual distance/par from the placed tee and basket.

## 0.5.55 development
- Enable the existing imported-particle hiding option for Southside, where the in-game screenshot showed oversized white light cones. Log the first 24 hidden renderer names and particle shaders so the next load can confirm whether these are the cones; the renderer state is restored when the map unloads.

## 0.5.54 development
- Extend imported-throw snapshots with the game's thrown flag, flight/stationary timers, reset height, current OB area, and current throw completion/collision/penalty fields. This adds evidence for lost-disc diagnosis without changing collision, scoring, or throw completion.

## 0.5.53 development
- Continue tracing imported throws after the game clears `IsThrown`, and log the player's `OnThrowComplete` callback with course, hole, and throw count. This distinguishes a disc that never settles from a round callback that fails after settlement; no recovery or normal throw behavior is changed.

## 0.5.52 development
- Replace unsupported concave mesh triggers on imported water with sampled water-surface checks and hazard markers; disable source concave water colliders while preserving snapshot restoration.
- Skip visual startup for generated imported-map OB polygons, which have no game-authored line/flag objects and were throwing `OBArea.Start` null-reference errors.
- Keep existing ground collision when the visible mesh is unreadable, rather than assigning an unusable mesh to a collision proxy. Log imported throw start, 10-second active-flight snapshots, and completion to diagnose throws that never settle; this adds diagnostics only.

## 0.5.49 development
- Include the renderer hierarchy path and bounds when imported-material diagnostics cannot discover a color texture. This is limited to courses with `ignoreSourceTint` diagnostics enabled and is intended to locate the remaining Milsim Forest surfaces that render incorrectly; it does not change material rendering.

## 0.5.48 development
- Set the imported round HUD's par text from the active DGM hole after `SetupUI`; the game refreshes distance from the moved tee and basket but retains the base layout's cached par text.

## 0.5.47 development
- Move the imported-hole HUD refresh into the imported-map placement path. The 0.5.46 refresh had landed in the ordinary custom-layout path, so it did not update imported-map rounds. This refreshed measured distance; in-game review then found the par label still used the base layout value.

## 0.5.46 development
- Add the imported-hole HUD refresh, but it was initially attached to the ordinary custom-layout path. The imported-map path correction is in 0.5.47.

## 0.5.42 development
- For imported materials with no color texture, reuse a texture only when another scene material has the same name after removing a trailing duplicate suffix such as ` 1` or `.001`. Ignore ambiguous matches and log every reuse. This recovers source textures for duplicate plaster, ceiling, and roof materials without changing materials that already resolve their own texture.

## 0.5.41 development
- When imported-map ground sampling finds fewer than six points on the coarse grid, retry with up to a five-metre grid before planning. This targets small islands and narrow land strips that the original spacing skipped.
- Allow a sparse map with at least two grounded positions to reuse its short pair across holes rather than skipping all tee/basket placement and leaving the player at the base-game spawn outside the imported play boundary. The log warns when this limited fallback is used.

## 0.5.40 development
- Reduce the imported-water ground cutoff to 0.25 m and log it separately from the grounded-disc landing allowance. On Teeny Tiny Isle, the previous 4 m cutoff rejected the island's own terrain as submerged, pushing generated holes onto distant mountain meshes.

## 0.5.39 development
- Allow imported courses to configure a grounded-disc water landing height allowance. Set Teeny Tiny Isle to 8 m to catch shallow shoreline ground that sits above its water plane, without raising visible water or changing other courses.

## 0.5.38 development
- Log a focused audit of the imported shoreline collider type and triangle heights to determine why the waterline collision cut did not apply on Teeny Tiny Isle.

## 0.5.37 development
- Remove low ground collision triangles that overlap imported water within 4 m above the waterline, while keeping the visible terrain mesh unchanged. Re-run ground placement after collision updates.

## 0.5.36 development
- Added a focused runtime scan for solid imported colliders near the mapped waterline so we can identify the ground mesh that is preventing water OB behavior.

## 0.5.35 development

- Track imported water by its actual mesh footprint instead of its rectangular renderer bounds. Keep auto-generated tees and baskets off the water surface, and mark a disc as a water hazard if it settles on a shallow collider above water after being thrown. This closes the observed Teeny Tiny Isle case where the rendered water overlapped a playable ground collision surface; water behavior still requires in-game confirmation.

## 0.5.34 development

- Add Discord-friendly course-sharing guidance that distinguishes original-course layouts from imported map bundles and points back to the retained BONELAB source references.
- Add `.gitattributes` for LF source text and binary asset handling, package it with the source, and check both rules in the disposable Git preflight. An exact source-tree staging audit confirms that maps and local/build outputs remain excluded.
- Consolidate clean-install, course-authoring, selector, and four-map gameplay checks into one coordinated acceptance pass, with a result matrix so the final candidate can be reviewed and recorded in a single session.
- Reconcile the supported-environment page with the exact 0.5.34 offline preflight evidence and record that the four installed target-map packages validate structurally while their bounds and playability still need review.
- Split imported-map rendering methods into `Runtime/Patches/ImportedMapRendering.cs`, leaving shared snapshots and restore/cleanup ownership in `CourseScenePatch.cs`. The refactored source compiles with zero warnings/errors and passes the full offline preflight in an exact DRAFT archive; in-game verification remains open.
- Strengthen the release archive audit so it requires both `CourseScenePatcher` source files, guarding against an incomplete source package.
- Reject Unity source assets, game textures/images/fonts, and BONELAB pallet catalogs/manifests in public archives; synthetic image and pallet-manifest archive fixtures are rejected as intended.
- Expose five existing imported-map rendering options in Course Studio under a collapsed **Terrain & vegetation** group: use lit foliage cutouts, preserve baked lightmaps, repair terrain materials, rebuild textured terrain foliage details, and keep tree meshes visible at distance. The controls load from and save to course settings. The targeted editor check covers serialization and wiring; visual results and performance still need the coordinated in-game pass.
- Clarify Installed maps readiness: cards separately warn when overview bounds remain at the default or a hole still needs explicit tee/basket placement, while **READY TO EDIT** continues to describe package/path health. The BONELAB source links remain documentation-only references in `docs/PORTING_GUIDE.md`; map assets stay out of Git and release archives.
- Add `Tools/TestReleasePreflight.ps1` to run the offline build/install, converter, package-validation, editor, and pure C# checks in one ordered command, with optional exact-DRAFT archive audit and installer fixtures. The check does not start DGM or replace clean-game testing.
- Extract archive asset exclusions into a reusable policy module and add a repeatable preflight with 12 forbidden and 5 allowed path examples. The refreshed 174-entry 0.5.34 DRAFT passed the complete offline preflight and exact-archive installer fixtures; its SHA-256 is `91495765202C358A85D1D362C33025EDE0B077D04A33C5DD6037BBD8D70BDD96`.
- Ignore nested `Tools/**/bin` and `Tools/**/obj` outputs, then verify 14 ignored paths and 5 includable source paths using a disposable Git worktree with external metadata. Add the Git ignore fixture to the release preflight and archive audit.
- Let the source Course Editor serve its current UI against a selected installed plugin folder through `DGM_COURSE_EDITOR_PLUGIN_DIR`, including that install's generated original-course catalog and map previews; `DGM_COURSE_EDITOR_READ_ONLY=1` blocks direct API writes during review. Isolated server tests cover installed course/map/catalog/preview discovery, read-only rejection, and writable export routing.
- Extend the release archive policy to reject machine-specific Windows user-profile paths in bundled text while allowing documented placeholders such as `C:\Users\you`; policy fixtures cover both cases.

## 0.5.33 development

- Expand imported-foliage recognition to common plant, bush, branch, spruce, and atlas material/texture names and route matching materials through the alpha-cutout rendering repair. The Milsim Forest test showed improved canopy visibility and loaded all nine planned tee/basket pairs, but oversized cutout cards and garbled plant textures remain. This import behavior is still experimental and requires another in-game pass after the material/settings changes settle.

## 0.5.32 development

- Prefer grounded imported-map samples with nearby standing-height colliders clear when planning long layouts; fall back to all grounded samples when fewer than six clear positions are available or the collision query is unavailable. Recalculate planned hole length and par from DGM's actual tee-pad front-middle and selected basket endpoints. Move scenic selector captures farther from and above the tee, and version the preview cache path so existing branch-heavy captures are not reused. Runtime placement, distance display, preview quality, and loading cost still require the single integrated in-game acceptance pass.

## 0.5.31 development

- Add an opt-in long imported-map layout that samples ground and varies hole lengths and headings; size and placement depend on each scene and require playtesting. Correct imported tee orientation for the game tee's separate aim child. Preserve course-card image detail instead of always shrinking images to 96 × 54, uploading pixels in a single batch.

## 0.5.30 development

- Capture selector previews with a standalone camera when the game has no tagged main camera. Log scheduling, missing scene objects, capture cancellation, and success/failure so preview problems can be diagnosed from the game log.

## 0.5.29 development

- Keep the custom loading screen for the course list only; entering an imported course no longer displays it.

## 0.5.28 development

- Capture a landscape selector image from the actual custom course after its round loads, cache it separately by course-definition fingerprint, and preserve the supplied card as a fallback and tee sign. Give custom courses a disc-and-basket badge instead of the base course's logo. Runtime visuals require in-game verification.

## 0.5.27 development

- Show a loading overlay before preparing custom selector entries and imported maps. Hold selector scrolling while its layout settles, then stop adjusting it when the player interacts; bounded timeouts release the interface on failures. Synchronous Unity asset work can still pause animation, and the behavior requires in-game verification.

## 0.5.26 development

- Read the round's game-owned hole-score collection by enumeration rather than index access, in line with the IL2CPP collection safety rule.
- Restore a reward display previously hidden for a custom course when the component is next set up for a non-custom selection or when the custom-course detection path fails.
- Add an archive-root PowerShell installer that updates the plugin and editor while preserving player data, with path checks and build commands compatible with Windows PowerShell 5.1. Disposable fresh/update fixtures pass.
- Pass the game root to MSBuild without a trailing separator so Windows PowerShell 5.1 preserves the full path, and join the BepInEx reference paths in the project file.
- Resolve default maintainer paths after parameter binding so Windows PowerShell 5.1 can use the default game and package directories.
- Load the ZIP compression assemblies and normalize pallet entry separators so the BONELAB folder and ZIP converters work in Windows PowerShell 5.1; all seven synthetic converter scenarios pass there.

## 0.5.25 development

- Add a separate local personal-best store for custom solo layouts. The round hook records a score relative to par only after every expected hole has a valid score; the selector reads that value through DGM's personal-best path. A targeted UI patch suppresses the missing reward widgets for custom-course selections while leaving built-in course progression alone. The candidate builds successfully and its isolated checks cover persistence across reloads, score improvements, incomplete rounds, and preservation of unreadable user data. A previously completed custom round was found in the game's PlayerStats data, confirming that its score persisted despite the broken course UI. That one record was imported manually in the development install; older results are not migrated automatically. The 0.5.25 candidate has since started in DGM and loaded Tree Land; the log confirms four definitions and all nine hole positions, and the user confirms foliage collision and sound. The available Player.log excerpt contains no recurrence of the prior reward-display exception, but personal-best display, completed-round recording, and persistence still need one integrated in-game verification.

## 0.5.24 development

- Add an opt-in approximate imported-terrain foliage sweep and game foliage-sound request, independent of the disabled native canopy proxy experiment. In the 0.5.24 Tree Land round, the user confirmed shrubs/leaves react and foliage sound plays; the runtime log confirms 342 foliage volumes were prepared and the disc-slowing path ran. A read-only check of the installed Course Studio showed both installed map packages ready, search/filter/rescan working, and zoomable nine-hole overviews for Tree Land and Canon Park. Selector scrolling, hole reachability, performance, foliage appearance, and branch animation remain unverified.
- Validate the BONELAB pallet converter against two user-provided PC pallets in temporary folders: scene/dependency bytes were preserved, starter course cards were generated, package validation passed, and temporary map assets were removed. Tree Land has a user-confirmed DGM round; the later Hood Corner log shows a round loaded, all nine hole positions applied, a temporary key light added because no source directional light was active, a base-terrain audio fallback, and a post-throw hook. Hood Corner's visible lighting/audio, full-hole reachability, and performance lack user confirmation. Source-scene preview generation remains unverified. User map assets are excluded from release archives.
- Exercise the generic Unity scene-bundle adapter against the authorized Tree Land scene and dependencies. Its temporary package validated with the scene bytes unchanged; those assets had loaded in DGM through the BONELAB package, while the generic-adapter output itself was not separately launched.
- Update the automated BONELAB fixtures to mirror nested Marrow pallet catalogs, `PALLET_BARCODE` bundle references, and named scene variants, while keeping all fixture bundles synthetic.
- Add disposable build/install smoke checks for fresh and update paths, verifying DLL/editor placement, clean-install sample copying, and preservation of existing courses/cards, maps, generated catalog, and previews without writing to the live game folder.
- Include the exact pinned BepInEx, Il2CppInterop, HarmonyX, and Harmony license texts in review packages; expand the archive audit to require both the license copies and an explicit MIT-0 scope in the manifest.
- Reject Unity scene dependencies without a UnityFS header in the shared package writer and validator. Converter fixtures cover malformed dependency rejection and partial-stage cleanup.

## 0.5.23 development

- Reuse Course Studio's existing PNG decoder for imported tee signs and tee cards after Unity's `ImageConversion.LoadImage` failed on this IL2CPP build. The build compiles; installation and in-game image rendering remain unverified.

## 0.5.22 development

- Carry forward the 0.5.21 selector-layout fix and 0.5.20 imported-map audio fallback. Add a framed tee sign for imported courses, use the course card when a hole card is missing, and configure Tree Land to prefer the base course sun. The 0.5.22 runtime log shows Tree Land loaded with all nine hole positions, selector layout sizing completed, base-sun and audio fallback paths ran, and basket chain rigs were awakened. The user confirmed that shadows look good. Tee-sign image loading failed in IL2CPP with a missing `ReadOnlySpan<T>.GetPinnableReference` method; 0.5.23 replaces that path but remains staged. Foliage collision and sound remain under investigation; canopy collision is disabled.

## 0.5.21 development

- Replace the unsupported IL2CPP `RectTransformUtility.CalculateRelativeRectTransformBounds` call with preferred/root layout heights. The 0.5.21 build is installed; full selector scrolling and course reachability still need in-game verification.

## 0.5.20 development

- Add an imported-map collision-audio fallback that clamps out-of-range samples to the base course terrain. The fallback sound may not match the imported surface. This version also carries forward the ambient-light and basket-chain changes; their gameplay effects remain unverified.

## 0.5.19 development

- Removed the unsafe direct `FoliageSweep.Classify` diagnostic after an IL2CPP access-violation crash. The loader skips canopy-trigger generation even when the course flag is enabled; the editor control remains a no-op pending cleanup. The Tree Land example disables canopy triggers, the DGM foliage shader, and base-sun selection. The user confirmed a Tree Land round started on 0.5.19 without crashing; all-hole reachability, selector scrolling, and throw behavior remain unverified.

## 0.5.18 development

- Add experimental game foliage shader, game-layer foliage trigger proxies, base-course sun selection, ambient fill and moved-basket chain wake-up. In-game appearance, collisions, chain visibility and performance remain unverified.
- Preserve the new foliage and sun options through normalized layout conversion, with converter assertions for these settings.

## 0.5.17 development

- Record the exact local compiler reference versions, source commits, and hashes, and pin dependency license links to those versions.
- Add operation labels and full exception details to offline course-selector layout failures so IL2CPP unstripping errors can be traced to the exact UI call. Runtime scrolling remains unverified.
- Record that the 0.5.16 Tree Land gameplay check found overly dark imported rendering, missing trunk/rock/shrub shadows, and foliage proxies that did not affect throws. Further runtime investigation is required.

## 0.5.16 development

- Replace terrain-prefab canopy collision changes with sparse scene proxies for terrain-tree instances. Add a physics raycast probe and capture the tree bark texture for shadow diagnosis. Visual movement, collision response and trunk shadows remain unverified.
- Keep the normalized layout converter aligned with current imported-map lighting settings and add repeatable BONELAB, Unity bundle, layout, and rollback smoke checks.
- Add an isolated Course Editor export test for direct server installation, folder writes, downloads, and installed-map rescanning.

## 0.5.15 development

- Enabled GPU instancing for imported tree materials during realtime-shadow repair and added an opt-in sparse canopy collision approximation for terrain trees. Both changes need gameplay and performance validation.

## 0.5.14 development

- Record terrain-tree prefab shadow passes and one post-throw rendering snapshot to investigate missing imported terrain shadows; no per-frame polling is added.
- Split the browser editor into feature scripts and moved selector/card responsibilities into focused C# service files without changing the public course format.
- Added a converter adapter author guide covering coordinate semantics, shared package creation, fixture safety, and honest source-game support claims.

## 0.5.13 development

- Clear stale imported terrain lightmap indices and enable available shadow-caster passes. Expanded shadow diagnostics; projected shadows on imported terrain still need gameplay verification.

## 0.5.12 candidate

- Added imported terrain shadow setup and diagnostics for URP pipeline, quality, camera and directional-light settings.
- Added Course Studio controls for experimental imported-map realtime shadows and hiding imported particle renderers.
- Smoke-tested Tree Land in game: four course definitions registered, both map dependencies loaded, all nine hole placements were logged, and the first-hole course card appeared. Visual shadow results remain inconclusive; the selector layout warning remains under investigation.

## 0.5.11 draft

- Added experimental imported-map realtime shadows and preserved imported particle shaders.
- Added an installed-map explorer with search, health filters, rescan and repairable setup entries. Split map discovery into a separately testable module.
- Added generic Unity scene-bundle import, cross-game layout interchange, and direct BONELAB PC pallet-folder support in addition to ZIP inputs.
- Smoke-tested Tree Land in game: dependencies, all nine hole placements and imported foliage loaded. The selector emitted a layout warning, and the captured view did not establish visible object shadows.
- Added player, porting, architecture, AI contributor, supported-environment, release and troubleshooting documentation.
- Organized loader source under `Domain/`, `Runtime/`, `Services/` and `Diagnostics/` while preserving its namespace.
- Added draft packaging and course-package validation workflows. Public packaging remains gated on a project license and release checks.

## Earlier 0.5.x

- Added offline custom course cards and course/hole authoring.
- Added built-in-course tee/basket offsets, imported-scene placement, OB polygons, shot limits and per-hole card images.
- Added course catalog and map preview export, BONELAB PC pallet scene packaging, and imported-scene lighting options.

The exact installed plugin version is declared in `Plugin.cs`.
