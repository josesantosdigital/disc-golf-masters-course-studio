"use strict";

const fs = require("node:fs");
const path = require("node:path");

async function listInstalledMaps(options) {
  const pluginRoot = path.resolve(options.pluginRoot);
  const courseFiles = path.resolve(options.courseFiles);
  const mapsRoot = path.resolve(options.mapsRoot);
  const editorRoot = path.resolve(options.editorRoot || __dirname);
  let courseEntries = [];
  try { courseEntries = await fs.promises.readdir(courseFiles, { withFileTypes: true }); } catch (error) { }

  const maps = [];
  const referencedSceneBundles = new Set();
  const referencedDependencies = new Set();
  const courseFilesByName = courseEntries.filter(function (entry) {
    return entry.isFile() && path.extname(entry.name).toLowerCase() === ".json";
  });

  for (const entry of courseFilesByName) {
    try {
      const course = JSON.parse(await fs.promises.readFile(path.join(courseFiles, entry.name), "utf8"));
      const scene = course && course.mapScene;
      if (!scene || typeof scene !== "object" || Array.isArray(scene)) continue;
      const courseId = safeCourseId(course && course.id) || safeCourseId(entry.name.replace(/\.json$/i, "")) || "installed-map";
      const scenePath = typeof scene.scenePath === "string" ? scene.scenePath : "";
      const scenePathValid = isValidScenePath(scenePath);
      const bundlePath = normalizePluginPath(scene.bundlePath, pluginRoot);
      const holes = Array.isArray(course.holes) ? course.holes : [];
      if (bundlePath) referencedSceneBundles.add(bundlePath.toLowerCase());
      const dependencies = Array.isArray(scene.dependencyBundlePaths)
        ? scene.dependencyBundlePaths.map(function (dependency) {
          const normalized = normalizePluginPath(dependency, pluginRoot);
          if (normalized) referencedDependencies.add(normalized.toLowerCase());
          return {
            path: normalized || (typeof dependency === "string" && dependency.trim() ? "Invalid path reference" : ""),
            exists: !!(normalized && fs.existsSync(path.resolve(pluginRoot, normalized)))
          };
        }).filter(function (dependency) { return dependency.path; }) : [];

      const customPreview = safePngName(course.mapPreviewImage);
      const customPreviewPath = customPreview && path.join(courseFiles, customPreview);
      const generatedPreview = "imported-" + courseId + ".png";
      const generatedPreviewPath = path.join(editorRoot, "assets", "maps", generatedPreview);
      const previewUrl = customPreviewPath && fs.existsSync(customPreviewPath)
        ? "/course-files/" + encodeURIComponent(customPreview)
        : fs.existsSync(generatedPreviewPath) ? "/assets/maps/" + encodeURIComponent(generatedPreview) : "";
      const bundleAbsolutePath = bundlePath ? path.resolve(pluginRoot, bundlePath) : "";
      maps.push({
        id: courseId,
        name: typeof course.name === "string" && course.name.trim() ? course.name : humanize(courseId),
        description: typeof course.description === "string" ? course.description : "",
        courseFileName: entry.name,
        bundlePath: bundlePath,
        bundleExists: !!(bundleAbsolutePath && fs.existsSync(bundleAbsolutePath)),
        scenePath: scenePath,
        scenePathValid: scenePathValid,
        dependencies: dependencies,
        holeCount: holes.length,
        autoLayout: typeof course.autoLayout === "string" ? course.autoLayout : "",
        explicitPositionHoleCount: holes.filter(function (hole) { return hole && hole.mapTeePosition && hole.mapBasketPosition; }).length,
        worldBoundsStatus: getWorldBoundsStatus(course.mapWorldBounds),
        baseCourse: typeof course.baseCourse === "string" ? course.baseCourse : "CanonPark",
        previewUrl: previewUrl,
        configured: !!(scenePathValid && bundlePath)
      });
    } catch (error) {
      // Ignore malformed course JSON and keep scanning the other installed packages.
    }
  }

  const bundles = await findBundles(mapsRoot, pluginRoot);
  for (const bundlePath of bundles) {
    const key = bundlePath.toLowerCase();
    if (referencedSceneBundles.has(key) || referencedDependencies.has(key)) continue;
    const absolutePath = path.resolve(pluginRoot, bundlePath);
    const relativeInsideMaps = bundlePath.slice("Maps/".length);
    const directoryName = path.basename(path.dirname(relativeInsideMaps));
    const fileBase = path.basename(relativeInsideMaps, path.extname(relativeInsideMaps));
    const id = safeCourseId(directoryName) || safeCourseId(fileBase) || "installed-map";
    maps.push({
      id: id,
      name: humanize(directoryName !== "Maps" ? directoryName : fileBase),
      description: "Installed scene bundle. Add its .unity scene path to create a playable course.",
      courseFileName: "",
      bundlePath: bundlePath,
      bundleExists: fs.existsSync(absolutePath),
      scenePath: "",
      scenePathValid: false,
      dependencies: [],
      holeCount: 0,
      autoLayout: "",
      explicitPositionHoleCount: 0,
      worldBoundsStatus: "missing",
      baseCourse: "CanonPark",
      previewUrl: "",
      configured: false
    });
  }

  return maps.sort(function (left, right) { return left.name.localeCompare(right.name); });
}

