"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { Readable } = require("node:stream");
const test = require("node:test");
const vm = require("node:vm");

const projectRoot = path.resolve(__dirname, "..");
const editorSource = path.join(projectRoot, "CourseEditor");
const editorFilesSource = path.join(editorSource, "app-files.js");

function createServerHarness(editorHostRoot, dataRoot = editorHostRoot, readOnly = false) {
  const editorRoot = path.join(editorHostRoot, "CourseEditor");
  fs.mkdirSync(editorRoot, { recursive: true });
  fs.mkdirSync(path.join(dataRoot, "Courses"), { recursive: true });
  fs.mkdirSync(path.join(dataRoot, "Maps"), { recursive: true });
  fs.copyFileSync(path.join(editorSource, "server.js"), path.join(editorRoot, "server.js"));
  fs.copyFileSync(path.join(editorSource, "map-catalog.js"), path.join(editorRoot, "map-catalog.js"));
  let requestHandler;
  const httpStub = {
    createServer(handler) {
      requestHandler = handler;
      return { listen: function () { } };
    }
  };
  const environment = { DGM_COURSE_EDITOR_PORT: "4170" };
  if (dataRoot !== editorHostRoot) environment.DGM_COURSE_EDITOR_PLUGIN_DIR = dataRoot;
  if (readOnly) environment.DGM_COURSE_EDITOR_READ_ONLY = "1";
  const serverContext = vm.createContext({
    require: function (name) {
      if (name === "node:http") return httpStub;
      if (name === "./map-catalog") return require(path.join(editorRoot, "map-catalog.js"));
      return require(name);
    },
    __dirname: editorRoot,
    process: { env: environment, stdout: { write: function () { } }, stderr: { write: function () { } } },
    URL: URL
  });
  vm.runInContext(fs.readFileSync(path.join(editorRoot, "server.js"), "utf8"), serverContext, { filename: "server.js" });

  return {
    async fetch(url, options) {
      options = options || {};
      const requestUrl = new URL(url, "http://127.0.0.1");
      let body = Buffer.alloc(0);
      if (options.body && typeof options.body.arrayBuffer === "function") {
        body = Buffer.from(await options.body.arrayBuffer());
      } else if (options.body !== undefined) {
        body = Buffer.from(String(options.body));
      }
      const request = Readable.from(body.length ? [body] : []);
      request.method = options.method || "GET";
      request.url = requestUrl.pathname + requestUrl.search;
      request.headers = {};

      return await new Promise(function (resolve, reject) {
        const responseChunks = [];
        const response = {
          statusCode: 200,
          headers: {},
          headersSent: false,
          writeHead(status, headers) {
            this.statusCode = status;
            this.headers = headers || {};
            this.headersSent = true;
            return this;
          },
          write(value) {
            responseChunks.push(Buffer.isBuffer(value) ? value : Buffer.from(String(value)));
            return true;
          },
          on() { return this; },
          once() { return this; },
          emit() { return false; },
          end(value) {
            if (value !== undefined) responseChunks.push(Buffer.isBuffer(value) ? value : Buffer.from(String(value)));
            const text = Buffer.concat(responseChunks).toString("utf8");
            resolve({
              status: this.statusCode,
              ok: this.statusCode >= 200 && this.statusCode < 300,
              text: async function () { return text; },
              json: async function () { return JSON.parse(text); }
            });
            return this;
          }
        };
        try { requestHandler(request, response); }
        catch (error) { reject(error); }
      });
    }
  };
}

function makeState() {
  const dependencyPath = "Maps/export-fixture/dependencies/shared.bundle";
  return {
    state: {
      draft: {
        id: "export-fixture",
        name: "Export Fixture",
        baseCourse: "CanonPark",
        description: "Synthetic editor export fixture",
        holes: [{
          par: 3,
          lengthMeters: 60,
          mapTeePosition: { x: 1, y: 2, z: 3 },
          mapBasketPosition: { x: 4, y: 5, z: 6 }
        }],
        mapScene: {
          scenePath: "Assets/Scenes/ExportFixture.unity",
          bundlePath: "Maps/export-fixture/scene.bundle",
          dependencyBundlePaths: [dependencyPath]
        },
        layoutGroundColliderExclusions: ["Terrain"],
        mapLighting: { realtimeMapShadows: true }
      },
      mapWidth: 400,
      holeIndex: 0,
      sceneBundleFile: new Blob(["scene bundle fixture"]),
      dependencyFiles: [{ path: dependencyPath, file: new Blob(["dependency fixture"]) }],
      mapPreviewDataUrl: "",
      createdMapLayoutIds: new Set(),
      installedMaps: []
    },
    dependencyPath: dependencyPath
  };
}

