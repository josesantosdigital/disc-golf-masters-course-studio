"use strict";

function bindEvents() {
  byId("course-search").addEventListener("input", renderLibrary);
  byId("map-status-filter").addEventListener("change", function (event) { state.installedMapFilter = event.target.value; renderLibrary(); });
  byId("refresh-installed-maps").addEventListener("click", loadInstalledMaps);
  document.querySelectorAll(".view-button").forEach(function (button) {
    button.addEventListener("click", function () {
      state.view = button.dataset.view; document.querySelectorAll(".view-button").forEach(function (item) { item.classList.toggle("active", item === button); }); renderLibrary();
    });
  });
  byId("new-course").addEventListener("click", function () { showMapLibrary("original"); });
  byId("new-imported-map").addEventListener("click", makeImportedMapCourse);
  byId("back-overview").addEventListener("click", function () { state.drawingOb = false; showMapLibrary(state.librarySource); });
  byId("open-course").addEventListener("click", function () { showMapLibrary("installed"); });
  byId("library-original").addEventListener("click", function () { showMapLibrary("original"); });
  byId("library-installed").addEventListener("click", function () { showMapLibrary("installed"); });
  byId("open-course-from-disk").addEventListener("click", function () { byId("course-file").click(); });
  byId("course-file").addEventListener("change", function (event) { const file = event.target.files && event.target.files[0]; if (file) openCourseFile(file); event.target.value = ""; });
  byId("export-course").addEventListener("click", exportCourse);
  byId("add-hole").addEventListener("click", addHole); byId("remove-hole").addEventListener("click", removeCurrentHole);
  byId("course-name").addEventListener("input", function (event) { setCourseName(event.target.value); });
  byId("course-id").addEventListener("change", setCourseMeta); byId("course-description").addEventListener("change", setCourseMeta);
  byId("base-course").addEventListener("change", function (event) { setBaseCourse(event.target.value); });
  byId("base-layout").addEventListener("change", function (event) { setBaseLayout(event.target.value); });
  byId("course-source").addEventListener("change", function (event) { setCourseSource(event.target.value); });
  byId("map-scene-path").addEventListener("change", function (event) { updateMapSceneField("scenePath", event.target.value); });
  byId("map-bundle-path").addEventListener("change", function (event) { updateMapSceneField("bundlePath", event.target.value); });
  byId("map-dependency-paths").addEventListener("change", function (event) { updateMapSceneField("dependencyBundlePaths", event.target.value); });
  ["world-min-x", "world-max-x", "world-min-z", "world-max-z", "world-ground-y"].forEach(function (id) {
    byId(id).addEventListener("change", setImportedMapBounds);
  });
  byId("layout-ground-collider-exclusions").addEventListener("change", setLayoutGroundColliderExclusions);
  ["map-force-unlit", "map-realtime-shadows", "map-hide-particles", "map-tree-canopy-collisions", "map-foliage-interactions", "map-game-foliage", "map-lit-foliage", "map-base-sun", "map-preserve-lightmaps", "map-disable-occlusion", "map-repair-terrain-materials", "map-repair-terrain-details", "map-force-tree-mesh-lods", "map-albedo-brightness", "map-local-light-multiplier", "map-ambient-color", "map-ambient-intensity", "map-key-color", "map-key-intensity", "map-key-yaw", "map-key-pitch"].forEach(function (id) {
    byId(id).addEventListener("change", setImportedMapLighting);
  });
  byId("choose-scene-bundle").addEventListener("click", function () { byId("scene-bundle-file").click(); });
  byId("scene-bundle-file").addEventListener("change", function (event) { chooseSceneBundle(event.target.files && event.target.files[0]); event.target.value = ""; });
  byId("choose-dependencies").addEventListener("click", function () { byId("dependency-files").click(); });
  byId("dependency-files").addEventListener("change", function (event) { chooseDependencies(event.target.files); event.target.value = ""; });
  byId("choose-map-image").addEventListener("click", function () { byId("map-image-file").click(); });
  byId("import-map-image").addEventListener("click", function () { byId("map-image-file").click(); });
  byId("map-image-file").addEventListener("change", function (event) { chooseMapPreview(event.target.files && event.target.files[0]); event.target.value = ""; });
  byId("map-width").addEventListener("change", function (event) { state.mapWidth = clamp(Number(event.target.value) || 400, 50, 5000); event.target.value = state.mapWidth; paintMap(); saveState(); });
  byId("calibrate-tee").addEventListener("click", function () { startCalibration("tee"); });
  byId("calibrate-basket").addEventListener("click", function () { startCalibration("basket"); });
  byId("draw-ob").addEventListener("click", startDrawingOb); byId("finish-ob").addEventListener("click", finishOb); byId("cancel-ob").addEventListener("click", cancelOb);
  byId("zoom-in").addEventListener("click", function () { zoomAt(1.22); });
  byId("zoom-out").addEventListener("click", function () { zoomAt(1 / 1.22); });
  byId("zoom-fit").addEventListener("click", function () { state.zoom = 1; state.pan = { x: 0, y: 0 }; paintMap(); });
  byId("view-original").addEventListener("click", function () {
    state.mapMode = "overview"; document.querySelectorAll(".map-tab").forEach(function (button) { button.classList.toggle("active", button.dataset.mapMode === "overview"); }); paintMap();
  });
  document.querySelectorAll(".map-tab").forEach(function (button) {
    button.addEventListener("click", function () { state.mapMode = button.dataset.mapMode; document.querySelectorAll(".map-tab").forEach(function (item) { item.classList.toggle("active", item === button); }); paintMap(); });
  });
  document.querySelectorAll(".inspector-tab").forEach(function (button) {
    button.addEventListener("click", function () { state.inspector = button.dataset.inspector; renderInspector(); });
  });
  canvas.addEventListener("pointerdown", onCanvasPointerDown); canvas.addEventListener("pointermove", onCanvasPointerMove);
  canvas.addEventListener("pointerup", onCanvasPointerUp); canvas.addEventListener("pointercancel", onCanvasPointerUp);
  canvas.addEventListener("wheel", function (event) { event.preventDefault(); zoomAt(event.deltaY < 0 ? 1.12 : 1 / 1.12, mapPointer(event)); }, { passive: false });
  canvas.addEventListener("dblclick", function () { if (state.drawingOb) finishOb(); });
  canvas.addEventListener("contextmenu", function (event) { event.preventDefault(); });
  window.addEventListener("resize", paintMap);
  window.addEventListener("keydown", function (event) {
    if (event.code === "Space") { state.spaceDown = true; if (document.activeElement === document.body) event.preventDefault(); }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") { event.preventDefault(); exportCourse(); }
    if (event.key === "Escape" && state.drawingOb) cancelOb();
    if (event.key === "Delete" && state.inspector === "ob" && state.selectedZone >= 0) doAction("delete-zone", state.selectedZone);
    if (event.key === "+" || event.key === "=") zoomAt(1.18); if (event.key === "-") zoomAt(1 / 1.18);
  });
  window.addEventListener("keyup", function (event) { if (event.code === "Space") state.spaceDown = false; });
}
