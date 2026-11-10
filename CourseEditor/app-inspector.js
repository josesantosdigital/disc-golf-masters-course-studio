"use strict";

function renderEditor() {
  if (!state.draft) return;
  const base = originalCourse(state.draft.baseCourse), layout = selectedLayout(), holes = state.draft.holes;
  state.holeIndex = Math.max(0, Math.min(state.holeIndex, holes.length - 1));
  byId("current-base-name").textContent = base ? base.name : "Original course";
  byId("summary-course-name").textContent = base ? base.name : "Original course";
  byId("summary-layout-name").textContent = layout ? layout.name : "Original course terrain";
  byId("current-hole-crumb").textContent = "Hole " + String(state.holeIndex + 1).padStart(2, "0");
  byId("course-description-short").textContent = state.draft.description || "Custom layout · Offline";
  byId("total-holes").textContent = holes.length;
  byId("total-par").textContent = holes.reduce(function (sum, item) { return sum + (Number(item.par) || 0); }, 0);
  byId("total-length").textContent = holes.reduce(function (sum, item) { return sum + (Number(item.lengthMeters) || 0); }, 0).toLocaleString();
  byId("hole-count-label").textContent = holes.length + " / 18";
  const isImported = state.draft.sourceMode === "imported";
  if (isImported && !state.draft.mapScene) state.draft.mapScene = { scenePath: "", bundlePath: "", dependencyBundlePaths: [] };
  byId("course-source").value = isImported ? "imported" : "original";
  byId("imported-map-panel").hidden = !isImported;
  byId("map-width").closest(".scale-control").style.display = isImported ? "none" : "";
  byId("calibrate-tee").hidden = isImported;
  byId("calibrate-basket").hidden = isImported;
  if (isImported) {
    byId("map-scene-path").value = state.draft.mapScene.scenePath || "";
    byId("map-bundle-path").value = state.draft.mapScene.bundlePath || "";
    byId("map-dependency-paths").value = (state.draft.mapScene.dependencyBundlePaths || []).join(", ");
    const bounds = state.draft.mapWorldBounds;
    byId("world-min-x").value = bounds.minX; byId("world-max-x").value = bounds.maxX;
    byId("world-min-z").value = bounds.minZ; byId("world-max-z").value = bounds.maxZ;
    byId("world-ground-y").value = bounds.groundY;
    byId("layout-ground-collider-exclusions").value = (state.draft.layoutGroundColliderExclusions || []).join(", ");
    const lighting = state.draft.mapLighting || defaultMapLighting();
    byId("map-ambient-color").value = colorToHex(lighting.ambientColor);
    byId("map-ambient-intensity").value = lighting.ambientIntensity;
    byId("map-key-color").value = colorToHex(lighting.keyLightColor);
    byId("map-key-intensity").value = lighting.keyLightIntensity;
    byId("map-local-light-multiplier").value = lighting.localLightIntensityMultiplier;
    byId("map-key-yaw").value = lighting.keyLightYawDegrees;
    byId("map-key-pitch").value = lighting.keyLightPitchDegrees;
    byId("map-force-unlit").checked = lighting.forceUnlitMaterials !== false;
    byId("map-realtime-shadows").checked = lighting.realtimeMapShadows === true;
    byId("map-hide-particles").checked = lighting.hideImportedParticles === true;
    byId("map-tree-canopy-collisions").checked = lighting.generateTreeCanopyColliders === true;
    byId("map-foliage-interactions").checked = lighting.experimentalFoliageInteractions === true;
    byId("map-game-foliage").checked = lighting.useGameFoliageShader === true;
    byId("map-lit-foliage").checked = lighting.useLitFoliageMaterials === true;
    byId("map-base-sun").checked = lighting.preferBaseCourseSun === true;
    byId("map-disable-occlusion").checked = lighting.disableCameraOcclusionCulling === true;
    byId("map-preserve-lightmaps").checked = lighting.preserveLightmaps === true;
    byId("map-repair-terrain-materials").checked = lighting.repairTerrainMaterials === true;
    byId("map-repair-terrain-details").checked = lighting.repairTerrainTextureDetails === true;
    byId("map-force-tree-mesh-lods").checked = lighting.forceTreeMeshLods === true;
    byId("map-albedo-brightness").value = lighting.albedoBrightness;
    byId("scene-bundle-status").textContent = state.sceneBundleFile ? state.sceneBundleFile.name : (state.draft.mapScene.bundlePath ? "Existing package path: " + state.draft.mapScene.bundlePath : "No scene bundle selected");
  }
  renderBaseSelectors(); renderHoleList(); renderInspector(); setMapImage(); paintMap(); drawCardPreview(); saveState();
}
function renderHoleList() {
  const layout = selectedLayout();
  byId("hole-list").innerHTML = state.draft.holes.map(function (hole, index) {
    const source = layout && layout.holes && layout.holes[index];
    const title = (source && source.name) || ("Hole " + String(index + 1).padStart(2, "0"));
    const limit = hole.shotLimit ? " · max " + hole.shotLimit : "";
    return '<button class="hole-row' + (index === state.holeIndex ? " active" : "") + '" data-index="' + index + '">' +
      '<span class="hole-number">' + String(index + 1).padStart(2, "0") + '</span><span class="hole-name"><b>' + escapeHtml(title) + '</b><small>' + (hole.lengthMeters || 0) + ' m' + limit + '</small></span><span class="hole-par">P' + (hole.par || 3) + '</span></button>';
  }).join("");
  byId("hole-list").querySelectorAll(".hole-row").forEach(function (row) {
    row.addEventListener("click", function () {
      state.holeIndex = Number(row.dataset.index); state.selectedZone = -1;
      state.drawingOb = false; state.pendingObPoints = []; renderEditor();
    });
  });
}
function field(label, name, value, unit, min, max, step, full) {
  const type = unit || "number";
  let input = "";
  if (type === "select") {
    input = '<select data-field="' + name + '">' + value.map(function (item) {
      return '<option value="' + escapeAttr(item[0]) + '"' + (item[0] === item[2] ? " selected" : "") + '>' + escapeHtml(item[1]) + '</option>';
    }).join("") + '</select>';
  } else if (type === "text") {
    input = '<textarea data-field="' + name + '" maxlength="120">' + escapeHtml(String(value || "")) + '</textarea>';
  } else {
    input = '<div class="field-unit"><input data-field="' + name + '" type="number" value="' + escapeAttr(value) + '"' +
      (min !== undefined ? ' min="' + min + '"' : "") + (max !== undefined ? ' max="' + max + '"' : "") + (step !== undefined ? ' step="' + step + '"' : "") + '><em>' + escapeHtml(unit === "number" ? "" : unit || "") + '</em></div>';
  }
  return '<label class="field' + (full ? " full" : "") + '"><span>' + escapeHtml(label) + '</span>' + input + '</label>';
}
function selectField(label, name, choices, current) {
  return field(label, name, choices.map(function (value) { return [value, value, value === current ? current : ""]; }), "select");
}
function switchRow(title, description, fieldName, enabled) {
  return '<div class="toggle-row"><div class="toggle-copy"><b>' + escapeHtml(title) + '</b><small>' + escapeHtml(description) + '</small></div><button class="switch ' + (enabled ? "on" : "") + '" data-toggle="' + fieldName + '" aria-label="' + escapeAttr(title) + '" aria-pressed="' + String(!!enabled) + '"></button></div>';
}
function offsetFields(offset, prefix) {
  return '<div class="field-grid">' +
    field("RIGHT", prefix + ".right", Number(offset.right) || 0, "m", -250, 250, 0.1) +
    field("FORWARD", prefix + ".forward", Number(offset.forward) || 0, "m", -250, 250, 0.1) +
    field("HEIGHT", prefix + ".up", Number(offset.up) || 0, "m", -100, 100, 0.1) +
    field("ANGLE / YAW", prefix + "Yaw", Number(currentHole()[prefix === "teeOffset" ? "teeYawDegrees" : "basketYawDegrees"]) || 0, "°", -180, 180, 1) +
    '</div>';
}
function worldPositionFields(position, prefix, yawField) {
  const world = position || mapNormToWorld(mapAnchors(currentHole())[prefix === "mapTeePosition" ? "tee" : "basket"]);
  return '<div class="field-grid">' +
    field("WORLD X", prefix + ".x", Number(world.x) || 0, "m", -10000, 10000, 0.1) +
    field("HEIGHT / Y", prefix + ".y", Number(world.y) || 0, "m", -10000, 10000, 0.1) +
    field("WORLD Z", prefix + ".z", Number(world.z) || 0, "m", -10000, 10000, 0.1) +
    field("ANGLE / YAW", yawField, Number(currentHole()[yawField === "teeYawDegrees" ? "teeYawDegrees" : "basketYawDegrees"]) || 0, "°", -180, 180, 1) +
    '</div>';
}
function renderInspector() {
  const hole = currentHole();
  if (!hole) return;
  document.querySelectorAll(".inspector-tab").forEach(function (button) { button.classList.toggle("active", button.dataset.inspector === state.inspector); });
  let html = "";
  if (state.inspector === "hole") {
    html = '<div class="inspector-block"><div class="inspector-head"><h3>Hole ' + String(state.holeIndex + 1).padStart(2, "0") + '</h3><small>DESIGN SETTINGS</small></div>' +
      '<div class="field-grid">' + field("PAR", "par", Number(hole.par) || 3, "number", 1, 8, 1) +
      field("LENGTH", "lengthMeters", Number(hole.lengthMeters) || 60, "m", 1, 2000, 1) +
      field("SHOT LIMIT", "shotLimit", Number(hole.shotLimit) || 0, "throws", 0, 20, 1, true) + '</div>' +
      switchRow("Show tee pad", "Show the tee pad artwork in game; the starting position always remains usable.", "teeEnabled", hole.teeEnabled === true) +
      switchRow("Show hole card", "Show this hole's card beside the tee.", "cardEnabled", hole.cardEnabled === true) +
      '<div class="note-box"><span>↳</span><div>Set shot limit to <b>0</b> for unlimited throws. When the cap is reached, the offline round advances to the next hole.</div></div></div>' +
      '<div class="inspector-block"><div class="inspector-head"><h3>Starting variants</h3><small>BASE SCENE</small></div><div class="field-grid">' +
      selectField("TEE", "teeVariant", ["Default", "Blue", "Red", "Yellow", "Black"], hole.teeVariant || "Default") +
      selectField("PIN", "pinVariant", ["Default", "Blue", "Red", "Yellow", "Black"], hole.pinVariant || "Default") + '</div></div>';
  } else if (state.inspector === "tee") {
    html = '<div class="inspector-block"><div class="inspector-head"><h3>Starting tee</h3><small>HOLE ' + String(state.holeIndex + 1).padStart(2, "0") + '</small></div>' +
      switchRow("Show tee pad", "Show the tee pad artwork in game; the starting position remains usable.", "teeEnabled", hole.teeEnabled === true) +
      '<div class="field-label" style="margin:14px 0 9px">' + (isImportedMap() ? "POSITION IN MAP WORLD" : "POSITION RELATIVE TO ORIGINAL TEE") + '</div>' +
      (isImportedMap() ? worldPositionFields(hole.mapTeePosition, "mapTeePosition", "teeYawDegrees") : offsetFields(hole.teeOffset, "teeOffset")) +
      '<div class="inspector-actions">' + (hole.teeEnabled === false ? '<button class="inspector-button primary" data-action="add-tee">＋ Add tee</button>' : '<button class="inspector-button danger" data-action="remove-tee">− Remove tee</button>') +
      '<button class="inspector-button" data-action="reset-tee">Reset position</button></div></div>' +
      '<div class="note-box"><span>⌖</span><div>' + (isImportedMap() ? "Drag the green tee or enter its world X, height Y and Z. If Y matches map ground Y, the game snaps it to nearby ground on load; enter a different Y to keep a deliberate elevation. Tee yaw rotates from the tee-to-basket direction." : "Drag the green tee on the map. Height is measured in Unity meters above the original tee pad.") + '</div></div>';
  } else if (state.inspector === "basket") {
    html = '<div class="inspector-block"><div class="inspector-head"><h3>Basket position</h3><small>HOLE ' + String(state.holeIndex + 1).padStart(2, "0") + '</small></div>' +
      switchRow("Basket marker", "Remove hides the basket art while keeping the game scoring trigger.", "basketEnabled", hole.basketEnabled !== false) +
      '<div class="field-label" style="margin:14px 0 9px">' + (isImportedMap() ? "POSITION IN MAP WORLD" : "POSITION RELATIVE TO ORIGINAL BASKET") + '</div>' +
      (isImportedMap() ? worldPositionFields(hole.mapBasketPosition, "mapBasketPosition", "basketYawDegrees") : offsetFields(hole.basketOffset, "basketOffset")) +
      '<div class="inspector-actions">' + (hole.basketEnabled === false ? '<button class="inspector-button primary" data-action="add-basket">＋ Add basket</button>' : '<button class="inspector-button danger" data-action="remove-basket">− Remove basket</button>') +
      '<button class="inspector-button" data-action="reset-basket">Reset position</button></div></div>' +
      '<div class="note-box"><span>◎</span><div>' + (isImportedMap() ? "Drag the amber basket or enter its world X, height Y and Z. If Y matches map ground Y, the game snaps it to nearby ground on load; enter a different Y to keep a deliberate elevation. Basket yaw is in world degrees." : "Drag the amber basket on the map. Height and angle are applied to its in-game transform.") + '</div></div>';
  } else if (state.inspector === "ob") {
    const zones = Array.isArray(hole.obZones) ? hole.obZones : [];
    html = '<div class="inspector-block"><div class="inspector-head"><h3>OB limits</h3><small>' + zones.length + ' ZONES</small></div>' +
      '<div class="note-box"><span>⬡</span><div>Draw closed polygon zones on the map. Points are stored in meters relative to this hole’s original tee.</div></div>' +
      '<div class="inspector-actions"><button class="inspector-button primary" data-action="start-ob">＋ Draw OB zone</button>' + (state.pendingObPoints.length ? '<button class="inspector-button" data-action="finish-ob">Finish zone</button>' : "") + '</div>' +
      '<div class="inspector-block"><div class="zone-list">' + (zones.length ? zones.map(function (zone, index) {
        return '<div class="zone-item' + (state.selectedZone === index ? " active" : "") + '"><span><i></i>' + escapeHtml(zone.name || ("OB zone " + (index + 1))) + ' <small>' + ((zone.points || []).length) + ' pts</small></span><button data-action="delete-zone" data-index="' + index + '" title="Remove zone" aria-label="Remove ' + escapeAttr(zone.name || "zone") + '">×</button></div>';
      }).join("") : '<div class="empty-zones">No custom OB on this hole yet.</div>') + '</div></div></div>';
  } else {
    html = '<div class="inspector-block"><div class="inspector-head"><h3>Tee-side card</h3><small>PER HOLE</small></div>' +
      switchRow("Show hole card", "Show this hole's card beside the tee.", "cardEnabled", hole.cardEnabled === true) +
      '<canvas id="card-preview" class="card-preview" width="900" height="1200"></canvas><div class="field-grid">' +
      field("CARD SUBTITLE", "cardSubtitle", state.draft.cardSubtitle || "CUSTOM OFFLINE LAYOUT", "text", undefined, undefined, undefined, true) +
      field("HOLE NOTE", "cardNote", hole.cardNote || "", "text", undefined, undefined, undefined, true) + '</div>' +
      '<div class="field-label" style="margin-top:12px">CARD COLORWAY</div><div class="card-colors">' +
      ["moss", "sand", "night", "red"].map(function (theme) { return '<button class="color-choice color-' + theme + (state.cardTheme === theme ? " active" : "") + '" data-theme="' + theme + '" title="' + theme + '" aria-label="' + theme + ' colorway"></button>'; }).join("") +
      '</div><div class="note-box"><span>▣</span><div>A 3:4 PNG card is generated for each hole and can appear beside the tee. The selector uses a scenic image captured when you play the course, with this card as its fallback.</div></div></div>';
  }
  byId("inspector-content").innerHTML = html;
  bindInspector();
  if (state.inspector === "card") drawCardPreview();
  byId("legend-tee").textContent = hole.teeVariant || "A";
  byId("legend-basket").textContent = hole.pinVariant || "A";
}
function bindInspector() {
  const content = byId("inspector-content");
  content.querySelectorAll("[data-field]").forEach(function (input) {
    input.addEventListener("change", function () { applyField(input.dataset.field, input.value); });
  });
  content.querySelectorAll("[data-toggle]").forEach(function (button) {
    button.addEventListener("click", function () {
      const key = button.dataset.toggle; currentHole()[key] = !currentHole()[key];
      renderInspector(); paintMap(); saveState();
    });
  });
  content.querySelectorAll("[data-action]").forEach(function (button) {
    button.addEventListener("click", function () { doAction(button.dataset.action, Number(button.dataset.index)); });
  });
  content.querySelectorAll("[data-theme]").forEach(function (button) {
    button.addEventListener("click", function () { state.cardTheme = button.dataset.theme; renderInspector(); saveState(); });
  });
}
function applyField(name, rawValue) {
  const hole = currentHole();
  let value = rawValue;
  if (name === "par" || name === "lengthMeters" || name === "shotLimit" || name === "teeYawDegrees" || name === "basketYawDegrees" || name.indexOf("mapTeePosition.") === 0 || name.indexOf("mapBasketPosition.") === 0 ||
      name.indexOf("Offset.") === 0 || name.indexOf("Yaw") > 0) {
    value = Number(rawValue); if (!Number.isFinite(value)) value = 0;
  }
  if (name === "par") hole.par = Math.max(1, Math.min(8, Math.round(value)));
  else if (name === "lengthMeters") hole.lengthMeters = Math.max(1, Math.min(2000, Math.round(value)));
  else if (name === "shotLimit") hole.shotLimit = Math.max(0, Math.min(20, Math.round(value)));
  else if (name.indexOf("teeOffset.") === 0) hole.teeOffset[name.split(".")[1]] = value;
  else if (name.indexOf("basketOffset.") === 0) hole.basketOffset[name.split(".")[1]] = value;
  else if (name === "teeOffsetYaw") hole.teeYawDegrees = value;
  else if (name === "basketOffsetYaw") hole.basketYawDegrees = value;
  else if (name === "teeYawDegrees") hole.teeYawDegrees = value;
  else if (name === "basketYawDegrees") hole.basketYawDegrees = value;
  else if (name.indexOf("mapTeePosition.") === 0) hole.mapTeePosition[name.split(".")[1]] = value;
  else if (name.indexOf("mapBasketPosition.") === 0) hole.mapBasketPosition[name.split(".")[1]] = value;
  else if (name === "teeVariant") hole.teeVariant = rawValue;
  else if (name === "pinVariant") hole.pinVariant = rawValue;
  else if (name === "cardSubtitle") state.draft.cardSubtitle = rawValue;
  else if (name === "cardNote") hole.cardNote = rawValue;
  renderEditor();
}
function doAction(action, index) {
  const hole = currentHole();
  if (action === "remove-tee") hole.teeEnabled = false;
  if (action === "add-tee") hole.teeEnabled = true;
  if (action === "remove-basket") hole.basketEnabled = false;
  if (action === "add-basket") hole.basketEnabled = true;
  if (action === "reset-tee") { hole.teeOffset = { right: 0, up: 0, forward: 0 }; hole.teeYawDegrees = 0; if (isImportedMap()) hole.mapTeePosition = mapNormToWorld(mapAnchors(hole).tee); }
  if (action === "reset-basket") { hole.basketOffset = { right: 0, up: 0, forward: 0 }; hole.basketYawDegrees = 0; hole.basketEnabled = true; if (isImportedMap()) hole.mapBasketPosition = mapNormToWorld(mapAnchors(hole).basket); }
  if (action === "start-ob") startDrawingOb();
  if (action === "cancel-ob") cancelOb();
  if (action === "finish-ob") finishOb();
  if (action === "delete-zone" && index >= 0) { hole.obZones.splice(index, 1); state.selectedZone = -1; }
  renderEditor();
}
function setMapImage() {
  const layout = selectedLayout();
  const installedMap = state.draft && state.draft.sourceMode === "imported"
    ? state.installedMaps.find(function (map) { return map.id === slug(state.draft.id); }) : null;
  const sourceImage = state.draft && (state.draft.mapPreviewDataUrl ||
    (state.draft.mapPreviewImage ? "/course-files/" + encodeURIComponent(state.draft.mapPreviewImage) : ""));
  const generatedMapImage = installedMap && installedMap.previewUrl
    ? installedMap.previewUrl
    : state.draft && state.draft.sourceMode === "imported"
      ? "/assets/maps/imported-" + encodeURIComponent(slug(state.draft.id)) + ".png" : "";
  const src = sourceImage || generatedMapImage || (state.draft && state.draft.sourceMode === "imported" ? "" : layout && (layout.mapImage || layout.cardImage));
  const key = src || "fallback:" + state.draft.baseCourse;
  if (key === state.imageKey) return;
  state.imageKey = key; state.image = null;
  const emptyHint = byId("map-empty-description");
  if (emptyHint) emptyHint.textContent = state.draft && state.draft.sourceMode === "imported"
    ? "The game creates a top-down map overview the first time you load this course. You can also choose a screenshot under Choose map preview."
    : "Open the offline course selector once to export the original course maps.";
  if (!src) { byId("map-empty").hidden = true; paintMap(); return; }
  const image = new Image();
  image.onload = function () {
    if (state.imageKey !== key) return;
    state.image = image; byId("map-empty").hidden = true; paintMap();
    const tip = byId("map-tip"); tip.classList.remove("complete");
    tip.textContent = "Set the base tee and basket locations to align this hole with its course map.";
  };
  image.onerror = function () {
    if (state.imageKey === key) { state.image = null; byId("map-empty").hidden = false; paintMap(); }
  };
  image.src = src;
}
