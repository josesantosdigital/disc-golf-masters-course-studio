"use strict";
const assert = require("node:assert/strict"), fs = require("node:fs"), path = require("node:path"), vm = require("node:vm"), test = require("node:test");
const read = name => fs.readFileSync(path.join(__dirname, "../CourseEditor", name), "utf8");

test("new and legacy holes default off and export independent visibility settings", () => {
  const canvas = { getContext: () => ({}), toDataURL: () => "data:image/png;base64,AA==" };
  const context = vm.createContext({
    window: {}, document: { getElementById: () => canvas, createElement: () => ({ ...canvas }) },
    drawCardPreview: () => {}, slug: value => value, console
  });
  vm.runInContext(read("app-core.js"), context);
  vm.runInContext(read("app-files.js"), context);
  assert.equal(vm.runInContext("defaultHole(null, 0, 2, 'CanonPark').teeEnabled", context), false);
  assert.equal(vm.runInContext("defaultHole(null, 0, 2, 'CanonPark').cardEnabled", context), false);
  vm.runInContext("state.draft=normalizeCourse({id:'test',baseCourse:'CanonPark',holes:[{par:3,lengthMeters:60},{par:4,lengthMeters:90,teeEnabled:true,cardEnabled:false},{par:3,lengthMeters:70,teeEnabled:false,cardEnabled:true}]});", context);
  const result = JSON.parse(vm.runInContext("exportDocument().json", context));
  assert.deepEqual(result.holes.map(h => [h.teeEnabled, h.cardEnabled]), [[false, false], [true, false], [false, true]]);
});

test("the tee position remains selectable in the editor when its in-game artwork is hidden", () => {
  const source = read("app-map.js"), start = source.indexOf("function nearestMarker("), end = source.indexOf("\nfunction onCanvasPointerDown(", start);
  assert.ok(start >= 0 && end > start);
  const context = vm.createContext({
    currentHole: () => ({ teeEnabled: false, basketEnabled: true }),
    markerMapPosition: (_, type) => type === "tee" ? { x: 10, y: 10 } : { x: 90, y: 90 },
    nToScreen: point => point, Math
  });
  vm.runInContext(source.slice(start, end), context);
  assert.equal(vm.runInContext("nearestMarker({x:10,y:10},{})", context).type, "tee");
});
