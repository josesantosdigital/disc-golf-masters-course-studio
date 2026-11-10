"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

test("generated overview coordinates match its padded world bounds and legacy OB drawings can be repaired", function () {
  const old = { minX: -165.6, maxX: 25.7, minZ: -90.8, maxZ: 24.4, groundY: 43.1 };
  const preview = { minX: -171.339, maxX: 31.439, minZ: -96.568125, maxZ: 30.168125 };
  const tee = { x: -130.2, y: 43.1, z: -8.7 }, basket = { x: 7.5, y: 43.1, z: -6.7 };
  const points = [{ right: 5.4, forward: -0.9 }, { right: 11.5, forward: 147.2 }, { right: 74.7, forward: 146.5 }, { right: 71.7, forward: -3.9 }];
  const hole = { mapTeePosition: tee, mapBasketPosition: basket, teeYawDegrees: 0, obZones: [{ points }] };
  const state = { image: null, mapWidth: 400, draft: { sourceMode: "imported", mapWorldBounds: old, mapPreviewWorldBounds: preview, _legacyObPreviewBounds: old, holes: [hole] } };
  let saved = 0;
  const context = vm.createContext({ state, isImportedMap: () => true, clamp: (n, lo, hi) => Math.min(hi, Math.max(lo, n)),
    round1: n => Math.round(n * 10) / 10, mapAnchors: () => ({ heading: 0 }), renderEditor: () => {}, saveState: () => saved++, toast: () => {} });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../CourseEditor/app-map.js"), "utf8"), context);
  const centre = vm.runInContext("mapNormToWorld({x: 0.5, y: 0.5})", context);
  assert.ok(Math.abs(centre.x - (-69.95)) <= 0.1);
  assert.ok(Math.abs(centre.z - (-33.2)) <= 0.1);
  assert.equal(centre.y, 43.1, "preview bounds must not alter the course ground height");
  const oldHeading = Math.atan2(basket.z - tee.z, basket.x - tee.x);
  const p = points[0], x = tee.x + Math.sin(oldHeading) * p.right + Math.cos(oldHeading) * p.forward;
  const z = tee.z - Math.cos(oldHeading) * p.right + Math.sin(oldHeading) * p.forward;
  const originalImagePoint = { x: (x - old.minX) / (old.maxX - old.minX), y: (old.maxZ - z) / (old.maxZ - old.minZ) };
  vm.runInContext("realignLegacyObZones()", context);
  const repaired = hole.obZones[0].points[0];
  const newX = tee.x + Math.sin(oldHeading) * repaired.right + Math.cos(oldHeading) * repaired.forward;
  const newZ = tee.z - Math.cos(oldHeading) * repaired.right + Math.sin(oldHeading) * repaired.forward;
  assert.ok(Math.abs((newX - preview.minX) / (preview.maxX - preview.minX) - originalImagePoint.x) < 0.001);
  assert.ok(Math.abs((preview.maxZ - newZ) / (preview.maxZ - preview.minZ) - originalImagePoint.y) < 0.001);
  const repairedWorld = hole.obZones[0].points.map(point => ({
    x: tee.x + Math.sin(oldHeading) * point.right + Math.cos(oldHeading) * point.forward,
    z: tee.z - Math.cos(oldHeading) * point.right + Math.sin(oldHeading) * point.forward
  }));
  const lie = { x: -132.9, z: -73 };
  const inside = repairedWorld.reduce((inPolygon, point, index, polygon) => {
    const previous = polygon[(index + polygon.length - 1) % polygon.length];
    return ((point.z > lie.z) !== (previous.z > lie.z) &&
      lie.x < (previous.x - point.x) * (lie.z - point.z) / (previous.z - point.z) + point.x)
      ? !inPolygon : inPolygon;
  }, false);
  assert.equal(inside, true, "the reported landing is inside the repaired zone");
  assert.deepEqual(tee, { x: -130.2, y: 43.1, z: -8.7 }, "repair leaves the tee untouched");
  assert.deepEqual(basket, { x: 7.5, y: 43.1, z: -6.7 }, "repair leaves the basket untouched");
  assert.equal(state.draft._legacyObPreviewBounds, null);
  assert.equal(saved, 1);
});
