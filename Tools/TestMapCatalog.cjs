"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const test = require("node:test");
const { listInstalledMaps, deleteInstalledMap } = require("../CourseEditor/map-catalog");
const authoringWarnings = vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../CourseEditor/app-library.js"), "utf8") + "\ninstalledMapAuthoringWarnings", {});

test("map catalog lists configured, orphaned, and repairable map packages", async function () {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dgm-map-catalog-"));
  try {
    const pluginRoot = path.join(tempRoot, "CustomCourseLoader");
    const courseFiles = path.join(pluginRoot, "Courses");
    const mapsRoot = path.join(pluginRoot, "Maps");
    const editorRoot = path.join(pluginRoot, "CourseEditor");
    const sceneBundle = path.join(mapsRoot, "valid", "scene.bundle");
    const dependencyBundle = path.join(mapsRoot, "valid", "dependencies", "shared.bundle");
    const orphanBundle = path.join(mapsRoot, "loose", "Bare.assetbundle");
    fs.mkdirSync(courseFiles, { recursive: true });
    fs.mkdirSync(path.dirname(sceneBundle), { recursive: true });
    fs.mkdirSync(path.dirname(dependencyBundle), { recursive: true });
    fs.mkdirSync(path.dirname(orphanBundle), { recursive: true });
    fs.writeFileSync(sceneBundle, "UnityFS fixture");
    fs.writeFileSync(dependencyBundle, "UnityFS fixture");
    fs.writeFileSync(orphanBundle, "UnityFS fixture");

    fs.writeFileSync(path.join(courseFiles, "valid.json"), JSON.stringify({
      id: "valid-course", name: "Valid Course", holes: [{ mapTeePosition: { x: 1 }, mapBasketPosition: { x: 2 } }, {}],
      mapWorldBounds: { minX: -200, maxX: 300, minZ: -150, maxZ: 175, groundY: 2 },
      mapScene: {
        scenePath: "Assets/Scenes/Valid.unity",
        bundlePath: "Maps/valid/scene.bundle",
        dependencyBundlePaths: ["Maps/valid/dependencies/shared.bundle"]
      }
    }));
    fs.writeFileSync(path.join(courseFiles, "broken.json"), JSON.stringify({
      id: "broken-course", name: "Repair This Course", autoLayout: "long", holes: [{}, {}],
      mapScene: {
        scenePath: "../invalid.unity",
        bundlePath: "../outside.bundle",
        dependencyBundlePaths: ["Maps/valid/missing.bundle"]
      }
    }));
    fs.writeFileSync(path.join(courseFiles, "default-bounds.json"), JSON.stringify({
      id: "default-bounds", name: "Default Bounds Course", holes: [], mapWorldBounds: {},
      mapScene: {
        scenePath: "Assets/Scenes/Valid.unity",
        bundlePath: "Maps/valid/scene.bundle",
        dependencyBundlePaths: []
      }
    }));
    fs.writeFileSync(path.join(courseFiles, "malformed.json"), "{ invalid json");

    const maps = await listInstalledMaps({ pluginRoot: pluginRoot, courseFiles: courseFiles, mapsRoot: mapsRoot, editorRoot: editorRoot });
    assert.deepEqual(maps.map(function (map) { return map.name; }), ["Default Bounds Course", "Loose", "Repair This Course", "Valid Course"]);

    const broken = maps.find(function (map) { return map.id === "broken-course"; });
    assert.ok(broken, "a course with invalid map paths remains visible for repair");
    assert.equal(broken.bundlePath, "");
    assert.equal(broken.bundleExists, false);
    assert.equal(broken.scenePathValid, false);
    assert.equal(broken.configured, false);
    assert.equal(broken.dependencies[0].exists, false);
    assert.equal(broken.worldBoundsStatus, "missing");
    assert.equal(broken.explicitPositionHoleCount, 0);
    assert.deepEqual(JSON.parse(JSON.stringify(authoringWarnings(broken))), [
      "Map overview uses the default ±100 m bounds; calibrate it to show the full scene.",
      "Hole positions are planned at runtime; review every tee and basket."
    ]);

    const valid = maps.find(function (map) { return map.id === "valid-course"; });
    assert.equal(valid.configured, true);
    assert.equal(valid.bundleExists, true);
    assert.equal(valid.dependencies[0].exists, true);
    assert.equal(valid.holeCount, 2);
    assert.equal(valid.worldBoundsStatus, "valid");
    assert.equal(valid.explicitPositionHoleCount, 1);
    assert.deepEqual(JSON.parse(JSON.stringify(authoringWarnings(valid))), [
      "1/2 holes have tee/basket positions; every hole needs both."
    ]);

    const defaultBounds = maps.find(function (map) { return map.id === "default-bounds"; });
    assert.equal(defaultBounds.worldBoundsStatus, "default");
    assert.deepEqual(JSON.parse(JSON.stringify(authoringWarnings(defaultBounds))), [
      "Map overview uses the default ±100 m bounds; calibrate it to show the full scene.",
      "No holes are defined yet; add holes before playing."
    ]);

    const orphan = maps.find(function (map) { return map.name === "Loose"; });
    assert.equal(orphan.bundlePath, "Maps/loose/Bare.assetbundle");
    assert.equal(orphan.configured, false);
    assert.equal(maps.some(function (map) { return map.bundlePath.endsWith("shared.bundle"); }), false,
      "dependency bundles are not shown as standalone maps");
  } finally {
    const tempBase = path.resolve(os.tmpdir()) + path.sep;
    const cleanupTarget = path.resolve(tempRoot);
    if (!cleanupTarget.startsWith(tempBase)) throw new Error("Refusing to remove a path outside the system temp directory.");
    fs.rmSync(cleanupTarget, { recursive: true, force: true });
  }
});

