"use strict";

const FALLBACK_COURSES = [
  ["CanonPark", "Canon Park"], ["MinokaSprings", "Minoka Springs"],
  ["BaldurDgc", "Baldur Disc Golf Club"]
].map(function (entry) { return { id: entry[0], name: entry[1], layouts: [] }; });
const rawCatalog = window.DGM_BUILTIN_COURSES && Array.isArray(window.DGM_BUILTIN_COURSES.courses)
  ? window.DGM_BUILTIN_COURSES.courses : [];
const catalogById = {};
FALLBACK_COURSES.forEach(function (course) { catalogById[course.id.toLowerCase()] = course; });
rawCatalog.forEach(function (course) {
  const key = String(course.id || "").toLowerCase();
  if (!key) return;
  catalogById[key] = Object.assign({}, catalogById[key] || {}, course);
  catalogById[key].layouts = Array.isArray(course.layouts) ? course.layouts : [];
});
const CATALOG = Object.values(catalogById);
const $ = function (selector) { return document.querySelector(selector); };
const byId = function (id) { return document.getElementById(id); };
const state = {
  draft: null, holeIndex: 0, inspector: "hole", view: "grid", librarySource: "original", mapMode: "design",
  image: null, imageKey: "", zoom: 1, pan: { x: 0, y: 0 }, pointer: null,
  selectedMarker: "tee", calibrating: "", drawingOb: false, pendingObPoints: [],
  selectedZone: -1, spaceDown: false, toastTimer: 0, mapWidth: 400, cardTheme: "moss",
  sceneBundleFile: null, dependencyFiles: [], installedMaps: [], installedMapsLoaded: false,
  installedMapsLoading: false, installedMapsError: "", installedMapFilter: "all", createdMapLayoutIds: new Set()
};
const canvas = byId("course-map");
const ctx = canvas.getContext("2d");
const cardCanvas = document.createElement("canvas");
cardCanvas.width = 900; cardCanvas.height = 1200;
const cardCtx = cardCanvas.getContext("2d");
const themes = {
  moss: { dark: "#17291f", mid: "#36573a", light: "#b8ef72", warm: "#e6a46c" },
  sand: { dark: "#33251d", mid: "#76543b", light: "#e6bf80", warm: "#f1a874" },
  night: { dark: "#192638", mid: "#354d67", light: "#9dd5ff", warm: "#e4b879" },
  red: { dark: "#321f21", mid: "#70413d", light: "#f0a28d", warm: "#f1d28a" }
};

