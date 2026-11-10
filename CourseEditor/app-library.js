"use strict";

function renderLibrary() {
  const grid = byId("course-grid");
  const query = byId("course-search").value.trim().toLowerCase();
  const installed = state.librarySource === "installed";
  byId("library-original").classList.toggle("active", !installed);
  byId("library-original").setAttribute("aria-selected", String(!installed));
  byId("library-installed").classList.toggle("active", installed);
  byId("library-installed").setAttribute("aria-selected", String(installed));
  byId("library-title").textContent = installed ? "Installed maps" : "Choose a course map";
  byId("course-search").placeholder = installed ? "Find an installed map" : "Find an original course";
  byId("new-imported-map").hidden = !installed;
  byId("open-course-from-disk").hidden = !installed;
  byId("refresh-installed-maps").hidden = !installed;
  byId("refresh-installed-maps").disabled = state.installedMapsLoading;
  byId("refresh-installed-maps").textContent = state.installedMapsLoading ? "Scanning…" : "↻ Scan maps";
  byId("map-filter-control").hidden = !installed;
  byId("installed-map-result-count").hidden = !installed || state.installedMapsLoading || !!state.installedMapsError;
  const courseCount = String(CATALOG.length).padStart(2, "0");
  byId("course-count").textContent = courseCount;
  byId("hero-course-count").textContent = courseCount + " ORIGINALS";
  byId("installed-map-count").textContent = state.installedMapsLoading ? "…" : String(state.installedMaps.length).padStart(2, "0");
  grid.classList.toggle("list", state.view === "list");
  grid.classList.toggle("map-explorer", installed);
  if (installed) { renderInstalledMapExplorer(grid, query); return; }
  const visible = CATALOG.filter(function (course) { return course.name.toLowerCase().includes(query); });
  if (!visible.length) { grid.innerHTML = '<div class="empty-library">No courses match that name.</div>'; return; }
  grid.innerHTML = visible.map(function (course) {
    const layout = course.layouts && course.layouts[0], map = findMap(layout);
    const holeCount = layout && layout.holes && layout.holes.length ? layout.holes.length : 18;
    const par = layout && layout.par ? layout.par : holeCount * 3;
    const meters = layout && layout.lengthMeters ? layout.lengthMeters : "—";
    const layoutCount = course.layouts && course.layouts.length ? course.layouts.length : 1;
    const art = map
      ? '<img loading="lazy" src="' + escapeAttr(map) + '" alt="' + escapeAttr(course.name) + ' map preview">'
      : '<div class="course-art-fallback"><span class="fallback-contour fallback-one"></span><span class="fallback-contour fallback-two"></span><span class="fallback-fairway"></span><span class="fallback-pin"></span><span class="fallback-tee"></span></div>';
    return '<article class="course-card" data-course="' + escapeAttr(course.id) + '" data-layout="' + escapeAttr(layout ? layout.id : "") + '">' +
      '<div class="course-card-art">' + art + '<span class="course-card-stamp"><i></i> ' + courseInitials(course.name) + ' / ORIGINAL</span><span class="course-card-arrow">↗</span></div>' +
      '<div class="course-card-body"><h3>' + escapeHtml(course.name) + '</h3><div class="course-card-meta"><span><b>' + String(holeCount).padStart(2, "0") + '</b> HOLES</span><span><b>PAR ' + par + '</b></span><span><b>' + meters + '</b> M</span></div>' +
      '<div class="course-card-layout"><span>BASE LAYOUT</span><em>' + escapeHtml(layout ? layout.name : "Original course terrain") + (layoutCount > 1 ? " · +" + (layoutCount - 1) + " layouts" : "") + '</em></div></div></article>';
  }).join("");
  grid.querySelectorAll(".course-card").forEach(function (card) {
    card.addEventListener("click", function () { makeCourse(card.dataset.course, card.dataset.layout); });
  });
}
function installedMapAuthoringWarnings(map) {
  const warnings = [];
  if (map.worldBoundsStatus === "missing" || map.worldBoundsStatus === "default") warnings.push("Map overview uses the default ±100 m bounds; calibrate it to show the full scene.");
  else if (map.worldBoundsStatus === "invalid") warnings.push("Map world bounds are invalid; correct them before editing.");

  if (map.holeCount === 0 && map.courseFileName) {
    warnings.push("No holes are defined yet; add holes before playing.");
  } else if (map.holeCount > 0 && map.explicitPositionHoleCount < map.holeCount) {
    if (map.autoLayout === "long") warnings.push("Hole positions are planned at runtime; review every tee and basket.");
    else if (!map.explicitPositionHoleCount) warnings.push("No tee/basket positions are authored; place them before playing.");
    else warnings.push(map.explicitPositionHoleCount + "/" + map.holeCount + " holes have tee/basket positions; every hole needs both.");
  }
  return warnings;
}