test("map deletion removes only unshared course files and bundles", async function () {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dgm-map-delete-"));
  try {
    const pluginRoot = path.join(tempRoot, "CustomCourseLoader");
    const courseFiles = path.join(pluginRoot, "Courses");
    const mapsRoot = path.join(pluginRoot, "Maps");
    const editorRoot = path.join(pluginRoot, "CourseEditor");
    const selectedScene = path.join(mapsRoot, "one", "scene.bundle");
    const sharedBundle = path.join(mapsRoot, "shared", "shared.bundle");
    const otherScene = path.join(mapsRoot, "two", "scene.bundle");
    fs.mkdirSync(courseFiles, { recursive: true });
    fs.mkdirSync(path.dirname(selectedScene), { recursive: true });
    fs.mkdirSync(path.dirname(sharedBundle), { recursive: true });
    fs.mkdirSync(path.dirname(otherScene), { recursive: true });
    fs.mkdirSync(path.join(editorRoot, "assets", "maps"), { recursive: true });
    for (const file of [selectedScene, sharedBundle, otherScene]) fs.writeFileSync(file, "UnityFS fixture");

    const selected = {
      id: "map-one", name: "Map One", courseCardImage: "map-one-card.png", mapPreviewImage: "map-one-preview.png",
      holes: [{ cardImage: "map-one-hole.png" }],
      mapScene: { scenePath: "Assets/Maps/One.unity", bundlePath: "Maps/one/scene.bundle", dependencyBundlePaths: ["Maps/shared/shared.bundle"] }
    };
    const other = {
      id: "map-two", name: "Map Two", courseCardImage: "shared-card.png", holes: [],
      mapScene: { scenePath: "Assets/Maps/Two.unity", bundlePath: "Maps/two/scene.bundle", dependencyBundlePaths: ["Maps/shared/shared.bundle"] }
    };
    fs.writeFileSync(path.join(courseFiles, "map-one.json"), JSON.stringify(selected));
    fs.writeFileSync(path.join(courseFiles, "map-two.json"), JSON.stringify(other));
    for (const name of ["map-one-card.png", "map-one-preview.png", "map-one-hole.png", "shared-card.png"]) {
      fs.writeFileSync(path.join(courseFiles, name), "image fixture");
    }
    const generatedPreview = path.join(editorRoot, "assets", "maps", "imported-map-one.png");
    fs.writeFileSync(generatedPreview, "generated preview fixture");

    const result = await deleteInstalledMap({ pluginRoot, courseFiles, mapsRoot, editorRoot }, {
      courseFileName: "map-one.json", bundlePath: "Maps/one/scene.bundle"
    });

    assert.equal(result.name, "Map One");
    assert.ok(result.deletedFiles.includes("Maps/one/scene.bundle"));
    assert.ok(result.deletedFiles.includes("Courses/map-one.json"));
    assert.deepEqual(result.keptSharedBundles, ["Maps/shared/shared.bundle"]);
    for (const file of [selectedScene, path.join(courseFiles, "map-one.json"), path.join(courseFiles, "map-one-card.png"),
      path.join(courseFiles, "map-one-preview.png"), path.join(courseFiles, "map-one-hole.png"), generatedPreview]) {
      assert.equal(fs.existsSync(file), false, "selected map file should be removed: " + file);
    }
    for (const file of [sharedBundle, otherScene, path.join(courseFiles, "map-two.json"),
      path.join(courseFiles, "shared-card.png")]) {
      assert.equal(fs.existsSync(file), true, "other/shared map file should be preserved: " + file);
    }
  } finally {
    const tempBase = path.resolve(os.tmpdir()) + path.sep;
    const cleanupTarget = path.resolve(tempRoot);
    if (!cleanupTarget.startsWith(tempBase)) throw new Error("Refusing to remove a path outside the system temp directory.");
    fs.rmSync(cleanupTarget, { recursive: true, force: true });
  }
});

