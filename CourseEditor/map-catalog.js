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

async function deleteInstalledMap(options, selection) {
  const pluginRoot = path.resolve(options.pluginRoot);
  const courseFiles = path.resolve(options.courseFiles);
  const mapsRoot = path.resolve(options.mapsRoot);
  const editorRoot = path.resolve(options.editorRoot || __dirname);
  const courseFileName = selection && typeof selection.courseFileName === "string" ? selection.courseFileName : "";
  const selectedBundlePath = selection && typeof selection.bundlePath === "string"
    ? normalizePluginPath(selection.bundlePath, pluginRoot) : "";
  if (!courseFileName && !selectedBundlePath) throw mapError(400, "Choose an installed map package to delete.");
  if (courseFileName && (path.basename(courseFileName) !== courseFileName || path.extname(courseFileName).toLowerCase() !== ".json")) {
    throw mapError(400, "The selected course file name is invalid.");
  }

  await assertSafeManagedDirectory(pluginRoot);
  await assertSafeManagedDirectory(courseFiles);
  await assertSafeManagedDirectory(mapsRoot, true);
  await assertSafeManagedDirectory(editorRoot, true);

  const maps = await listInstalledMaps(options);
  const matches = maps.filter(function (map) {
    if (courseFileName) return map.courseFileName === courseFileName && (!selectedBundlePath || map.bundlePath === selectedBundlePath);
    return !map.courseFileName && map.bundlePath === selectedBundlePath;
  });
  if (!matches.length) throw mapError(404, "That installed map is no longer present. Scan maps and try again.");
  if (matches.length > 1) throw mapError(409, "The selected map is ambiguous. Refresh the Map Explorer before deleting it.");

  const map = matches[0];
  const coursePath = courseFileName ? path.resolve(courseFiles, courseFileName) : "";
  let course = null;
  if (coursePath) {
    await assertSafeFileTarget(courseFiles, coursePath);
    try { course = JSON.parse(await fs.promises.readFile(coursePath, "utf8")); }
    catch (error) { throw mapError(409, "Could not safely read the selected course file; nothing was deleted."); }
  }

  const otherCourses = await readOtherCourseFiles(courseFiles, courseFileName);
  const otherBundleRefs = new Set();
  const otherImageRefs = new Set();
  const otherIds = new Set();
  for (const item of otherCourses) {
    const id = safeCourseId(item.course && item.course.id);
    if (id) otherIds.add(id);
    const scene = item.course && item.course.mapScene;
    if (scene && typeof scene === "object") {
      const sceneBundle = normalizePluginPath(scene.bundlePath, pluginRoot);
      if (sceneBundle) otherBundleRefs.add(sceneBundle.toLowerCase());
      if (Array.isArray(scene.dependencyBundlePaths)) {
        for (const dependency of scene.dependencyBundlePaths) {
          const normalized = normalizePluginPath(dependency, pluginRoot);
          if (normalized) otherBundleRefs.add(normalized.toLowerCase());
        }
      }
    }
    collectCourseImageNames(item.course).forEach(function (name) { otherImageRefs.add(name.toLowerCase()); });
  }

  const bundlePaths = new Set();
  if (map.bundlePath) bundlePaths.add(map.bundlePath);
  (map.dependencies || []).forEach(function (dependency) {
    const normalized = normalizePluginPath(dependency.path, pluginRoot);
    if (normalized) bundlePaths.add(normalized);
  });

  const filesToDelete = new Map();
  const sharedBundles = [];
  for (const bundlePath of bundlePaths) {
    const normalized = normalizePluginPath(bundlePath, pluginRoot);
    if (!normalized || ![".bundle", ".unity3d", ".assetbundle"].includes(path.extname(normalized).toLowerCase())) continue;
    const absolute = path.resolve(pluginRoot, normalized.split("/").join(path.sep));
    if (!isPathInside(mapsRoot, absolute)) continue;
    if (otherBundleRefs.has(normalized.toLowerCase())) {
      sharedBundles.push(normalized);
      continue;
    }
    filesToDelete.set(absolute.toLowerCase(), { path: absolute, root: mapsRoot, kind: "bundle" });
  }

  for (const imageName of collectCourseImageNames(course)) {
    if (otherImageRefs.has(imageName.toLowerCase())) continue;
    const absolute = path.resolve(courseFiles, imageName);
    filesToDelete.set(absolute.toLowerCase(), { path: absolute, root: courseFiles, kind: "image" });
  }

  const mapId = safeCourseId(map.id);
  if (mapId && !otherIds.has(mapId)) {
    const generatedPreview = path.resolve(editorRoot, "assets", "maps", "imported-" + mapId + ".png");
    filesToDelete.set(generatedPreview.toLowerCase(), { path: generatedPreview, root: editorRoot, kind: "image" });
  }
  if (coursePath) filesToDelete.set(coursePath.toLowerCase(), { path: coursePath, root: courseFiles, kind: "course" });

  const targets = [];
  for (const target of filesToDelete.values()) {
    const exists = await assertSafeFileTarget(target.root, target.path, true);
    if (exists) targets.push(target);
  }

  const deleted = [];
  // Remove the course definition last, so failed bundle cleanup leaves the map visible for recovery.
  targets.sort(function (left, right) {
    const order = { bundle: 0, image: 1, course: 2 };
    return order[left.kind] - order[right.kind];
  });
  for (const target of targets) {
    await fs.promises.unlink(target.path);
    deleted.push(path.relative(pluginRoot, target.path).split(path.sep).join("/"));
  }
  for (const target of targets) if (target.kind === "bundle") await pruneEmptyMapDirectories(path.dirname(target.path), mapsRoot);

  return {
    name: map.name,
    deletedFiles: deleted,
    keptSharedBundles: sharedBundles
  };
}