async function findBundles(directory, pluginRoot) {
  let entries;
  try { entries = await fs.promises.readdir(directory, { withFileTypes: true }); } catch (error) { return []; }
  const bundles = [];
  for (const entry of entries) {
    if (entry.isSymbolicLink()) continue;
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (/^(?:dependencies|deps)$/i.test(entry.name)) continue;
      bundles.push.apply(bundles, await findBundles(absolutePath, pluginRoot));
      continue;
    }
    if (!entry.isFile() || ![".bundle", ".unity3d", ".assetbundle"].includes(path.extname(entry.name).toLowerCase())) continue;
    const relative = path.relative(pluginRoot, absolutePath).split(path.sep).join("/");
    const normalized = normalizePluginPath(relative, pluginRoot);
    if (normalized) bundles.push(normalized);
  }
  return bundles;
}

function normalizePluginPath(value, pluginRoot) {
  if (typeof value !== "string") return "";
  const normalized = value.trim().replace(/\\/g, "/");
  if (!normalized || /^(?:[A-Za-z]:|\/)/.test(normalized)) return "";
  const parts = normalized.split("/");
  if (parts.some(function (part) { return !part || part === "." || part === ".."; })) return "";
  const resolved = path.resolve(pluginRoot, normalized.split("/").join(path.sep));
  if (!resolved.startsWith(pluginRoot + path.sep)) return "";
  return normalized;
}

function safeCourseId(value) {
  return typeof value === "string" && /^[a-z0-9_-]{1,48}$/i.test(value) ? value.toLowerCase() : "";
}

function isValidScenePath(value) {
  if (typeof value !== "string") return false;
  const normalized = value.trim().replace(/\\/g, "/");
  if (!normalized || /^(?:[A-Za-z]:|\/)/.test(normalized) || !/\.unity$/i.test(normalized)) return false;
  return normalized.split("/").every(function (part) { return part && part !== "." && part !== ".."; });
}

function safePngName(value) {
  return typeof value === "string" && path.basename(value) === value && path.extname(value).toLowerCase() === ".png" ? value : "";
}

function getWorldBoundsStatus(bounds) {
  if (bounds == null) return "missing";
  if (typeof bounds !== "object" || Array.isArray(bounds)) return "invalid";
  const minX = bounds.minX === undefined ? -100 : bounds.minX;
  const maxX = bounds.maxX === undefined ? 100 : bounds.maxX;
  const minZ = bounds.minZ === undefined ? -100 : bounds.minZ;
  const maxZ = bounds.maxZ === undefined ? 100 : bounds.maxZ;
  const groundY = bounds.groundY === undefined ? 0 : bounds.groundY;
  const values = [minX, maxX, minZ, maxZ, groundY];
  if (!values.every(function (value) { return typeof value === "number" && Number.isFinite(value) && Math.abs(value) <= 10000; }) ||
      maxX <= minX || maxZ <= minZ) return "invalid";
  if (minX === -100 && maxX === 100 && minZ === -100 && maxZ === 100 && groundY === 0) return "default";
  return "valid";
}

function humanize(value) {
  return String(value || "Installed map").replace(/\.[^.]+$/, "").replace(/[-_]+/g, " ").replace(/\b\w/g, function (letter) { return letter.toUpperCase(); });
}

module.exports = { listInstalledMaps: listInstalledMaps };
