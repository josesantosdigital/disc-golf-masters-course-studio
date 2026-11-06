"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

test("editing imported map settings serializes terrain and vegetation options", function () {
  const controls = {
    "map-force-unlit": { checked: true }, "map-realtime-shadows": { checked: true },
    "map-hide-particles": { checked: true }, "map-tree-canopy-collisions": { checked: true },
    "map-foliage-interactions": { checked: true },
    "map-game-foliage": { checked: true }, "map-lit-foliage": { checked: true }, "map-base-sun": { checked: true },
    "map-preserve-lightmaps": { checked: false }, "map-repair-terrain-materials": { checked: true },
    "map-repair-terrain-details": { checked: true }, "map-force-tree-mesh-lods": { checked: true },
    "map-albedo-brightness": { value: "1.2" }, "map-local-light-multiplier": { value: "0.2" }, "map-ambient-color": { value: "#ffffff" },
    "map-ambient-intensity": { value: "1" }, "map-key-color": { value: "#ffffff" },
    "map-key-intensity": { value: "2" }, "map-key-yaw": { value: "-30" }, "map-key-pitch": { value: "50" }
  };
  let saves = 0;
  const state = { draft: { mapLighting: { forceTreeMeshLods: true, repairTerrainMaterials: true, preserveLightmaps: false } } };
  const context = vm.createContext({ state, byId: id => controls[id], clamp: (n, min, max) => Math.min(max, Math.max(min, n)), saveState: () => saves++, defaultMapLighting: () => ({}) });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../CourseEditor/app-settings.js"), "utf8"), context);
  vm.runInContext("setImportedMapLighting()", context);
  assert.equal(state.draft.mapLighting.forceTreeMeshLods, true);
  assert.equal(state.draft.mapLighting.repairTerrainMaterials, true);
  assert.equal(state.draft.mapLighting.preserveLightmaps, false);
  assert.equal(state.draft.mapLighting.generateTreeCanopyColliders, true);
  assert.equal(state.draft.mapLighting.experimentalFoliageInteractions, true);
  assert.equal(state.draft.mapLighting.useGameFoliageShader, true);
  assert.equal(state.draft.mapLighting.useLitFoliageMaterials, true);
  assert.equal(state.draft.mapLighting.preferBaseCourseSun, true);
  assert.equal(state.draft.mapLighting.preserveLightmaps, false);
  assert.equal(state.draft.mapLighting.repairTerrainMaterials, true);
  assert.equal(state.draft.mapLighting.repairTerrainTextureDetails, true);
  assert.equal(state.draft.mapLighting.forceTreeMeshLods, true);
  assert.equal(state.draft.mapLighting.albedoBrightness, 1.2);
  assert.equal(state.draft.mapLighting.localLightIntensityMultiplier, 0.2);
  assert.equal(saves, 1);
});

test("terrain and vegetation controls are visible and wired through the editor", function () {
  const ids = [
    ["map-lit-foliage", "useLitFoliageMaterials"],
    ["map-preserve-lightmaps", "preserveLightmaps"],
    ["map-repair-terrain-materials", "repairTerrainMaterials"],
    ["map-repair-terrain-details", "repairTerrainTextureDetails"],
    ["map-force-tree-mesh-lods", "forceTreeMeshLods"]
  ];
  const html = fs.readFileSync(path.join(__dirname, "../CourseEditor/index.html"), "utf8");
  const inspector = fs.readFileSync(path.join(__dirname, "../CourseEditor/app-inspector.js"), "utf8");
  const settings = fs.readFileSync(path.join(__dirname, "../CourseEditor/app-settings.js"), "utf8");
  const events = fs.readFileSync(path.join(__dirname, "../CourseEditor/app-events.js"), "utf8");
  assert.match(html, /<summary>Terrain &amp; vegetation<\/summary>/);
  for (const [id, property] of ids) {
    assert.equal((html.match(new RegExp(`id=\"${id}\"`, "g")) || []).length, 1, `${id} should appear once`);
    assert.ok(inspector.includes(`byId(\"${id}\").checked = lighting.${property} === true;`), `${id} should load from course data`);
    assert.ok(settings.includes(`${property}: byId(\"${id}\").checked,`), `${id} should save to course data`);
    assert.ok(events.includes(`\"${id}\"`), `${id} should have a change handler`);
  }
  assert.ok(html.includes('id=\"map-local-light-multiplier\"'));
  assert.ok(inspector.includes('byId(\"map-local-light-multiplier\").value = lighting.localLightIntensityMultiplier;'));
  assert.ok(settings.includes('localLightIntensityMultiplier: clamp(Number(byId(\"map-local-light-multiplier\").value), 0, 5),'));
  assert.ok(events.includes('map-local-light-multiplier'));
});