async function readOtherCourseFiles(courseFiles, excludedFileName) {
  let entries;
  try { entries = await fs.promises.readdir(courseFiles, { withFileTypes: true }); }
  catch (error) { throw mapError(500, "Could not safely inspect the Courses folder."); }
  const results = [];
  for (const entry of entries) {
    if (!entry.isFile() || path.extname(entry.name).toLowerCase() !== ".json" ||
        (excludedFileName && entry.name.toLowerCase() === excludedFileName.toLowerCase())) continue;
    try {
      const contents = await fs.promises.readFile(path.join(courseFiles, entry.name), "utf8");
      results.push({ fileName: entry.name, course: JSON.parse(contents) });
    } catch (error) {
      throw mapError(409, "Could not safely check whether another course shares these map files. Fix the malformed course JSON and retry.");
    }
  }
  return results;
}

function collectCourseImageNames(course) {
  if (!course || typeof course !== "object") return [];
  const names = [course.courseCardImage, course.mapPreviewImage];
  if (Array.isArray(course.holes)) course.holes.forEach(function (hole) { if (hole) names.push(hole.cardImage); });
  return Array.from(new Set(names.filter(function (name) {
    return typeof name === "string" && path.basename(name) === name && path.extname(name).toLowerCase() === ".png";
  })));
}

async function assertSafeFileTarget(root, target, allowMissing) {
  if (!await assertSafeManagedDirectory(root, allowMissing)) return false;
  const relative = path.relative(root, target);
  if (!relative || relative === ".." || relative.startsWith(".." + path.sep) || path.isAbsolute(relative)) {
    throw mapError(400, "A selected map file points outside its managed folder.");
  }
  let current = root;
  for (const part of relative.split(path.sep)) {
    current = path.join(current, part);
    let stat;
    try { stat = await fs.promises.lstat(current); }
    catch (error) {
      if (allowMissing && error.code === "ENOENT") return false;
      if (error.code === "ENOENT") throw mapError(404, "A selected map file is missing.");
      throw error;
    }
    if (stat.isSymbolicLink()) throw mapError(409, "A selected map path is linked; deletion was refused for safety.");
    if (current !== target && !stat.isDirectory()) throw mapError(409, "A selected map path contains a non-folder component.");
    if (current === target && !stat.isFile()) throw mapError(409, "A selected map path is not a regular file.");
  }
  return true;
}

async function assertSafeManagedDirectory(root, allowMissing) {
  let stat;
  try { stat = await fs.promises.lstat(root); }
  catch (error) {
    if (allowMissing && error.code === "ENOENT") return false;
    if (error.code === "ENOENT") throw mapError(404, "A selected map's managed folder is missing.");
    throw error;
  }
  if (stat.isSymbolicLink() || !stat.isDirectory()) {
    throw mapError(409, "A selected map's managed folder is linked or is not a directory; deletion was refused for safety.");
  }
  return true;
}

async function pruneEmptyMapDirectories(directory, mapsRoot) {
  let current = directory;
  while (isPathInside(mapsRoot, current) && path.resolve(current).toLowerCase() !== path.resolve(mapsRoot).toLowerCase()) {
    try { await fs.promises.rmdir(current); }
    catch (error) {
      if (error.code === "ENOENT") { current = path.dirname(current); continue; }
      if (error.code === "ENOTEMPTY" || error.code === "EEXIST") return;
      throw error;
    }
    current = path.dirname(current);
  }
}

function isPathInside(root, target) {
  const relative = path.relative(root, target);
  return !!relative && relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
}

function mapError(statusCode, message) {
  const error = new Error(message);
  error.statusCode = statusCode;
  return error;
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

module.exports = { listInstalledMaps: listInstalledMaps, deleteInstalledMap: deleteInstalledMap };