function originalCourse(id) {
  return CATALOG.find(function (course) { return course.id.toLowerCase() === String(id || "").toLowerCase(); }) || CATALOG[0];
}
function selectedLayout() {
  const course = state.draft && originalCourse(state.draft.baseCourse);
  if (!course || !course.layouts || !course.layouts.length) return null;
  return course.layouts.find(function (layout) { return layout.id === state.draft.baseLayout; }) || course.layouts[0];
}
function currentHole() { return state.draft && state.draft.holes[state.holeIndex]; }
function isImportedMap() { return !!(state.draft && state.draft.sourceMode === "imported"); }
function defaultMapLighting() {
  return {
    forceUnlitMaterials: true, realtimeMapShadows: false, hideImportedParticles: false, generateTreeCanopyColliders: false, experimentalFoliageInteractions: false, useGameFoliageShader: false, useLitFoliageMaterials: false, preferBaseCourseSun: false, disableCameraOcclusionCulling: false, localLightIntensityMultiplier: 1, albedoBrightness: 1.5,
    preserveLightmaps: false, repairTerrainMaterials: false, repairTerrainTextureDetails: false, forceTreeMeshLods: false,
    ambientColor: { r: 0.75, g: 0.75, b: 0.78 }, ambientIntensity: 1,
    keyLightColor: { r: 1, g: 1, b: 1 }, keyLightIntensity: 2.5,
    keyLightYawDegrees: -30, keyLightPitchDegrees: 50
  };
}
function obAnchor(hole) { return isImportedMap() ? markerMapPosition(hole, "tee") : mapAnchors(hole).tee; }
function mapAnchors(hole) {
  if (!hole.mapAnchors) hole.mapAnchors = {};
  if (!hole.mapAnchors.tee) hole.mapAnchors.tee = { x: 0.22, y: 0.51 };
  if (!hole.mapAnchors.basket) hole.mapAnchors.basket = { x: 0.76, y: 0.45 };
  if (typeof hole.mapAnchors.heading !== "number") hole.mapAnchors.heading = -0.11;
  if (typeof hole.mapAnchors.teeSet !== "boolean") hole.mapAnchors.teeSet = false;
  if (typeof hole.mapAnchors.basketSet !== "boolean") hole.mapAnchors.basketSet = false;
  return hole.mapAnchors;
}
function defaultHole(sourceHole, index, count, courseId) {
  const cols = Math.min(5, Math.max(3, Math.ceil(Math.sqrt(count))));
  const row = Math.floor(index / cols), col = index % cols;
  const x = 0.12 + (col / Math.max(1, cols - 1)) * 0.74;
  const y = 0.17 + ((row % 4) / 3) * 0.66;
  const originAngle = ((index * 41 + String(courseId).length * 7) % 71 - 35) * Math.PI / 180;
  const source = sourceHole || {};
  return {
    par: Number(source.par) || 3, lengthMeters: Number(source.lengthMeters) || 70,
    pinVariant: source.pinVariant || "Default", teeVariant: source.teeVariant || "Default",
    teeOffset: { right: 0, up: 0, forward: 0 }, basketOffset: { right: 0, up: 0, forward: 0 },
    teeYawDegrees: 0, basketYawDegrees: 0, teeEnabled: false, cardEnabled: false, basketEnabled: true,
    shotLimit: 0, obZones: [], mapAnchors: {
      tee: { x: x, y: y },
      basket: { x: x + Math.cos(originAngle) * 0.22, y: y - Math.sin(originAngle) * 0.22 },
      heading: originAngle
    }, mapTeePosition: null, mapBasketPosition: null, cardNote: ""
  };
}
function makeCourse(courseId, layoutId) {
  const base = originalCourse(courseId);
  if (!base) return;
  const layout = (base.layouts || []).find(function (item) { return item.id === layoutId; }) || (base.layouts || [])[0] || null;
  const count = layout && Array.isArray(layout.holes) && layout.holes.length ? Math.min(18, layout.holes.length) : 18;
  const length = layout && layout.lengthMeters ? layout.lengthMeters : count * 74;
  state.draft = {
    schemaVersion: 2, id: slug(base.id + "-custom"), name: "New " + base.name + " Course",
    baseCourse: base.id, baseLayout: layout ? layout.id : "",
    description: "A custom offline course on " + base.name + ".", courseCardImage: "", holes: [],
    sourceMode: "original", mapScene: null, mapPreviewImage: "", mapPreviewDataUrl: "",
    layoutGroundColliderExclusions: [],
    mapWorldBounds: { minX: -100, maxX: 100, minZ: -100, maxZ: 100, groundY: 0 },
    mapLighting: defaultMapLighting(),
    editorMapWidthMeters: Math.max(200, Math.min(1200, Math.round(length * 0.34))),
    cardSubtitle: "CUSTOM OFFLINE LAYOUT", cardTheme: "moss"
  };
  for (let i = 0; i < count; i++) state.draft.holes.push(defaultHole(layout && layout.holes ? layout.holes[i] : null, i, count, base.id));
  state.holeIndex = 0; state.inspector = "hole"; state.cardTheme = "moss";
  state.mapWidth = state.draft.editorMapWidthMeters; openEditor();
  toast("Started a custom course from " + base.name + ".");
}
function makeImportedMapCourse() {
  makeCourse("CanonPark");
  state.draft.id = "new-imported-map";
  state.draft.name = "New Imported Map Course";
  state.draft.description = "A custom offline course on an imported map scene.";
  state.draft.sourceMode = "imported";
  state.draft.mapScene = { scenePath: "", bundlePath: "", dependencyBundlePaths: [] };
  state.draft.layoutGroundColliderExclusions = [];
  state.draft.mapWorldBounds = { minX: -100, maxX: 100, minZ: -100, maxZ: 100, groundY: 0 };
  state.draft.mapLighting = defaultMapLighting();
  state.draft.mapPreviewImage = "";
  state.draft.mapPreviewDataUrl = "";
  state.sceneBundleFile = null;
  state.dependencyFiles = [];
  state.draft.holes.forEach(function (hole) {
    hole.mapTeePosition = mapNormToWorld(mapAnchors(hole).tee);
    hole.mapBasketPosition = mapNormToWorld(mapAnchors(hole).basket);
  });
  byId("course-name").value = state.draft.name;
  byId("course-id").value = state.draft.id;
  byId("course-description").value = state.draft.description;
  renderEditor();
  byId("map-setup-details").open = true;
  toast("Created a course that uses an imported map scene.");
}
function normalizeCourse(value) {
  const source = value && typeof value === "object" ? value : {};
  const base = originalCourse(source.baseCourse) || CATALOG[0];
  const layout = (base.layouts || []).find(function (item) { return item.id === source.baseLayout; }) || (base.layouts || [])[0];
  const inputHoles = Array.isArray(source.holes) ? source.holes.slice(0, 18) : [];
  const holes = inputHoles.map(function (hole, index) {
    const fresh = defaultHole(layout && layout.holes ? layout.holes[index] : null, index, inputHoles.length || 1, base.id);
    const item = Object.assign(fresh, hole || {});
    item.teeOffset = Object.assign({ right: 0, up: 0, forward: 0 }, hole && hole.teeOffset || {});
    item.basketOffset = Object.assign({ right: 0, up: 0, forward: 0 }, hole && hole.basketOffset || {});
    item.obZones = Array.isArray(hole && hole.obZones) ? hole.obZones : [];
    mapAnchors(item);
    return item;
  });
  if (!holes.length) holes.push(defaultHole(null, 0, 1, base.id));
  return {
    schemaVersion: 2, id: source.id || slug(base.id + "-custom"), name: source.name || "Untitled Course",
    baseCourse: base.id, baseLayout: source.baseLayout || (layout ? layout.id : ""),
    description: source.description || "", courseCardImage: source.courseCardImage || "", holes: holes,
    sourceMode: source.mapScene || source.sourceMode === "imported" ? "imported" : "original",
    mapScene: source.mapScene || source.sourceMode === "imported" ? Object.assign({ scenePath: "", bundlePath: "", dependencyBundlePaths: [] }, source.mapScene || {}) : null,
    mapPreviewImage: source.mapPreviewImage || "", mapPreviewDataUrl: source.mapPreviewDataUrl || "",
    mapPreviewWorldBounds: source.mapPreviewWorldBounds || null,
    _legacyObPreviewBounds: source.obPreviewRepairBounds || source._legacyObPreviewBounds || null,
    layoutGroundColliderExclusions: Array.isArray(source.layoutGroundColliderExclusions) ? source.layoutGroundColliderExclusions.slice(0, 8) : [],
    mapWorldBounds: Object.assign({ minX: -100, maxX: 100, minZ: -100, maxZ: 100, groundY: 0 }, source.mapWorldBounds || {}),
    mapLighting: Object.assign(defaultMapLighting(), source.mapLighting || {}, {
      ambientColor: Object.assign(defaultMapLighting().ambientColor, source.mapLighting && source.mapLighting.ambientColor || {}),
      keyLightColor: Object.assign(defaultMapLighting().keyLightColor, source.mapLighting && source.mapLighting.keyLightColor || {})
    }),
    editorMapWidthMeters: Number(source.editorMapWidthMeters) || 400,
    cardSubtitle: source.cardSubtitle || "CUSTOM OFFLINE LAYOUT", cardTheme: source.cardTheme || "moss"
  };
}
function adoptGeneratedPreviewBounds(verifiedDataUrl) {
  if (!isImportedMap() || state.draft.mapPreviewWorldBounds || (state.draft.mapPreviewDataUrl && !verifiedDataUrl)) return false;
  const map = state.installedMaps.find(function (item) { return item.id === slug(state.draft.id); });
  if (!map || !map.previewWorldBounds) return false;
  state.draft.mapPreviewWorldBounds = Object.assign({}, map.previewWorldBounds);
  state.draft._legacyObPreviewBounds = Object.assign({}, state.draft.mapWorldBounds);
  return true;
}
function slug(value) {
  return String(value || "").toLowerCase().trim().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 48) || "custom-course";
}
function saveState() {
  if (!state.draft) return;
  state.draft.editorMapWidthMeters = state.mapWidth; state.draft.cardTheme = state.cardTheme;
  try {
    localStorage.setItem("dgm-course-studio-draft", JSON.stringify(state.draft));
    byId("save-state").textContent = "Saved locally";
    byId("editor-save-state").textContent = "Draft saved in this browser";
  } catch (error) { byId("save-state").textContent = "Local workspace"; }
}
function restoreState() {
  try {
    const saved = localStorage.getItem("dgm-course-studio-draft");
    if (!saved) return;
    state.draft = normalizeCourse(JSON.parse(saved));
    state.mapWidth = state.draft.editorMapWidthMeters; state.cardTheme = state.draft.cardTheme;
    state.holeIndex = 0; openEditor();
  } catch (error) { localStorage.removeItem("dgm-course-studio-draft"); }
}
function toast(message) {
  const box = byId("toast"); box.textContent = message; box.classList.add("show");
  clearTimeout(state.toastTimer);
  state.toastTimer = setTimeout(function () { box.classList.remove("show"); }, 2600);
}
function courseInitials(name) {
  return String(name || "").split(/\s+/).filter(Boolean).slice(0, 2).map(function (word) { return word[0]; }).join("").toUpperCase();
}
function findMap(layout) { return layout && (layout.mapImage || layout.cardImage) || ""; }
