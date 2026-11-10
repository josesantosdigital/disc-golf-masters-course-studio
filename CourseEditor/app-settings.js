"use strict";

function removeCurrentHole() {
  if (!state.draft || state.draft.holes.length <= 1) { toast("A course needs at least one hole."); return; }
  state.draft.holes.splice(state.holeIndex, 1); state.holeIndex = Math.max(0, state.holeIndex - 1); renderEditor();
}
function addHole() {
  if (!state.draft || state.draft.holes.length >= 18) { toast("A course can have up to 18 holes."); return; }
  const layout = selectedLayout(), index = state.draft.holes.length;
  const hole = defaultHole(layout && layout.holes ? layout.holes[index] : null, index, index + 1, state.draft.baseCourse);
  if (isImportedMap()) {
    hole.mapTeePosition = mapNormToWorld(hole.mapAnchors.tee);
    hole.mapBasketPosition = mapNormToWorld(hole.mapAnchors.basket);
  }
  state.draft.holes.push(hole);
  state.holeIndex = index; renderEditor();
}
function setCourseName(value) {
  state.draft.name = value.slice(0, 64); byId("course-name").value = state.draft.name;
  byId("course-description-short").textContent = state.draft.description || "Custom layout · Offline"; drawCardPreview(); saveState();
}
function setCourseMeta() {
  state.draft.name = byId("course-name").value || "Untitled Course";
  state.draft.id = slug(byId("course-id").value); byId("course-id").value = state.draft.id;
  state.draft.description = byId("course-description").value;
  byId("course-description-short").textContent = state.draft.description || "Custom layout · Offline"; drawCardPreview(); saveState();
}
function setBaseCourse(id) {
  const base = originalCourse(id); if (!base) return;
  const layout = base.layouts && base.layouts[0], count = layout && layout.holes && layout.holes.length ? layout.holes.length : state.draft.holes.length;
  state.draft.baseCourse = base.id; state.draft.baseLayout = layout ? layout.id : ""; state.draft.holes = [];
  for (let i = 0; i < Math.min(18, count || 1); i++) state.draft.holes.push(defaultHole(layout && layout.holes ? layout.holes[i] : null, i, count, base.id));
  if (isImportedMap()) state.draft.holes.forEach(function (hole) {
    hole.mapTeePosition = mapNormToWorld(hole.mapAnchors.tee);
    hole.mapBasketPosition = mapNormToWorld(hole.mapAnchors.basket);
  });
  state.mapWidth = Math.max(200, Math.min(1200, Math.round((layout && layout.lengthMeters || count * 74) * .34)));
  state.holeIndex = 0; byId("map-width").value = state.mapWidth; renderEditor(); toast("Switched the base terrain to " + base.name + ".");
}
function setBaseLayout(id) {
  const base = originalCourse(state.draft.baseCourse), layout = base && base.layouts && base.layouts.find(function (item) { return item.id === id; });
  if (!layout) { state.draft.baseLayout = id || ""; renderEditor(); return; }
  state.draft.baseLayout = layout.id;
  const count = layout.holes && layout.holes.length ? layout.holes.length : state.draft.holes.length, next = [];
  for (let i = 0; i < Math.min(18, count); i++) {
    const existing = state.draft.holes[i], fresh = defaultHole(layout.holes ? layout.holes[i] : null, i, count, base.id);
    next.push(existing ? Object.assign(fresh, existing, {
      par: layout.holes && layout.holes[i] ? layout.holes[i].par : existing.par,
      lengthMeters: layout.holes && layout.holes[i] ? layout.holes[i].lengthMeters : existing.lengthMeters,
      teeVariant: layout.holes && layout.holes[i] ? layout.holes[i].teeVariant : existing.teeVariant,
      pinVariant: layout.holes && layout.holes[i] ? layout.holes[i].pinVariant : existing.pinVariant
    }) : fresh);
  }
  state.draft.holes = next.length ? next : [defaultHole(null, 0, 1, base.id)]; state.holeIndex = 0; renderEditor();
}
function setCourseSource(mode) {
  const imported = mode === "imported";
  state.draft.sourceMode = imported ? "imported" : "original";
  if (imported) {
    if (!state.draft.mapScene) state.draft.mapScene = { scenePath: "", bundlePath: "", dependencyBundlePaths: [] };
    state.draft.holes.forEach(function (hole) {
      if (!hole.mapTeePosition) hole.mapTeePosition = mapNormToWorld(mapAnchors(hole).tee);
      if (!hole.mapBasketPosition) hole.mapBasketPosition = mapNormToWorld(mapAnchors(hole).basket);
    });
  } else {
    state.draft.mapScene = null; state.sceneBundleFile = null; state.dependencyFiles = [];
  }
  renderEditor(); saveState(); paintMap();
}
function updateMapSceneField(fieldName, value) {
  if (!state.draft.mapScene) state.draft.mapScene = { scenePath: "", bundlePath: "", dependencyBundlePaths: [] };
  if (fieldName === "dependencyBundlePaths") {
    state.draft.mapScene.dependencyBundlePaths = value.split(",").map(function (item) { return item.trim(); }).filter(Boolean);
  } else state.draft.mapScene[fieldName] = value.trim();
  saveState();
}
function validRelativePackagePath(value) {
  const text = String(value || "").trim().replace(/\\/g, "/");
  return !!text && !/^(?:[A-Za-z]:|\/)/.test(text) && text.split("/").every(function (part) { return part && part !== "." && part !== ".."; });
}
function setImportedMapBounds() {
  const bounds = {
    minX: Number(byId("world-min-x").value), maxX: Number(byId("world-max-x").value),
    minZ: Number(byId("world-min-z").value), maxZ: Number(byId("world-max-z").value),
    groundY: Number(byId("world-ground-y").value)
  };
  if (!Object.values(bounds).every(Number.isFinite) || bounds.maxX <= bounds.minX || bounds.maxZ <= bounds.minZ ||
      Math.max(Math.abs(bounds.minX), Math.abs(bounds.maxX), Math.abs(bounds.minZ), Math.abs(bounds.maxZ), Math.abs(bounds.groundY)) > 10000) {
    toast("World bounds need finite values, with max greater than min."); renderEditor(); return;
  }
  state.draft.mapWorldBounds = bounds; renderEditor(); saveState();
}
function setLayoutGroundColliderExclusions() {
  const values = byId("layout-ground-collider-exclusions").value.split(",").map(function (item) { return item.trim(); }).filter(Boolean);
  if (values.length > 8 || values.some(function (item) { return item.length > 64; })) {
    toast("Use up to eight collider-name fragments, each no longer than 64 characters."); renderEditor(); return;
  }
  state.draft.layoutGroundColliderExclusions = values; saveState();
}
function colorToHex(color) {
  const channel = function (value) { return Math.round(clamp(Number(value) || 0, 0, 1) * 255).toString(16).padStart(2, "0"); };
  return "#" + channel(color && color.r) + channel(color && color.g) + channel(color && color.b);
}
function colorFromHex(value) {
  const match = /^#?([0-9a-f]{6})$/i.exec(String(value || ""));
  if (!match) return { r: 1, g: 1, b: 1 };
  const hex = match[1];
  return { r: parseInt(hex.slice(0, 2), 16) / 255, g: parseInt(hex.slice(2, 4), 16) / 255, b: parseInt(hex.slice(4, 6), 16) / 255 };
}
function setImportedMapLighting() {
  state.draft.mapLighting = Object.assign({}, state.draft.mapLighting || defaultMapLighting(), {
    forceUnlitMaterials: byId("map-force-unlit").checked,
    realtimeMapShadows: byId("map-realtime-shadows").checked,
    hideImportedParticles: byId("map-hide-particles").checked,
    generateTreeCanopyColliders: byId("map-tree-canopy-collisions").checked,
    experimentalFoliageInteractions: byId("map-foliage-interactions").checked,
    useGameFoliageShader: byId("map-game-foliage").checked,
    useLitFoliageMaterials: byId("map-lit-foliage").checked,
    preferBaseCourseSun: byId("map-base-sun").checked,
    disableCameraOcclusionCulling: byId("map-disable-occlusion").checked,
    localLightIntensityMultiplier: clamp(Number(byId("map-local-light-multiplier").value), 0, 5),
    preserveLightmaps: byId("map-preserve-lightmaps").checked,
    repairTerrainMaterials: byId("map-repair-terrain-materials").checked,
    repairTerrainTextureDetails: byId("map-repair-terrain-details").checked,
    forceTreeMeshLods: byId("map-force-tree-mesh-lods").checked,
    albedoBrightness: clamp(Number(byId("map-albedo-brightness").value) || 1, 0.5, 3),
    ambientColor: colorFromHex(byId("map-ambient-color").value),
    ambientIntensity: clamp(Number(byId("map-ambient-intensity").value) || 0, 0, 2.5),
    keyLightColor: colorFromHex(byId("map-key-color").value),
    keyLightIntensity: clamp(Number(byId("map-key-intensity").value) || 0, 0, 5),
    keyLightYawDegrees: Number(byId("map-key-yaw").value) || 0,
    keyLightPitchDegrees: clamp(Number(byId("map-key-pitch").value) || 0, -89, 89)
  });
  saveState();
}
function chooseSceneBundle(file) {
  if (!file) return;
  state.sceneBundleFile = file;
  if (!state.draft.mapScene.bundlePath) state.draft.mapScene.bundlePath = "Maps/" + slug(state.draft.id) + "/scene.bundle";
  state.draft.mapScene.bundlePath = state.draft.mapScene.bundlePath.replace(/\\/g, "/");
  byId("map-bundle-path").value = state.draft.mapScene.bundlePath;
  byId("scene-bundle-status").textContent = file.name + " · ready to export";
  saveState(); toast("Scene bundle selected. Add the scene path from inside the bundle.");
}
function chooseDependencies(files) {
  state.dependencyFiles = Array.from(files || []).map(function (file) {
    return { file: file, path: "Maps/" + slug(state.draft.id) + "/dependencies/" + file.name };
  });
  state.draft.mapScene.dependencyBundlePaths = state.dependencyFiles.map(function (entry) { return entry.path; });
  byId("map-dependency-paths").value = state.draft.mapScene.dependencyBundlePaths.join(", ");
  saveState(); toast(state.dependencyFiles.length + " dependency bundle(s) selected.");
}
function chooseMapPreview(file) {
  if (!file) return;
  const reader = new FileReader();
  reader.onload = function () {
    const image = new Image();
    image.onload = function () {
      const maxSide = 1800, scale = Math.min(1, maxSide / Math.max(image.naturalWidth, image.naturalHeight));
      const preview = document.createElement("canvas");
      preview.width = Math.max(1, Math.round(image.naturalWidth * scale));
      preview.height = Math.max(1, Math.round(image.naturalHeight * scale));
      preview.getContext("2d").drawImage(image, 0, 0, preview.width, preview.height);
      state.draft.mapPreviewImage = slug(state.draft.id) + "-map.png";
      state.draft.mapPreviewDataUrl = preview.toDataURL("image/png");
      state.draft.mapPreviewWorldBounds = null;
      state.draft._legacyObPreviewBounds = null;
      setMapImage(); paintMap(); saveState(); toast("Map preview loaded and converted to PNG.");
    };
    image.onerror = function () { toast("Could not read that map image."); };
    image.src = reader.result;
  };
  reader.readAsDataURL(file);
}