test("map deletion refuses a managed root reported as a symlink", async function () {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "dgm-map-delete-link-"));
  try {
    const pluginRoot = path.join(tempRoot, "CustomCourseLoader");
    const courseFiles = path.join(pluginRoot, "Courses");
    const mapsRoot = path.join(pluginRoot, "Maps");
    const editorRoot = path.join(pluginRoot, "CourseEditor");
    const outsideCourses = path.join(tempRoot, "outside-courses");
    const outsideCourse = path.join(outsideCourses, "map-one.json");
    const sceneBundle = path.join(mapsRoot, "one", "scene.bundle");
    fs.mkdirSync(pluginRoot, { recursive: true });
    fs.mkdirSync(outsideCourses, { recursive: true });
    fs.mkdirSync(path.dirname(sceneBundle), { recursive: true });
    fs.mkdirSync(editorRoot, { recursive: true });
    fs.writeFileSync(outsideCourse, JSON.stringify({
      id: "map-one", name: "Map One", holes: [],
      mapScene: { scenePath: "Assets/One.unity", bundlePath: "Maps/one/scene.bundle" }
    }));
    fs.writeFileSync(sceneBundle, "UnityFS fixture");
    const originalLstat = fs.promises.lstat;
    fs.promises.lstat = async function (target, ...args) {
      if (path.resolve(String(target)) === path.resolve(courseFiles)) {
        return { isSymbolicLink: function () { return true; }, isDirectory: function () { return false; } };
      }
      return originalLstat.call(fs.promises, target, ...args);
    };
    try {
      await assert.rejects(deleteInstalledMap({ pluginRoot, courseFiles, mapsRoot, editorRoot }, {
        courseFileName: "map-one.json", bundlePath: "Maps/one/scene.bundle"
      }), /managed folder is linked/);
    } finally {
      fs.promises.lstat = originalLstat;
    }
    assert.equal(fs.existsSync(outsideCourse), true, "the course outside the plugin root must remain untouched");
    assert.equal(fs.existsSync(sceneBundle), true, "refusing the linked root must happen before any bundle deletion");
  } finally {
    const tempBase = path.resolve(os.tmpdir()) + path.sep;
    const cleanupTarget = path.resolve(tempRoot);
    if (!cleanupTarget.startsWith(tempBase)) throw new Error("Refusing to remove a path outside the system temp directory.");
    fs.rmSync(cleanupTarget, { recursive: true, force: true });
  }
});
