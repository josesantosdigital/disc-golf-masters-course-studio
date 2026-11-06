"use strict";

function getMapGeometry(width, height) {
  const sourceWidth = state.image ? state.image.naturalWidth : 1000;
  const sourceHeight = state.image ? state.image.naturalHeight : 720;
  const base = Math.min(width / sourceWidth, height / sourceHeight);
  const drawWidth = sourceWidth * base * state.zoom, drawHeight = sourceHeight * base * state.zoom;
  return { sourceWidth: sourceWidth, sourceHeight: sourceHeight, x: (width - drawWidth) / 2 + state.pan.x,
    y: (height - drawHeight) / 2 + state.pan.y, width: drawWidth, height: drawHeight };
}
function nToScreen(point, geometry) { return { x: geometry.x + point.x * geometry.width, y: geometry.y + point.y * geometry.height }; }
function screenToN(point, geometry) { return { x: clamp((point.x - geometry.x) / geometry.width, 0, 1), y: clamp((point.y - geometry.y) / geometry.height, 0, 1) }; }
function currentCourseSize() {
  const aspect = state.image ? state.image.naturalHeight / state.image.naturalWidth : 0.72;
  if (state.draft && state.draft.sourceMode === "imported") {
    const bounds = state.draft.mapWorldBounds;
    return { width: Math.max(1, Number(bounds.maxX) - Number(bounds.minX)), height: Math.max(1, Number(bounds.maxZ) - Number(bounds.minZ)) };
  }
  return { width: state.mapWidth, height: state.mapWidth * aspect };
}
function mapNormToWorld(position, priorPosition) {
  const bounds = state.draft.mapWorldBounds;
  return {
    x: round1(Number(bounds.minX) + position.x * (Number(bounds.maxX) - Number(bounds.minX))),
    y: priorPosition && Number.isFinite(Number(priorPosition.y)) ? round1(Number(priorPosition.y)) : round1(Number(bounds.groundY) || 0),
    z: round1(Number(bounds.maxZ) - position.y * (Number(bounds.maxZ) - Number(bounds.minZ)))
  };
}
function worldToMapNorm(position, fallback) {
  if (!position) return fallback;
  const bounds = state.draft.mapWorldBounds;
  const width = Number(bounds.maxX) - Number(bounds.minX), depth = Number(bounds.maxZ) - Number(bounds.minZ);
  if (width <= 0 || depth <= 0) return fallback;
  return {
    x: clamp((Number(position.x) - Number(bounds.minX)) / width, 0, 1),
    y: clamp((Number(bounds.maxZ) - Number(position.z)) / depth, 0, 1)
  };
}
function mapHeading(hole) {
  if (!isImportedMap()) return Number(mapAnchors(hole).heading) || 0;
  const tee = markerMapPosition(hole, "tee"), basket = markerMapPosition(hole, "basket"), size = currentCourseSize();
  const dx = (basket.x - tee.x) * size.width, dy = -(basket.y - tee.y) * size.height;
  const lineHeading = Math.hypot(dx, dy) > 1 ? Math.atan2(dy, dx) : Number(mapAnchors(hole).heading) || 0;
  return lineHeading - (Number(hole.teeYawDegrees) || 0) * Math.PI / 180;
}
function offsetToMap(anchor, offset, hole) {
  const size = currentCourseSize(), heading = mapHeading(hole);
  const forward = { x: Math.cos(heading), y: Math.sin(heading) }, right = { x: Math.sin(heading), y: -Math.cos(heading) };
  const rightMeters = Number(offset.right) || 0, forwardMeters = Number(offset.forward) || 0;
  return { x: anchor.x + (right.x * rightMeters + forward.x * forwardMeters) / size.width,
    y: anchor.y - (right.y * rightMeters + forward.y * forwardMeters) / size.height };
}
function mapToOffset(position, anchor, hole) {
  const size = currentCourseSize(), heading = mapHeading(hole);
  const forward = { x: Math.cos(heading), y: Math.sin(heading) }, right = { x: Math.sin(heading), y: -Math.cos(heading) };
  const mapDelta = { x: (position.x - anchor.x) * size.width, y: -(position.y - anchor.y) * size.height };
  return { right: round1(mapDelta.x * right.x + mapDelta.y * right.y), forward: round1(mapDelta.x * forward.x + mapDelta.y * forward.y) };
}
function markerMapPosition(hole, type) {
  const anchors = mapAnchors(hole);
  if (state.draft && state.draft.sourceMode === "imported") {
    const fallback = type === "tee" ? anchors.tee : anchors.basket;
    return worldToMapNorm(type === "tee" ? hole.mapTeePosition : hole.mapBasketPosition, fallback);
  }
  return type === "tee" ? offsetToMap(anchors.tee, hole.teeOffset, hole) : offsetToMap(anchors.basket, hole.basketOffset, hole);
}
function obPointToMap(point, hole) { return offsetToMap(obAnchor(hole), point, hole); }
function updateMarkerFromMap(hole, type, position) {
  const anchors = mapAnchors(hole);
  if (state.draft && state.draft.sourceMode === "imported") {
    if (type === "tee") hole.mapTeePosition = mapNormToWorld(position, hole.mapTeePosition);
    else hole.mapBasketPosition = mapNormToWorld(position, hole.mapBasketPosition);
    return;
  }
  if (type === "tee") {
    const offset = mapToOffset(position, anchors.tee, hole);
    hole.teeOffset.right = offset.right; hole.teeOffset.forward = offset.forward;
  } else {
    const offset = mapToOffset(position, anchors.basket, hole);
    hole.basketOffset.right = offset.right; hole.basketOffset.forward = offset.forward;
  }
}
function calibrateAt(position, type) {
  const hole = currentHole(), anchors = mapAnchors(hole);
  anchors[type] = position;
  anchors[type + "Set"] = true;
  if (state.draft.sourceMode === "imported") {
    const worldPosition = mapNormToWorld(position);
    if (type === "tee") hole.mapTeePosition = worldPosition; else hole.mapBasketPosition = worldPosition;
  }
  const dx = (anchors.basket.x - anchors.tee.x) * currentCourseSize().width;
  const dy = -(anchors.basket.y - anchors.tee.y) * currentCourseSize().height;
  if (Math.hypot(dx, dy) > 1) anchors.heading = Math.atan2(dy, dx);
  anchors.calibrated = anchors.teeSet && anchors.basketSet;
  state.calibrating = "";
  byId("calibrate-tee").classList.remove("active"); byId("calibrate-basket").classList.remove("active");
  byId("map-tip").textContent = "Map alignment updated. Drag the tee and basket to reposition them.";
  byId("map-tip").classList.remove("complete"); paintMap(); saveState();
}
function mapPointer(event) { const rect = canvas.getBoundingClientRect(); return { x: event.clientX - rect.left, y: event.clientY - rect.top }; }
function paintMap() {
  if (!canvas || !ctx) return;
  const rect = canvas.getBoundingClientRect(); if (!rect.width || !rect.height) return;
  const ratio = window.devicePixelRatio || 1;
  canvas.width = Math.round(rect.width * ratio); canvas.height = Math.round(rect.height * ratio);
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  const w = rect.width, h = rect.height;
  ctx.clearRect(0, 0, w, h);
  const geometry = getMapGeometry(w, h);
  if (state.image) {
    ctx.fillStyle = "#20352a"; ctx.fillRect(0, 0, w, h);
    ctx.save(); ctx.globalAlpha = .91; ctx.drawImage(state.image, geometry.x, geometry.y, geometry.width, geometry.height); ctx.restore();
  } else drawSchematicBackground(w, h, geometry);
  ctx.save(); ctx.beginPath(); ctx.rect(0, 0, w, h); ctx.clip();
  drawMapGrid(geometry);
  if (!state.draft || !currentHole()) { ctx.restore(); return; }
  if (state.mapMode === "overview") drawCourseOverview(geometry); else drawHolePlan(geometry);
  ctx.restore();
  byId("zoom-level").textContent = Math.round(state.zoom * 100) + "%";
  updateMapTip();
}
function drawSchematicBackground(w, h, geometry) {
  const gradient = ctx.createLinearGradient(0, 0, w, h);
  gradient.addColorStop(0, "#243e30"); gradient.addColorStop(.52, "#1b3127"); gradient.addColorStop(1, "#1a2d24");
  ctx.fillStyle = gradient; ctx.fillRect(0, 0, w, h);
  ctx.save(); ctx.strokeStyle = "rgba(186,224,160,.09)"; ctx.lineWidth = 1;
  const seed = seedFrom((state.draft && state.draft.baseCourse || "DGM") + state.holeIndex);
  for (let i = 0; i < 11; i++) {
    const px = geometry.x + randomUnit(seed + i * 17) * geometry.width;
    const py = geometry.y + randomUnit(seed + i * 31) * geometry.height;
    const rx = geometry.width * (.13 + randomUnit(seed + i * 51) * .22), ry = geometry.height * (.1 + randomUnit(seed + i * 73) * .22);
    ctx.beginPath(); ctx.ellipse(px, py, rx, ry, randomUnit(seed + i * 97) * 2, 0, Math.PI * 2); ctx.stroke();
  }
  ctx.lineCap = "round"; ctx.beginPath();
  ctx.moveTo(geometry.x + geometry.width * .08, geometry.y + geometry.height * .85);
  ctx.bezierCurveTo(geometry.x + geometry.width * .26, geometry.y + geometry.height * .55, geometry.x + geometry.width * .42, geometry.y + geometry.height * .9, geometry.x + geometry.width * .6, geometry.y + geometry.height * .54);
  ctx.bezierCurveTo(geometry.x + geometry.width * .78, geometry.y + geometry.height * .16, geometry.x + geometry.width * .89, geometry.y + geometry.height * .53, geometry.x + geometry.width * .98, geometry.y + geometry.height * .18);
  ctx.lineWidth = Math.max(22, geometry.height * .085); ctx.strokeStyle = "rgba(83,139,83,.21)"; ctx.stroke();
  ctx.beginPath(); ctx.moveTo(geometry.x + geometry.width * .79, geometry.y + geometry.height * .1);
  ctx.bezierCurveTo(geometry.x + geometry.width * .72, geometry.y + geometry.height * .27, geometry.x + geometry.width * .96, geometry.y + geometry.height * .38, geometry.x + geometry.width * .78, geometry.y + geometry.height * .56);
  ctx.bezierCurveTo(geometry.x + geometry.width * .7, geometry.y + geometry.height * .66, geometry.x + geometry.width * .84, geometry.y + geometry.height * .76, geometry.x + geometry.width * .72, geometry.y + geometry.height * .87);
  ctx.lineWidth = Math.max(18, geometry.width * .025); ctx.strokeStyle = "rgba(78,137,156,.34)"; ctx.stroke(); ctx.restore();
}
function drawMapGrid(geometry) {
  if (state.zoom < 1.35) return;
  ctx.save(); ctx.strokeStyle = "rgba(210,231,200,.055)"; ctx.lineWidth = 1;
  const step = Math.max(36, Math.min(90, 54 * state.zoom));
  for (let x = ((geometry.x % step) + step) % step; x < canvas.clientWidth; x += step) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, canvas.clientHeight); ctx.stroke(); }
  for (let y = ((geometry.y % step) + step) % step; y < canvas.clientHeight; y += step) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(canvas.clientWidth, y); ctx.stroke(); }
  ctx.restore();
}
function drawHolePlan(geometry) {
  const hole = currentHole(), tee = markerMapPosition(hole, "tee"), basket = markerMapPosition(hole, "basket");
  const teePt = nToScreen(tee, geometry), basketPt = nToScreen(basket, geometry);
  ctx.save(); ctx.lineCap = "round"; ctx.beginPath(); ctx.moveTo(teePt.x, teePt.y);
  ctx.bezierCurveTo(teePt.x + (basketPt.x - teePt.x) * .3, teePt.y - 30, basketPt.x - (basketPt.x - teePt.x) * .28, basketPt.y + 20, basketPt.x, basketPt.y);
  ctx.strokeStyle = "rgba(184,239,114,.12)"; ctx.lineWidth = 25; ctx.stroke();
  ctx.strokeStyle = "rgba(184,239,114,.5)"; ctx.lineWidth = 1.2; ctx.setLineDash([5, 6]); ctx.stroke(); ctx.setLineDash([]);
  drawObZones(hole, geometry);
  if (state.pendingObPoints.length) {
    ctx.beginPath(); state.pendingObPoints.forEach(function (point, index) { const screen = nToScreen(point, geometry); if (!index) ctx.moveTo(screen.x, screen.y); else ctx.lineTo(screen.x, screen.y); });
    if (state.pointer && state.pointer.last) ctx.lineTo(state.pointer.last.x, state.pointer.last.y);
    ctx.strokeStyle = "#ff8c83"; ctx.lineWidth = 2; ctx.setLineDash([5, 4]); ctx.stroke(); ctx.setLineDash([]);
    state.pendingObPoints.forEach(function (point) { drawObVertex(nToScreen(point, geometry)); });
  }
  const teeWorldYaw = 90 - mapHeading(hole) * 180 / Math.PI + (isImportedMap() ? 0 : Number(hole.teeYawDegrees) || 0);
  if (hole.teeEnabled !== false) drawTee(teePt, teeWorldYaw, state.selectedMarker === "tee");
  if (hole.basketEnabled !== false) drawBasket(basketPt, hole.basketYawDegrees, state.selectedMarker === "basket");
  drawMapLabel(teePt, "TEE " + String(state.holeIndex + 1).padStart(2, "0"), "#c9f38d", -1);
  drawMapLabel(basketPt, "PIN " + String(state.holeIndex + 1).padStart(2, "0"), "#f4bd8d", 1);
  ctx.restore();
}
function drawCourseOverview(geometry) {
  const holes = state.draft.holes;
  ctx.save(); ctx.lineWidth = 1.5; ctx.strokeStyle = "rgba(192,221,172,.33)"; ctx.beginPath();
  holes.forEach(function (hole, index) { const tee = nToScreen(markerMapPosition(hole, "tee"), geometry); if (!index) ctx.moveTo(tee.x, tee.y); else ctx.lineTo(tee.x, tee.y); });
  ctx.stroke();
  holes.forEach(function (hole, index) {
    const teePt = nToScreen(markerMapPosition(hole, "tee"), geometry), basketPt = nToScreen(markerMapPosition(hole, "basket"), geometry);
    ctx.beginPath(); ctx.moveTo(teePt.x, teePt.y); ctx.lineTo(basketPt.x, basketPt.y);
    ctx.strokeStyle = "rgba(180,224,126,.35)"; ctx.setLineDash([3, 4]); ctx.stroke(); ctx.setLineDash([]);
    drawSmallTee(teePt, index + 1, index === state.holeIndex); drawSmallBasket(basketPt, index === state.holeIndex);
  });
  ctx.restore();
}
function drawObZones(hole, geometry) {
  (hole.obZones || []).forEach(function (zone, zoneIndex) {
    const points = (zone.points || []).map(function (point) { return nToScreen(obPointToMap(point, hole), geometry); });
    if (points.length < 3) return;
    ctx.beginPath(); points.forEach(function (point, index) { if (!index) ctx.moveTo(point.x, point.y); else ctx.lineTo(point.x, point.y); }); ctx.closePath();
    ctx.fillStyle = zoneIndex === state.selectedZone ? "rgba(241,120,114,.23)" : "rgba(241,120,114,.15)"; ctx.fill();
    ctx.strokeStyle = "#f17872"; ctx.lineWidth = zoneIndex === state.selectedZone ? 2 : 1.4; ctx.setLineDash([7, 4]); ctx.stroke(); ctx.setLineDash([]);
    if (zoneIndex === state.selectedZone) points.forEach(drawObVertex);
    if (zone.name) drawMapLabel(points[0], zone.name.toUpperCase(), "#ffb2ac", -1);
  });
}
function drawTee(point, angle, selected) {
  const r = 9; ctx.save(); ctx.shadowColor = "rgba(184,239,114,.55)"; ctx.shadowBlur = selected ? 18 : 10;
  ctx.fillStyle = "#b8ef72"; ctx.beginPath(); ctx.arc(point.x, point.y, r, 0, Math.PI * 2); ctx.fill(); ctx.shadowBlur = 0;
  ctx.strokeStyle = "#17301c"; ctx.lineWidth = 2; ctx.stroke(); ctx.translate(point.x, point.y); ctx.rotate(Number(angle || 0) * Math.PI / 180);
  ctx.fillStyle = "#284026"; ctx.beginPath(); ctx.moveTo(0, -4); ctx.lineTo(4.6, 5); ctx.lineTo(-4.6, 5); ctx.closePath(); ctx.fill(); ctx.restore();
  if (selected) drawSelectionRing(point, r + 5, "#d6ffab");
}
function drawBasket(point, angle, selected) {
  const r = 10; ctx.save(); ctx.shadowColor = "rgba(230,164,108,.5)"; ctx.shadowBlur = selected ? 18 : 11;
  ctx.fillStyle = "#d88f5e"; ctx.beginPath(); ctx.arc(point.x, point.y, r, 0, Math.PI * 2); ctx.fill(); ctx.shadowBlur = 0;
  ctx.strokeStyle = "#f2cc9c"; ctx.lineWidth = 1.5; ctx.stroke(); ctx.translate(point.x, point.y); ctx.rotate(Number(angle || 0) * Math.PI / 180);
  ctx.strokeStyle = "#fff0d5"; ctx.lineWidth = 1.6; ctx.beginPath(); ctx.arc(0, 0, 4.2, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(-6, 0); ctx.lineTo(6, 0); ctx.moveTo(0, -6); ctx.lineTo(0, 6); ctx.stroke(); ctx.restore();
  if (selected) drawSelectionRing(point, r + 5, "#ffd0a4");
}
function drawSelectionRing(point, radius, color) {
  ctx.save(); ctx.beginPath(); ctx.arc(point.x, point.y, radius, 0, Math.PI * 2);
  ctx.strokeStyle = color; ctx.lineWidth = 1; ctx.setLineDash([3, 3]); ctx.stroke(); ctx.restore();
}
function drawSmallTee(point, number, active) {
  ctx.save(); ctx.fillStyle = active ? "#c8f18c" : "#98c76c"; ctx.beginPath(); ctx.arc(point.x, point.y, active ? 7 : 5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = "#152218"; ctx.font = "9px Segoe UI"; ctx.textAlign = "center"; ctx.textBaseline = "middle"; ctx.fillText(String(number), point.x, point.y); ctx.restore();
}
function drawSmallBasket(point, active) {
  ctx.save(); ctx.fillStyle = active ? "#f1ad79" : "#c2855c"; ctx.beginPath(); ctx.arc(point.x, point.y, active ? 5 : 3.5, 0, Math.PI * 2); ctx.fill(); ctx.restore();
}
function drawObVertex(point) {
  ctx.save(); ctx.fillStyle = "#ffd0cd"; ctx.strokeStyle = "#a3403b"; ctx.lineWidth = 1.2;
  ctx.beginPath(); ctx.arc(point.x, point.y, 3.5, 0, Math.PI * 2); ctx.fill(); ctx.stroke(); ctx.restore();
}
function drawMapLabel(point, text, color, side) {
  ctx.save(); ctx.font = "500 8px Consolas, monospace";
  const width = ctx.measureText(text).width + 12, x = point.x - width / 2, y = point.y + (side < 0 ? -25 : 16);
  ctx.fillStyle = "rgba(15,22,16,.84)"; ctx.strokeStyle = "rgba(210,231,200,.14)"; roundedRect(ctx, x, y, width, 16, 3); ctx.fill(); ctx.stroke();
  ctx.fillStyle = color; ctx.textAlign = "center"; ctx.textBaseline = "middle"; ctx.fillText(text, point.x, y + 8); ctx.restore();
}
function updateMapTip() {
  const tip = byId("map-tip");
  if (state.drawingOb || state.calibrating) { tip.classList.add("complete"); return; }
  if (state.mapMode === "overview") {
    tip.textContent = "Course overview · select a hole in the left panel to edit its tee, basket and OB.";
    tip.classList.remove("complete"); return;
  }
  const anchors = mapAnchors(currentHole());
  tip.textContent = isImportedMap()
    ? "Imported map · drag markers or enter world X, Y and Z in the tee and basket panels."
    : anchors.calibrated
      ? "Drag the tee and basket to set positions · map scale " + state.mapWidth + " m across."
    : "Click Base tee and Base basket, then click their original locations to calibrate this hole.";
  tip.classList.toggle("complete", !!anchors.calibrated);
}
function startCalibration(type) {
  state.calibrating = type; state.drawingOb = false; state.pendingObPoints = [];
  byId("calibrate-tee").classList.toggle("active", type === "tee");
  byId("calibrate-basket").classList.toggle("active", type === "basket");
  byId("draw-ob").classList.remove("active");
  byId("map-tip").textContent = "Click the original " + (type === "tee" ? "tee pad" : "basket") + " location on this map.";
  byId("map-tip").classList.remove("complete");
}
function startDrawingOb() {
  state.drawingOb = true; state.calibrating = ""; state.pendingObPoints = [];
  byId("calibrate-tee").classList.remove("active"); byId("calibrate-basket").classList.remove("active");
  byId("draw-ob").classList.add("active"); byId("finish-ob").hidden = false; byId("ob-instruction").hidden = false;
  byId("map-tip").classList.add("complete"); state.inspector = "ob"; renderInspector(); paintMap();
}
function finishOb() {
  if (state.pendingObPoints.length < 3) { toast("An OB boundary needs at least three points."); return; }
  const hole = currentHole();
  const points = state.pendingObPoints.map(function (position) { return mapToOffset(position, obAnchor(hole), hole); });
  hole.obZones = Array.isArray(hole.obZones) ? hole.obZones : [];
  hole.obZones.push({ name: "OB zone " + (hole.obZones.length + 1), points: points });
  state.selectedZone = hole.obZones.length - 1; cancelOb(); state.inspector = "ob"; renderEditor(); toast("Out-of-bounds boundary added.");
}
function cancelOb() {
  state.drawingOb = false; state.pendingObPoints = [];
  byId("finish-ob").hidden = true; byId("ob-instruction").hidden = true;
  byId("draw-ob").classList.remove("active"); paintMap();
}
function nearestMarker(position, geometry) {
  const hole = currentHole(); let result = null;
  [["tee", hole.teeEnabled !== false], ["basket", hole.basketEnabled !== false]].forEach(function (entry) {
    if (!entry[1]) return;
    const screen = nToScreen(markerMapPosition(hole, entry[0]), geometry), distance = Math.hypot(position.x - screen.x, position.y - screen.y);
    if (distance < 21 && (!result || distance < result.distance)) result = { type: entry[0], distance: distance };
  });
  return result;
}
function onCanvasPointerDown(event) {
  if (!state.draft) return;
  const point = mapPointer(event), geometry = getMapGeometry(canvas.clientWidth, canvas.clientHeight), mapPoint = screenToN(point, geometry);
  if (state.mapMode === "overview") {
    let best = 0, bestDistance = Infinity;
    state.draft.holes.forEach(function (hole, index) {
      const screen = nToScreen(markerMapPosition(hole, "tee"), geometry), distance = Math.hypot(point.x - screen.x, point.y - screen.y);
      if (distance < bestDistance) { bestDistance = distance; best = index; }
    });
    if (bestDistance < 30) { state.holeIndex = best; state.mapMode = "design"; document.querySelectorAll(".map-tab").forEach(function (button) { button.classList.toggle("active", button.dataset.mapMode === "design"); }); renderEditor(); return; }
  }
  if (state.calibrating) { calibrateAt(mapPoint, state.calibrating); return; }
  if (state.drawingOb) { state.pendingObPoints.push(mapPoint); state.pointer = { last: point }; paintMap(); return; }
  const marker = nearestMarker(point, geometry);
  if (marker && state.mapMode === "design") {
    state.selectedMarker = marker.type; state.pointer = { type: "marker", marker: marker.type, pointerId: event.pointerId };
    canvas.setPointerCapture(event.pointerId); renderInspector(); paintMap(); return;
  }
  if (state.spaceDown || event.button === 1 || event.button === 2) {
    state.pointer = { type: "pan", start: point, pan: { x: state.pan.x, y: state.pan.y }, pointerId: event.pointerId };
    canvas.setPointerCapture(event.pointerId); return;
  }
  if (state.mapMode === "design") {
    const zones = currentHole().obZones || [];
    for (let i = zones.length - 1; i >= 0; i--) {
      const polygon = (zones[i].points || []).map(function (p) { return nToScreen(obPointToMap(p, currentHole()), geometry); });
      if (insidePolygon(point, polygon)) { state.selectedZone = i; state.inspector = "ob"; renderInspector(); paintMap(); return; }
    }
  }
}
function onCanvasPointerMove(event) {
  if (!state.draft) return;
  const point = mapPointer(event), geometry = getMapGeometry(canvas.clientWidth, canvas.clientHeight), mapped = screenToN(point, geometry);
  const offset = mapToOffset(mapped, obAnchor(currentHole()), currentHole());
  byId("cursor-coordinates").textContent = isImportedMap()
    ? mapNormToWorld(mapped).x.toFixed(1) + ", " + mapNormToWorld(mapped).z.toFixed(1) + " world"
    : offset.right.toFixed(1) + ", " + offset.forward.toFixed(1) + " m";
  if (state.pointer && state.pointer.type === "marker") { updateMarkerFromMap(currentHole(), state.pointer.marker, mapped); paintMap(); return; }
  if (state.pointer && state.pointer.type === "pan") {
    state.pan.x = state.pointer.pan.x + (point.x - state.pointer.start.x);
    state.pan.y = state.pointer.pan.y + (point.y - state.pointer.start.y); paintMap(); return;
  }
  if (state.drawingOb && state.pendingObPoints.length) { state.pointer = { last: point }; paintMap(); }
}
function onCanvasPointerUp(event) {
  if (state.pointer && state.pointer.type === "marker") {
    state.pointer = null; state.inspector = state.selectedMarker; renderInspector(); saveState(); paintMap();
  } else if (state.pointer && state.pointer.type === "pan") state.pointer = null;
  if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
}
function zoomAt(factor, point) {
  const old = state.zoom, next = clamp(old * factor, .65, 5);
  const x = point ? point.x : canvas.clientWidth / 2, y = point ? point.y : canvas.clientHeight / 2;
  state.pan.x = x - (x - state.pan.x) * next / old; state.pan.y = y - (y - state.pan.y) * next / old;
  state.zoom = next; paintMap();
}
function drawCardPreview() {
  if (!cardCtx || !state.draft || !currentHole()) return;
  const hole = currentHole(), theme = themes[state.cardTheme] || themes.moss, w = cardCanvas.width, h = cardCanvas.height;
  const gradient = cardCtx.createLinearGradient(0, 0, w, h);
  gradient.addColorStop(0, theme.dark); gradient.addColorStop(.58, theme.mid); gradient.addColorStop(1, theme.dark);
  cardCtx.fillStyle = gradient; cardCtx.fillRect(0, 0, w, h);
  cardCtx.save(); cardCtx.globalAlpha = .15;
  for (let i = 0; i < 8; i++) {
    cardCtx.beginPath(); cardCtx.ellipse(730 - i * 53, 185 + i * 29, 150 + i * 13, 95 + i * 12, -.27, 0, Math.PI * 2);
    cardCtx.strokeStyle = "#e1f2d3"; cardCtx.lineWidth = 2; cardCtx.stroke();
  }
  cardCtx.globalAlpha = .22; cardCtx.lineWidth = 44; cardCtx.lineCap = "round"; cardCtx.strokeStyle = theme.light;
  cardCtx.beginPath(); cardCtx.moveTo(0, 700); cardCtx.bezierCurveTo(225, 565, 430, 830, 900, 560); cardCtx.stroke(); cardCtx.restore();
  cardCtx.fillStyle = "rgba(12,18,14,.7)"; cardCtx.fillRect(42, 42, w - 84, h - 84);
  cardCtx.strokeStyle = "rgba(230,244,220,.25)"; cardCtx.lineWidth = 2; cardCtx.strokeRect(42, 42, w - 84, h - 84);
  cardCtx.fillStyle = theme.light; cardCtx.font = "500 23px Consolas, monospace"; cardCtx.fillText("DISC GOLF MASTERS", 84, 113);
  cardCtx.fillStyle = "rgba(224,237,215,.7)"; cardCtx.font = "500 17px Consolas, monospace";
  cardCtx.fillText(String(state.draft.cardSubtitle || "CUSTOM OFFLINE LAYOUT").toUpperCase().slice(0, 34), 84, 154);
  cardCtx.fillStyle = "#f0f5e9"; fitCanvasText(cardCtx, state.draft.name || "Custom Course", 84, 244, 710, 48);
  cardCtx.fillStyle = "rgba(233,242,225,.72)"; cardCtx.font = "500 18px Consolas, monospace";
  cardCtx.fillText(((originalCourse(state.draft.baseCourse) || {}).name || "ORIGINAL COURSE").toUpperCase() + "  /  HOLE " + String(state.holeIndex + 1).padStart(2, "0"), 84, 286);
  cardCtx.fillStyle = "rgba(12,18,14,.53)"; cardCtx.fillRect(84, 407, 732, 240);
  cardCtx.fillStyle = theme.light; cardCtx.font = "700 118px Segoe UI, sans-serif"; cardCtx.fillText(String(state.holeIndex + 1).padStart(2, "0"), 105, 536);
  cardCtx.fillStyle = "rgba(230,242,222,.63)"; cardCtx.font = "500 19px Consolas, monospace"; cardCtx.fillText("HOLE", 115, 582);
  cardCtx.strokeStyle = "rgba(230,244,220,.25)"; cardCtx.beginPath(); cardCtx.moveTo(345, 438); cardCtx.lineTo(345, 613); cardCtx.stroke();
  cardCtx.fillStyle = "#f2f6ed"; cardCtx.font = "600 27px Segoe UI, sans-serif";
  cardCtx.fillText("PAR  " + hole.par, 392, 492); cardCtx.fillText("DISTANCE  " + hole.lengthMeters + " m", 392, 548);
  cardCtx.fillStyle = theme.warm; cardCtx.fillText("SHOT LIMIT  " + (hole.shotLimit ? hole.shotLimit + " throws" : "OPEN"), 392, 605);
  cardCtx.fillStyle = "rgba(232,242,224,.79)"; cardCtx.font = "500 19px Segoe UI, sans-serif";
  wrapCanvasText(cardCtx, hole.cardNote || state.draft.description || "Designed for the next great round.", 84, 742, 710, 30, 3);
  cardCtx.fillStyle = "rgba(222,237,212,.72)"; cardCtx.font = "500 17px Consolas, monospace";
  cardCtx.fillText("TEE  " + (hole.teeVariant || "DEFAULT").toUpperCase() + "   /   BASKET  " + (hole.pinVariant || "DEFAULT").toUpperCase(), 84, 1008);
  cardCtx.fillStyle = theme.light; cardCtx.fillRect(84, 1057, 732, 2);
  cardCtx.fillStyle = "rgba(218,232,207,.5)"; cardCtx.font = "500 14px Consolas, monospace"; cardCtx.fillText("COURSE STUDIO   ·   OFFLINE COURSE", 84, 1104);
  const element = byId("card-preview");
  if (element) { const previewCtx = element.getContext("2d"); previewCtx.clearRect(0, 0, element.width, element.height); previewCtx.drawImage(cardCanvas, 0, 0); }
}
function fitCanvasText(context, text, x, y, maxWidth, fontSize) {
  let size = fontSize; context.font = "700 " + size + "px Segoe UI, sans-serif";
  while (context.measureText(text).width > maxWidth && size > 20) { size -= 1; context.font = "700 " + size + "px Segoe UI, sans-serif"; }
  context.fillText(text, x, y);
}
function wrapCanvasText(context, text, x, y, maxWidth, lineHeight, maxLines) {
  const words = String(text || "").split(/\s+/); let line = "", lines = [];
  words.forEach(function (word) {
    const test = line ? line + " " + word : word;
    if (context.measureText(test).width > maxWidth && line) { lines.push(line); line = word; } else line = test;
  });
  if (line) lines.push(line); lines.slice(0, maxLines).forEach(function (value, index) { context.fillText(value, x, y + lineHeight * index); });
}
