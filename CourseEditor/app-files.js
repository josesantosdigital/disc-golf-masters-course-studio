"use strict";

function exportDocument() {
  const draft = state.draft, cleanId = slug(draft.id), images = [];
  draft.id = cleanId; draft.editorMapWidthMeters = state.mapWidth;
  const imported = isImportedMap();
  if (imported) {
    if (!draft.mapScene || !String(draft.mapScene.scenePath || "").toLowerCase().endsWith(".unity")) throw new Error("Enter the .unity scene path stored inside the selected bundle.");
    if (!validRelativePackagePath(draft.mapScene.bundlePath)) throw new Error("Set a safe relative bundle path such as Maps/" + cleanId + "/scene.bundle.");
    if ((draft.mapScene.dependencyBundlePaths || []).some(function (path) { return !validRelativePackagePath(path); })) throw new Error("Dependency bundle paths must be safe relative paths.");
    if (!state.sceneBundleFile && !draft.mapScene.bundlePath) throw new Error("Choose a scene bundle before exporting the imported map.");
    draft.holes.forEach(function (hole) {
      if (!hole.mapTeePosition) hole.mapTeePosition = mapNormToWorld(mapAnchors(hole).tee);
      if (!hole.mapBasketPosition) hole.mapBasketPosition = mapNormToWorld(mapAnchors(hole).basket);
    });
  }
  const holes = draft.holes.map(function (hole, index) {
    const fileName = cleanId + "-hole-" + String(index + 1).padStart(2, "0") + ".png";
    state.holeIndex = index; drawCardPreview(); images.push({ name: fileName, dataUrl: cardCanvas.toDataURL("image/png") });
    const output = Object.assign({}, hole, { cardImage: fileName });
    if (!imported) { delete output.mapTeePosition; delete output.mapBasketPosition; }
    return output;
  });
  if (draft.mapPreviewDataUrl && draft.mapPreviewImage) images.push({ name: draft.mapPreviewImage, dataUrl: draft.mapPreviewDataUrl });
  state.holeIndex = Math.min(state.holeIndex, draft.holes.length - 1);
  const documentData = {
    schemaVersion: 2, id: cleanId, name: draft.name || "Custom Course", baseCourse: draft.baseCourse,
    baseLayout: draft.baseLayout || "", description: draft.description || "",
    courseCardImage: cleanId + "-hole-01.png", editorMapWidthMeters: state.mapWidth, holes: holes,
    mapPreviewImage: draft.mapPreviewImage || "", mapWorldBounds: draft.mapWorldBounds,
    mapLighting: draft.mapLighting || defaultMapLighting(),
    layoutGroundColliderExclusions: draft.layoutGroundColliderExclusions || []
  };
  if (imported) documentData.mapScene = {
    scenePath: draft.mapScene.scenePath.trim(), bundlePath: draft.mapScene.bundlePath.replace(/\\/g, "/").trim(),
    dependencyBundlePaths: (draft.mapScene.dependencyBundlePaths || []).map(function (path) { return path.replace(/\\/g, "/").trim(); })
  };
  return { json: JSON.stringify(documentData, null, 2), images: images, fileName: cleanId + ".json" };
}
async function installToGame(bundle, imported) {
  let installedCount = 0;
  async function installFile(relativePath, blob) {
    const safePath = String(relativePath || "").replace(/\\/g, "/");
    const url = "/api/install/" + safePath.split("/").map(encodeURIComponent).join("/");
    const response = await fetch(url, { method: "PUT", body: blob });
    if (!response.ok) throw new Error(await response.text() || "Package write failed.");
    installedCount++;
  }
  for (const image of bundle.images)
    await installFile("Courses/" + image.name, dataUrlToBlob(image.dataUrl));
  await installFile("Courses/" + bundle.fileName, new Blob([bundle.json], { type: "application/json" }));
  if (imported && state.sceneBundleFile)
    await installFile(state.draft.mapScene.bundlePath, state.sceneBundleFile);
  if (imported) {
    for (const dependency of state.dependencyFiles) {
      if ((state.draft.mapScene.dependencyBundlePaths || []).indexOf(dependency.path) >= 0)
        await installFile(dependency.path, dependency.file);
    }
  }
  return installedCount;
}
async function exportCourse() {
  if (!state.draft) { toast("Choose an original course first."); return; }
  let bundle;
  try { bundle = exportDocument(); }
  catch (error) { toast(error.message || "Course export could not be prepared."); return; }
  const imported = isImportedMap();
  try {
    const installedCount = await installToGame(bundle, imported);
    state.holeIndex = 0; renderEditor();
    toast("Installed " + installedCount + " course file(s) into the game. Restart the game to load it.");
    return;
  } catch (error) {
    console.warn("Direct game install unavailable; using the folder/download export flow.", error);
  }
  try {
    if (typeof window.showDirectoryPicker === "function") {
      const rootDirectory = await window.showDirectoryPicker({ mode: "readwrite", startIn: "downloads" });
      const courseDirectory = imported || rootDirectory.name === "CustomCourseLoader"
        ? await rootDirectory.getDirectoryHandle("Courses", { create: true }) : rootDirectory;
      for (const image of bundle.images) await writeFile(courseDirectory, image.name, dataUrlToBlob(image.dataUrl));
      await writeFile(courseDirectory, bundle.fileName, new Blob([bundle.json], { type: "application/json" }));
      if (imported) {
        const bundleDirectory = await ensureDirectoryPath(rootDirectory, state.draft.mapScene.bundlePath);
        if (state.sceneBundleFile) await copyFile(bundleDirectory, pathLeaf(state.draft.mapScene.bundlePath), state.sceneBundleFile);
        for (const dependency of state.dependencyFiles) {
          if ((state.draft.mapScene.dependencyBundlePaths || []).indexOf(dependency.path) >= 0)
            await copyFile(await ensureDirectoryPath(rootDirectory, dependency.path), pathLeaf(dependency.path), dependency.file);
        }
      }
      state.holeIndex = 0; renderEditor();
      toast(imported ? "Course package exported. Restart the game to load the imported map." : "Course and hole cards exported. Restart the game to load the course."); return;
    }
  } catch (error) {
    if (error && error.name === "AbortError") return;
    toast("Folder access unavailable; downloading the course files instead.");
  }
  downloadText(bundle.json, bundle.fileName, "application/json");
  bundle.images.forEach(function (image, index) { setTimeout(function () { downloadUrl(image.dataUrl, image.name); }, 180 * (index + 1)); });
  if (imported && state.sceneBundleFile) setTimeout(function () { downloadUrl(URL.createObjectURL(state.sceneBundleFile), pathLeaf(state.draft.mapScene.bundlePath), true); }, 180 * (bundle.images.length + 1));
  if (imported) state.dependencyFiles.forEach(function (dependency, index) {
    if ((state.draft.mapScene.dependencyBundlePaths || []).indexOf(dependency.path) >= 0)
      setTimeout(function () { downloadUrl(URL.createObjectURL(dependency.file), pathLeaf(dependency.path), true); }, 180 * (bundle.images.length + index + 2));
  });
  state.holeIndex = 0; renderEditor();
  toast(imported ? "Downloaded package files. Place scene bundles at their Maps paths and course files in Courses." : "Downloaded the course JSON and image assets. Keep them together in the Courses folder.");
}
function pathLeaf(path) { return String(path || "").replace(/\\/g, "/").split("/").pop(); }
async function ensureDirectoryPath(root, relativePath) {
  let directory = root;
  const parts = String(relativePath).replace(/\\/g, "/").split("/").slice(0, -1);
  for (const part of parts) directory = await directory.getDirectoryHandle(part, { create: true });
  return directory;
}
async function writeFile(directory, name, blob) {
  const file = await directory.getFileHandle(name, { create: true }), stream = await file.createWritable();
  await stream.write(blob); await stream.close();
}
async function copyFile(directory, name, file) { await writeFile(directory, name, file); }
function dataUrlToBlob(dataUrl) {
  const parts = dataUrl.split(","), mime = (parts[0].match(/:(.*?);/) || [])[1] || "image/png";
  const binary = atob(parts[1]), bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new Blob([bytes], { type: mime });
}
function downloadText(text, name, type) { downloadUrl(URL.createObjectURL(new Blob([text], { type: type })), name, true); }
function downloadUrl(url, name, revoke) {
  const link = document.createElement("a"); link.href = url; link.download = name;
  document.body.appendChild(link); link.click(); link.remove();
  if (revoke) setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
}
function openCourseFile(file) {
  const reader = new FileReader();
  reader.onload = function () {
    try {
      state.draft = normalizeCourse(JSON.parse(reader.result)); state.holeIndex = 0;
      state.mapWidth = state.draft.editorMapWidthMeters || 400; state.cardTheme = state.draft.cardTheme || "moss";
      state.mapMode = "design"; openEditor(); toast("Opened " + file.name + ".");
    } catch (error) { toast("That file is not a valid course JSON."); }
  };
  reader.readAsText(file);
}
async function openInstalledCourse(fileName) {
  try {
    const response = await fetch("/course-files/" + encodeURIComponent(fileName), { cache: "no-store" });
    if (!response.ok) throw new Error("Could not read that installed course.");
    state.draft = normalizeCourse(await response.json());
    state.sceneBundleFile = null; state.dependencyFiles = [];
    state.holeIndex = 0; state.mapWidth = state.draft.editorMapWidthMeters || 400;
    state.cardTheme = state.draft.cardTheme || "moss"; state.mapMode = "design";
    openEditor(); toast("Opened " + state.draft.name + ".");
  } catch (error) {
    toast(error.message || "Could not open that installed course.");
  }
}
function uniqueMapLayoutId(mapId) {
  const root = slug(mapId + "-layout").slice(0, 42);
  let candidate = root, suffix = 2;
  const exists = function (id) {
    return state.createdMapLayoutIds.has(id) || state.installedMaps.some(function (map) { return map.id === id; });
  };
  while (exists(candidate)) { candidate = root.slice(0, 48 - String(suffix).length - 1) + "-" + suffix; suffix++; }
  state.createdMapLayoutIds.add(candidate);
  return candidate;
}
async function createLayoutOnInstalledMap(map) {
  try {
    if (!map || !map.courseFileName) throw new Error("This map does not have a saved course layout yet.");
    const response = await fetch("/course-files/" + encodeURIComponent(map.courseFileName), { cache: "no-store" });
    if (!response.ok) throw new Error("Could not read the installed map setup.");
    const source = await response.json();
    state.draft = normalizeCourse(source);
    state.draft.id = uniqueMapLayoutId(map.id);
    state.draft.name = ("New " + (map.name || source.name || "Map") + " Layout").slice(0, 64);
    state.draft.description = "A new layout on " + (map.name || source.name || "an installed map") + ".";
    state.draft.courseCardImage = "";
    if (!state.draft.mapPreviewDataUrl && map.previewUrl) {
      try { state.draft.mapPreviewDataUrl = await imageUrlToDataUrl(map.previewUrl); } catch (error) { }
      if (state.draft.mapPreviewDataUrl) state.draft.mapPreviewImage = state.draft.id + "-map.png";
    }
    state.sceneBundleFile = null; state.dependencyFiles = [];
    state.holeIndex = 0; state.mapWidth = state.draft.editorMapWidthMeters || 400;
    state.cardTheme = state.draft.cardTheme || "moss"; state.mapMode = "design"; state.inspector = "hole";
    openEditor(); toast("Started a new layout on " + map.name + ".");
  } catch (error) {
    toast(error.message || "Could not start a layout on that map.");
  }
}
async function imageUrlToDataUrl(url) {
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) return "";
  const blob = await response.blob();
  return new Promise(function (resolve, reject) {
    const reader = new FileReader();
    reader.onload = function () { resolve(reader.result); };
    reader.onerror = reject; reader.readAsDataURL(blob);
  });
}
function setupInstalledBundle(map) {
  makeImportedMapCourse();
  state.draft.id = uniqueMapLayoutId(map.id);
  state.draft.name = ("New " + (map.name || "Installed Map") + " Course").slice(0, 64);
  state.draft.description = "A custom course on " + (map.name || "an installed map") + ".";
  state.draft.mapScene.bundlePath = map.bundlePath;
  state.draft.mapScene.scenePath = map.scenePath || "";
  state.draft.mapScene.dependencyBundlePaths = (map.dependencies || []).map(function (dependency) { return dependency.path; });
  byId("course-id").value = state.draft.id; byId("course-name").value = state.draft.name;
  byId("course-description").value = state.draft.description;
  renderEditor();
  byId("map-setup-details").open = true;
  if (!map.scenePath) byId("map-scene-path").focus();
  toast(map.configured ? "Map package selected. Set up its holes." : "Enter the scene's .unity path in Map settings to finish setup.");
}
