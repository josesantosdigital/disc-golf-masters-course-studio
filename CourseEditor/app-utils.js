"use strict";

function insidePolygon(point, polygon) {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const a = polygon[i], b = polygon[j];
    const intersect = ((a.y > point.y) !== (b.y > point.y)) && (point.x < (b.x - a.x) * (point.y - a.y) / ((b.y - a.y) || .0001) + a.x);
    if (intersect) inside = !inside;
  }
  return inside;
}
function roundedRect(context, x, y, width, height, radius) {
  const r = Math.min(radius, width / 2, height / 2);
  context.beginPath(); context.moveTo(x + r, y); context.arcTo(x + width, y, x + width, y + height, r);
  context.arcTo(x + width, y + height, x, y + height, r); context.arcTo(x, y + height, x, y, r);
  context.arcTo(x, y, x + width, y, r); context.closePath();
}
function seedFrom(value) {
  let seed = 2166136261;
  for (let i = 0; i < value.length; i++) { seed ^= value.charCodeAt(i); seed = Math.imul(seed, 16777619); }
  return seed >>> 0;
}
function randomUnit(value) {
  let x = (value ^ (value >>> 15)) >>> 0; x = Math.imul(x, 2246822519) >>> 0;
  x ^= x >>> 13; x = Math.imul(x, 3266489917) >>> 0; x ^= x >>> 16; return (x >>> 0) / 4294967295;
}
function clamp(value, min, max) { return Math.max(min, Math.min(max, value)); }
function round1(value) { return Math.round(value * 10) / 10; }
function escapeHtml(value) {
  return String(value == null ? "" : value).replace(/[&<>"']/g, function (character) {
    return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character];
  });
}
function escapeAttr(value) { return escapeHtml(value); }
