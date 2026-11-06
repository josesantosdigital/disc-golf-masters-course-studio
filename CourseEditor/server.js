"use strict";

const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const { listInstalledMaps } = require("./map-catalog");

const root = path.resolve(__dirname);
const configuredPluginRoot = process.env.DGM_COURSE_EDITOR_PLUGIN_DIR;
if (configuredPluginRoot && !path.isAbsolute(configuredPluginRoot)) {
  throw new Error("DGM_COURSE_EDITOR_PLUGIN_DIR must be an absolute plugin data folder path.");
}
const pluginRoot = path.resolve(configuredPluginRoot || path.resolve(root, ".."));
if (configuredPluginRoot && (!fs.existsSync(pluginRoot) || !fs.statSync(pluginRoot).isDirectory())) {
  throw new Error("DGM_COURSE_EDITOR_PLUGIN_DIR must point to an existing plugin data folder.");
}
const pluginEditorRoot = path.resolve(pluginRoot, "CourseEditor");
const courseFiles = path.resolve(pluginRoot, "Courses");
const mapsRoot = path.resolve(pluginRoot, "Maps");
const port = Number(process.env.DGM_COURSE_EDITOR_PORT) || 4170;
const readOnly = process.env.DGM_COURSE_EDITOR_READ_ONLY === "1";
const contentTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".svg": "image/svg+xml"
};

const server = http.createServer(function (request, response) {
  let pathname;
  try {
    pathname = decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname);
  } catch (error) {
    response.writeHead(400).end("Invalid URL");
    return;
  }

  const installPrefix = "/api/install/";
  if (request.method === "PUT" && pathname.startsWith(installPrefix)) {
    if (readOnly) {
      response.writeHead(403, { "Content-Type": "text/plain; charset=utf-8" }).end("Course Studio server is in read-only mode.");
      return;
    }
    const relative = pathname.slice(installPrefix.length).replace(/\\/g, "/");
    const parts = relative.split("/");
    const top = parts[0];
    const extension = path.extname(relative).toLowerCase();
    const allowedCourseFile = top === "Courses" && parts.length === 2 && [".json", ".png"].includes(extension);
    const allowedMapFile = top === "Maps" && parts.length >= 3 && [".bundle", ".unity3d", ".assetbundle"].includes(extension);
    if (!relative || path.isAbsolute(relative) || parts.some(function (part) { return !part || part === "." || part === ".."; }) || (!allowedCourseFile && !allowedMapFile)) {
      response.writeHead(400, { "Content-Type": "text/plain; charset=utf-8" }).end("Only course JSON/PNG files and map bundles can be installed.");
      return;
    }

    const destination = path.resolve(pluginRoot, relative);
    if (destination !== pluginRoot && !destination.startsWith(pluginRoot + path.sep)) {
      response.writeHead(403).end("Forbidden");
      return;
    }
    fs.mkdir(path.dirname(destination), { recursive: true }, function (directoryError) {
      if (directoryError) {
        response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" }).end("Could not create the package folder.");
        return;
      }
      const output = fs.createWriteStream(destination);
      output.on("error", function () {
        if (!response.headersSent) response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" });
        response.end("Could not write the package file.");
      });
      output.on("finish", function () {
        response.writeHead(204).end();
      });
      request.on("error", function () { output.destroy(); });
      request.pipe(output);
    });
    return;
  }

  if (request.method !== "GET" && request.method !== "HEAD") {
    response.writeHead(405, { "Allow": "GET, HEAD, PUT" }).end("Method not allowed");
    return;
  }

  if (request.method === "GET" && pathname === "/api/courses") {
    fs.readdir(courseFiles, { withFileTypes: true }, function (directoryError, entries) {
      if (directoryError) {
        response.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
        response.end("[]");
        return;
      }

      const files = entries.filter(function (entry) {
        return entry.isFile() && path.extname(entry.name).toLowerCase() === ".json";
      });
      Promise.all(files.map(function (entry) {
        return fs.promises.readFile(path.join(courseFiles, entry.name), "utf8")
          .then(function (contents) {
            const course = JSON.parse(contents);
            const courseId = typeof course.id === "string" && /^[a-z0-9_-]{1,48}$/i.test(course.id)
              ? course.id.toLowerCase() : "";
            const customMapPreview = courseId ? "imported-" + courseId + ".png" : "";
            const customMapPreviewPath = customMapPreview
              ? path.join(pluginEditorRoot, "assets", "maps", customMapPreview) : "";
            const savedPreview = typeof course.mapPreviewImage === "string" &&
              path.basename(course.mapPreviewImage) === course.mapPreviewImage &&
              path.extname(course.mapPreviewImage).toLowerCase() === ".png" &&
              fs.existsSync(path.join(courseFiles, course.mapPreviewImage))
              ? "/course-files/" + encodeURIComponent(course.mapPreviewImage) : "";
            return {
              fileName: entry.name,
              name: typeof course.name === "string" && course.name ? course.name : entry.name,
              holeCount: Array.isArray(course.holes) ? course.holes.length : 0,
              importedMap: !!course.mapScene,
              previewUrl: savedPreview || (customMapPreviewPath && fs.existsSync(customMapPreviewPath)
                ? "/assets/maps/" + encodeURIComponent(customMapPreview) : "")
            };
          })
          .catch(function () { return null; });
      })).then(function (courses) {
        courses = courses.filter(Boolean).sort(function (left, right) { return left.name.localeCompare(right.name); });
        response.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
        response.end(JSON.stringify(courses));
      }).catch(function () {
        response.writeHead(500, { "Content-Type": "application/json; charset=utf-8" });
        response.end("[]");
      });
    });
    return;
  }

  if (request.method === "GET" && pathname === "/api/maps") {
    listInstalledMaps({ pluginRoot: pluginRoot, courseFiles: courseFiles, mapsRoot: mapsRoot, editorRoot: pluginEditorRoot }).then(function (maps) {
      response.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
      response.end(JSON.stringify(maps));
    }).catch(function (error) {
      process.stderr.write("Could not scan installed map packages: " + error.message + "\n");
      response.writeHead(500, { "Content-Type": "application/json; charset=utf-8" }).end("[]");
    });
    return;
  }

  const isCourseFile = pathname.startsWith("/course-files/");
  const isPluginEditorAsset = pathname === "/catalog.js" || pathname.startsWith("/assets/maps/");
  const relative = isCourseFile
    ? pathname.slice("/course-files/".length)
    : pathname === "/" ? "index.html" : pathname.replace(/^[/\\]+/, "");
  const allowedRoot = isCourseFile ? courseFiles : isPluginEditorAsset ? pluginEditorRoot : root;
  const target = path.resolve(allowedRoot, relative);
  if (target !== allowedRoot && !target.startsWith(allowedRoot + path.sep)) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  fs.stat(target, function (statError, stat) {
    if (statError || !stat.isFile()) {
      response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" }).end("Not found");
      return;
    }
    response.writeHead(200, {
      "Content-Type": contentTypes[path.extname(target).toLowerCase()] || "application/octet-stream",
      "Cache-Control": path.basename(target) === "catalog.js" ? "no-store" : "no-cache",
      "X-Content-Type-Options": "nosniff"
    });
    fs.createReadStream(target).pipe(response);
  });
});

server.listen(port, "127.0.0.1", function () {
  process.stdout.write("Course Studio is serving at http://127.0.0.1:" + port + "\n");
});