function makeDirectory(name) {
  const directories = new Map();
  const files = new Map();
  return {
    name: name,
    directories: directories,
    files: files,
    async getDirectoryHandle(childName, options) {
      if (!directories.has(childName)) {
        if (!options || !options.create) throw new Error("Directory not found: " + childName);
        directories.set(childName, makeDirectory(childName));
      }
      return directories.get(childName);
    },
    async getFileHandle(fileName, options) {
      if ((!files.has(fileName)) && (!options || !options.create)) throw new Error("File not found: " + fileName);
      return {
        async createWritable() {
          let contents;
          return {
            async write(value) { contents = value; },
            async close() { files.set(fileName, contents); }
          };
        }
      };
    }
  };
}

function runExportClient(options) {
  const fixture = makeState();
  const toasts = [];
  const downloads = [];
  let objectUrlId = 0;
  const link = {
    download: "",
    href: "",
    click() { downloads.push(this.download); },
    remove() { }
  };
  const context = vm.createContext({
    state: fixture.state,
    Blob: Blob,
    Uint8Array: Uint8Array,
    atob: atob,
    fetch: options.fetch,
    window: options.window || {},
    document: options.document || {
      createElement() { return Object.assign({}, link); },
      body: { appendChild() { } }
    },
    URL: {
      createObjectURL() { objectUrlId++; return "blob:course-studio-test-" + objectUrlId; },
      revokeObjectURL() { }
    },
    setTimeout: options.setTimeout || setTimeout,
    console: { warn() { } },
    slug: function (value) { return String(value).toLowerCase().replace(/[^a-z0-9_-]+/g, "-"); },
    isImportedMap: function () { return !!fixture.state.draft.mapScene; },
    validRelativePackagePath: function (value) {
      const normalized = String(value || "").replace(/\\/g, "/");
      return !!normalized && !normalized.startsWith("/") && !/^[a-z]:/i.test(normalized) &&
        normalized.split("/").every(function (part) { return part && part !== "." && part !== ".."; });
    },
    mapNormToWorld: function () { return { x: 0, y: 0, z: 0 }; },
    mapAnchors: function () { return { tee: [0, 0], basket: [1, 1] }; },
    drawCardPreview: function () { },
    cardCanvas: { toDataURL: function () { return "data:image/png;base64,aGVsbG8="; } },
    defaultMapLighting: function () { return {}; },
    renderEditor: function () { },
    toast: function (message) { toasts.push(message); },
    openEditor: function () { },
    normalizeCourse: function (course) { return course; },
    byId: function () { return { value: "", focus: function () { }, open: false }; }
  });
  vm.runInContext(fs.readFileSync(editorFilesSource, "utf8"), context, { filename: "app-files.js" });
  return {
    state: fixture.state,
    dependencyPath: fixture.dependencyPath,
    toasts: toasts,
    downloads: downloads,
    exportCourse: function () { return vm.runInContext("exportCourse()", context); }
  };
}