function renderInstalledMapExplorer(grid, query) {
  if (state.installedMapsLoading) { grid.innerHTML = '<div class="map-explorer-state"><span class="loader"></span><b>Scanning installed map packages…</b></div>'; return; }
  if (state.installedMapsError) { grid.innerHTML = '<div class="map-explorer-state"><b>Could not scan the plugin Maps folder.</b><small>' + escapeHtml(state.installedMapsError) + '</small><button class="button button-outline" id="retry-map-scan">Scan again</button></div>'; byId("retry-map-scan").addEventListener("click", loadInstalledMaps); return; }
  const searched = state.installedMaps.filter(function (map) {
    return !query || [map.name, map.description, map.bundlePath, map.scenePath, map.courseFileName, map.id].join(" ").toLowerCase().includes(query);
  });
  const visible = searched.filter(function (map) {
    return state.installedMapFilter === "all" || installedMapHealth(map) === state.installedMapFilter;
  });
  byId("installed-map-result-count").textContent = visible.length === searched.length
    ? visible.length + " of " + state.installedMaps.length + " installed map" + (state.installedMaps.length === 1 ? "" : "s")
    : visible.length + " shown · " + searched.length + " match search · " + state.installedMaps.length + " total";
  if (!visible.length) {
    grid.innerHTML = state.installedMaps.length
      ? '<div class="map-explorer-state"><b>No maps match these filters.</b><small>Change the status filter or clear the search to see more map packages.</small></div>'
      : '<div class="map-explorer-state"><b>No imported map packages found yet.</b><small>Original courses are listed in the other tab. Add a Unity scene bundle to install a new map.</small></div>';
    return;
  }
  grid.innerHTML = visible.map(function (map, index) {
    const art = map.previewUrl
      ? '<img loading="lazy" src="' + escapeAttr(map.previewUrl) + '" alt="' + escapeAttr(map.name) + ' map overview">'
      : '<div class="course-art-fallback"><span class="fallback-contour fallback-one"></span><span class="fallback-contour fallback-two"></span><span class="fallback-fairway"></span><span class="fallback-pin"></span><span class="fallback-tee"></span></div>';
    const health = installedMapHealth(map);
    const missingDependencies = (map.dependencies || []).filter(function (dependency) { return !dependency.exists; }).length;
    const missingBundle = !map.legacyApi && (!map.bundlePath || !map.bundleExists);
    const status = health === "missing" ? (missingBundle && missingDependencies ? "FILES MISSING" : missingDependencies ? "DEPENDENCIES MISSING" : "BUNDLE MISSING")
      : health === "ready" ? "READY TO EDIT" : "SETUP REQUIRED";
    const statusClass = health;
    const metadata = map.courseFileName
      ? (map.holeCount || 0) + " holes · " + (map.dependencies || []).length + " dependencies" + (missingDependencies ? " · " + missingDependencies + " missing" : "")
      : "Unity scene bundle · " + escapeHtml(map.bundlePath);
    const issue = health === "missing"
      ? [missingBundle ? (map.bundlePath ? "Scene bundle file not found." : "Scene bundle path is missing or invalid.") : "",
        missingDependencies ? missingDependencies + " dependency bundle" + (missingDependencies === 1 ? " is" : "s are") + " missing." : ""].filter(Boolean).join(" ")
      : health === "setup" ? (map.scenePath && map.scenePathValid === false ? "Scene path must be a safe relative .unity asset path." : map.scenePath ? "Set a valid scene bundle path to finish setup." : "Add the scene’s .unity path inside the bundle.")
        : "Scene bundle and dependencies are present.";
    const authoringWarnings = installedMapAuthoringWarnings(map);
    const authoringNote = authoringWarnings.length
      ? '<div class="installed-map-authoring-warnings"><b>AUTHORING CHECK</b>' + authoringWarnings.map(function (warning) { return '<span>' + escapeHtml(warning) + '</span>'; }).join("") + '</div>'
      : '';
    const deleteAction = '<button class="inspector-button danger" data-map-action="delete" data-map-index="' + index + '" aria-label="Delete ' + escapeAttr(map.name) + '">Delete map</button>';
    const actions = map.courseFileName
      ? '<div class="installed-map-actions"><button class="inspector-button primary" data-map-action="edit" data-map-index="' + index + '">Edit course</button><button class="inspector-button" data-map-action="new" data-map-index="' + index + '">New layout</button>' + deleteAction + '</div>'
      : '<div class="installed-map-actions"><button class="inspector-button primary" data-map-action="setup" data-map-index="' + index + '">Set up course</button>' + deleteAction + '</div>';
    return '<article class="installed-map-card"><div class="course-card-art">' + art + '<span class="course-card-stamp"><i></i> ' + escapeHtml(status) + '</span></div>' +
      '<div class="course-card-body"><div class="installed-map-heading"><h3>' + escapeHtml(map.name) + '</h3><span class="map-status ' + statusClass + '">' + escapeHtml(status) + '</span></div>' +
      '<div class="installed-map-meta">' + metadata + '</div><div class="installed-map-path" title="' + escapeAttr(map.bundlePath || map.courseFileName) + '">' + escapeHtml(map.scenePath || (map.legacyApi ? "Installed course package" : "Scene path not set")) + '</div><div class="installed-map-issue ' + statusClass + '">' + escapeHtml(issue) + '</div>' + authoringNote + actions + '</div></article>';
  }).join("");
  grid.querySelectorAll("[data-map-action]").forEach(function (button) {
    button.addEventListener("click", function (event) {
      event.stopPropagation();
      const map = visible[Number(button.dataset.mapIndex)];
      if (!map) return;
      if (button.dataset.mapAction === "edit") openInstalledCourse(map.courseFileName);
      else if (button.dataset.mapAction === "new") createLayoutOnInstalledMap(map);
      else if (button.dataset.mapAction === "delete") deleteInstalledMapFromExplorer(map, button);
      else setupInstalledBundle(map);
    });
  });
}
async function deleteInstalledMapFromExplorer(map, button) {
  const message = 'Delete "' + map.name + '" from Installed maps?\n\nThis removes its course file and map bundles that no other course uses. Shared bundles and images referenced by another course will be kept. This can’t be undone.';
  if (!window.confirm(message)) return;

  button.disabled = true;
  button.textContent = "Deleting…";
  try {
    const response = await fetch("/api/maps", {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ courseFileName: map.courseFileName || "", bundlePath: map.bundlePath || "" })
    });
    let result = {};
    try { result = await response.json(); } catch (error) { }
    if (!response.ok) throw new Error(result.error || "The map could not be deleted.");
    toast('Deleted "' + (result.name || map.name) + '" from Installed maps.');
    await loadInstalledMaps();
  } catch (error) {
    if (button.isConnected) {
      button.disabled = false;
      button.textContent = "Delete map";
    }
    toast(error.message || "The map could not be deleted.");
  }
}
function installedMapHealth(map) {
  if (!map.legacyApi && (!map.bundlePath || !map.bundleExists)) return "missing";
  if ((map.dependencies || []).some(function (dependency) { return !dependency.exists; })) return "missing";
  if (!map.configured || map.scenePathValid === false) return "setup";
  return "ready";
}
async function loadInstalledMaps() {
  state.installedMapsLoading = true; state.installedMapsError = ""; renderLibrary();
  try {
    const response = await fetch("/api/maps", { cache: "no-store" });
    if (!response.ok) throw new Error("Map explorer could not read installed packages.");
    const maps = await response.json();
    if (!Array.isArray(maps)) throw new Error("Map explorer received an invalid package list.");
    state.installedMaps = maps;
    state.installedMapsLoaded = true;
    let verifiedPreview = false;
    if (isImportedMap() && state.draft.mapPreviewDataUrl && !state.draft.mapPreviewWorldBounds) {
      const savedMap = maps.find(function (map) { return map.id === slug(state.draft.id) && map.previewWorldBounds && map.previewUrl; });
      if (savedMap) {
        try { verifiedPreview = (await imageUrlToDataUrl(savedMap.previewUrl)) === state.draft.mapPreviewDataUrl; } catch (error) { }
      }
    }
    if (adoptGeneratedPreviewBounds(verifiedPreview)) { setMapImage(); renderEditor(); saveState(); }
  } catch (error) {
    try {
      const response = await fetch("/api/courses", { cache: "no-store" });
      if (!response.ok) throw error;
      const courses = await response.json();
      if (!Array.isArray(courses)) throw error;
      state.installedMaps = courses.filter(function (course) { return course.importedMap; }).map(function (course) {
        const fileName = String(course.fileName || "");
        return {
          id: slug(fileName.replace(/\.json$/i, "")), name: course.name || fileName,
          courseFileName: fileName, bundlePath: "", scenePath: "", dependencies: [],
          holeCount: course.holeCount || 0, baseCourse: "CanonPark", previewUrl: course.previewUrl || "",
          bundleExists: true, configured: true, legacyApi: true
        };
      });
      state.installedMapsLoaded = true;
    } catch (fallbackError) {
      state.installedMapsError = error.message || "Map scan failed.";
    }
  } finally {
    state.installedMapsLoading = false; renderLibrary();
  }
}
function showMapLibrary(source) {
  state.librarySource = source;
  byId("overview-view").hidden = false; byId("editor-view").hidden = true;
  byId("map-status-filter").value = state.installedMapFilter;
  renderLibrary(); byId("overview-view").scrollIntoView({ behavior: "smooth" });
  if (source === "installed" && !state.installedMapsLoading) loadInstalledMaps();
}
function renderBaseSelectors() {
  const baseSelect = byId("base-course"), chosen = state.draft.baseCourse;
  baseSelect.innerHTML = CATALOG.map(function (course) {
    return '<option value="' + escapeAttr(course.id) + '">' + escapeHtml(course.name) + '</option>';
  }).join("");
  baseSelect.value = chosen;
  const base = originalCourse(chosen), layoutSelect = byId("base-layout");
  const layouts = base && base.layouts ? base.layouts : [];
  if (!layouts.length) {
    layoutSelect.innerHTML = '<option value="">Default available layout</option>'; layoutSelect.disabled = true; return;
  }
  layoutSelect.disabled = false;
  layoutSelect.innerHTML = layouts.map(function (layout, index) {
    return '<option value="' + escapeAttr(layout.id) + '">' + escapeHtml((layout.name || ("Layout " + (index + 1))) + (layout.par ? " · Par " + layout.par : "")) + '</option>';
  }).join("");
  layoutSelect.value = state.draft.baseLayout || layouts[0].id;
}
function openEditor() {
  if (!state.draft) return;
  byId("overview-view").hidden = true; byId("editor-view").hidden = false;
  byId("course-name").value = state.draft.name; byId("course-id").value = state.draft.id;
  byId("course-description").value = state.draft.description; byId("map-width").value = state.mapWidth;
  renderBaseSelectors(); renderEditor();
}