test("Course Editor exports through direct install and both fallback flows", async function () {
  const tempRoot = path.join(projectRoot, ".course-editor-export-test-" + require("node:crypto").randomUUID());
  const pluginRoot = path.join(tempRoot, "plugin");
  try {
    fs.mkdirSync(tempRoot, { recursive: true });
    const server = createServerHarness(pluginRoot);

    const direct = runExportClient({
      fetch: function (url, options) { return server.fetch(url, options); }
    });
    direct.state.draft.mapPreviewWorldBounds = { minX: -20, maxX: 20, minZ: -10, maxZ: 10 };
    direct.state.draft._legacyObPreviewBounds = { minX: -18, maxX: 18, minZ: -9, maxZ: 9 };
    await direct.exportCourse();

    const coursePath = path.join(pluginRoot, "Courses", "export-fixture.json");
    const cardPath = path.join(pluginRoot, "Courses", "export-fixture-hole-01.png");
    const scenePath = path.join(pluginRoot, "Maps", "export-fixture", "scene.bundle");
    const dependencyPath = path.join(pluginRoot, direct.dependencyPath.replace(/\//g, path.sep));
    assert.ok(fs.existsSync(coursePath), "direct install writes course JSON");
    assert.ok(fs.existsSync(cardPath), "direct install writes the hole card");
    assert.deepEqual(JSON.parse(fs.readFileSync(coursePath, "utf8")).layoutGroundColliderExclusions, ["Terrain"],
      "direct export preserves auto-layout ground collider exclusions");
    const exported = JSON.parse(fs.readFileSync(coursePath, "utf8"));
    assert.deepEqual(exported.mapPreviewWorldBounds, direct.state.draft.mapPreviewWorldBounds,
      "export preserves the overview's world coverage");
    assert.deepEqual(exported.obPreviewRepairBounds, direct.state.draft._legacyObPreviewBounds,
      "export preserves the option to repair older OB drawings later");
    assert.equal(fs.readFileSync(scenePath, "utf8"), "scene bundle fixture");
    assert.equal(fs.readFileSync(dependencyPath, "utf8"), "dependency fixture");
    assert.match(direct.toasts.at(-1), /Installed 4 course file/);

    const mapsResponse = await server.fetch("/api/maps");
    const maps = await mapsResponse.json();
    assert.equal(maps.length, 1, "the real server map scan sees the installed export");
    assert.equal(maps[0].id, "export-fixture");
    assert.equal(maps[0].configured, true);
    assert.equal(maps[0].bundleExists, true);
    assert.equal(maps[0].dependencies[0].exists, true);

    const rejectedNestedCourse = await server.fetch("/api/install/Courses/nested/course.json", { method: "PUT", body: "blocked" });
    const rejectedDll = await server.fetch("/api/install/Config/plugin.dll", { method: "PUT", body: "blocked" });
    assert.equal(rejectedNestedCourse.status, 400, "install API rejects nested course paths");
    assert.equal(rejectedDll.status, 400, "install API rejects non-package extensions");

    const fallbackRoot = makeDirectory("Downloads");
    const folderFallback = runExportClient({
      fetch: async function () { return { ok: false, text: async function () { return "server unavailable"; } }; },
      window: { showDirectoryPicker: async function () { return fallbackRoot; } }
    });
    await folderFallback.exportCourse();
    const courseFolder = fallbackRoot.directories.get("Courses");
    const mapsFolder = fallbackRoot.directories.get("Maps");
    assert.ok(courseFolder.files.has("export-fixture.json"), "folder fallback writes the course JSON");
    assert.ok(courseFolder.files.has("export-fixture-hole-01.png"), "folder fallback writes the hole card");
    assert.ok(mapsFolder.directories.get("export-fixture").files.has("scene.bundle"), "folder fallback writes the scene bundle");
    assert.ok(mapsFolder.directories.get("export-fixture").directories.get("dependencies").files.has("shared.bundle"), "folder fallback writes dependencies");
    assert.match(folderFallback.toasts.at(-1), /Course package exported/);

    const downloadFallback = runExportClient({
      fetch: async function () { return { ok: false, text: async function () { return "server unavailable"; } }; },
      setTimeout: function (callback) { callback(); return 0; }
    });
    await downloadFallback.exportCourse();
    assert.deepEqual(downloadFallback.downloads.sort(), [
      "export-fixture-hole-01.png", "export-fixture.json", "scene.bundle", "shared.bundle"
    ].sort(), "download fallback emits course, image, scene, and dependency files");
    assert.match(downloadFallback.toasts.at(-1), /Downloaded package files/);
  } finally {
    const projectPrefix = projectRoot + path.sep;
    const cleanupTarget = path.resolve(tempRoot);
    if (!cleanupTarget.startsWith(projectPrefix)) throw new Error("Refusing to remove an export fixture outside the project directory.");
    fs.rmSync(cleanupTarget, { recursive: true, force: true });
  }
});

test("source editor server can browse and export to a selected installed plugin folder", async function () {
  const tempRoot = path.join(projectRoot, ".course-editor-data-root-test-" + require("node:crypto").randomUUID());
  const editorHostRoot = path.join(tempRoot, "source-project");
  const dataRoot = path.join(tempRoot, "installed-plugin");
  try {
    const courseFiles = path.join(dataRoot, "Courses");
    const scenePath = path.join(dataRoot, "Maps", "installed-fixture", "scene.bundle");
    const installedEditorRoot = path.join(dataRoot, "CourseEditor");
    const installedPreviewPath = path.join(installedEditorRoot, "assets", "maps", "imported-installed-fixture.png");
    fs.mkdirSync(path.dirname(scenePath), { recursive: true });
    fs.mkdirSync(path.dirname(installedPreviewPath), { recursive: true });
    fs.mkdirSync(courseFiles, { recursive: true });
    fs.writeFileSync(scenePath, "synthetic scene bundle");
    fs.writeFileSync(installedPreviewPath, "installed preview fixture");
    fs.writeFileSync(path.join(courseFiles, "installed-fixture.json"), JSON.stringify({
      id: "installed-fixture",
      name: "Installed Fixture",
      holes: [{ mapTeePosition: { x: 1, y: 2, z: 3 }, mapBasketPosition: { x: 4, y: 5, z: 6 } }],
      mapScene: {
        scenePath: "Assets/Scenes/InstalledFixture.unity",
        bundlePath: "Maps/installed-fixture/scene.bundle",
        dependencyBundlePaths: []
      }
    }));

    const server = createServerHarness(editorHostRoot, dataRoot, true);
    fs.writeFileSync(path.join(editorHostRoot, "CourseEditor", "catalog.js"), "source checkout catalog");
    fs.writeFileSync(path.join(installedEditorRoot, "catalog.js"), "installed course catalog");
    const coursesResponse = await server.fetch("/api/courses");
    const courses = await coursesResponse.json();
    assert.equal(courses.length, 1);
    assert.equal(courses[0].fileName, "installed-fixture.json");

    const mapsResponse = await server.fetch("/api/maps");
    const maps = await mapsResponse.json();
    assert.equal(maps.length, 1);
    assert.equal(maps[0].id, "installed-fixture");
    assert.equal(maps[0].bundleExists, true);
    assert.equal(maps[0].explicitPositionHoleCount, 1);
    assert.equal(maps[0].previewUrl, "/assets/maps/imported-installed-fixture.png");

    const catalogResponse = await server.fetch("/catalog.js");
    assert.match(await catalogResponse.text(), /installed course catalog/,
      "the selected plugin folder supplies its generated original-course catalog");
    const previewResponse = await server.fetch(maps[0].previewUrl);
    assert.equal(await previewResponse.text(), "installed preview fixture",
      "the selected plugin folder supplies generated map previews");

    const writeResponse = await server.fetch("/api/install/Courses/source-editor-export.json", {
      method: "PUT",
      body: JSON.stringify({ id: "source-editor-export", holes: [] })
    });
    assert.equal(writeResponse.status, 403, "read-only review mode blocks package writes");
    assert.equal(fs.existsSync(path.join(courseFiles, "source-editor-export.json")), false,
      "read-only review mode leaves the selected plugin data folder unchanged");
    assert.equal(fs.existsSync(path.join(editorHostRoot, "Courses", "source-editor-export.json")), false,
      "exports do not write into the source editor checkout");

    const writableEditorHost = path.join(tempRoot, "writable-source-project");
    const writableServer = createServerHarness(writableEditorHost, dataRoot);
    const writableResponse = await writableServer.fetch("/api/install/Courses/source-editor-export.json", {
      method: "PUT",
      body: JSON.stringify({ id: "source-editor-export", holes: [] })
    });
    assert.equal(writableResponse.status, 204);
    assert.equal(fs.existsSync(path.join(courseFiles, "source-editor-export.json")), true,
      "writable source editor exports to the selected plugin data folder");
    assert.equal(fs.existsSync(path.join(writableEditorHost, "Courses", "source-editor-export.json")), false,
      "selected data-root exports do not write into the source editor checkout");
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});
