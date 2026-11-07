# Player guide

## Requirements

- Windows PC installation of Disc Golf Masters.
- BepInEx 6 IL2CPP installed for the same game installation.
- Node.js to launch the browser editor with `Launch-Course-Editor.ps1`.
- PowerShell on Windows for the editor launcher and the included import tools.

The release does not include BepInEx, game files, or map bundles. Install the **BepInEx 6 Unity IL2CPP Windows x64** build into the same game folder first, following the [official BepInEx IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html). Course Studio does not install or update BepInEx. This project has been verified with BepInEx `6.0.0-be.791` on DGM build `26127348`; other versions/builds are not yet verified. See [supported environment](SUPPORTED_ENVIRONMENT.md).

## Install the plugin

1. Close Disc Golf Masters.
2. Extract the release archive to a folder outside the game installation.
3. Open PowerShell in that extracted folder and run the included installer, replacing the example path with your game folder:

   ~~~powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CourseStudio.ps1 -GameRoot 'C:\Games\Disc Golf Masters'
   ~~~

4. Start the game and open the offline course selector once. The loader exports built-in course metadata and map previews for Course Studio.

The installer checks for BepInEx 6 IL2CPP and copies the plugin/editor files. On updates it preserves existing `Courses`, `Maps`, `CourseEditor/catalog.js`, `CourseEditor/assets/maps`, and custom personal-best data. It adds the bundled sample course only if that filename does not already exist. The release archive is marked DRAFT until release gates are closed; installing a local review build requires adding `-AllowDraft` to the command. Do not redistribute a DRAFT archive.

The plugin reads custom course definitions from:

~~~text
BepInEx/plugins/CustomCourseLoader/Courses
~~~

Imported map bundles live under:

~~~text
BepInEx/plugins/CustomCourseLoader/Maps/<course-id>
~~~

## Launch Course Studio

Run `BepInEx/plugins/CustomCourseLoader/CourseEditor/Launch-Course-Editor.ps1`. It starts a local server bound to `127.0.0.1`, selects an available port, waits for the page to respond, and then opens the editor in your browser. If the browser does not open automatically, the launcher prints the working local address. The server serves the editor and writes course files into this plugin folder; it does not upload course data to a remote service.

Choose **Original courses** to start from one of the game's built-in courses. Choose **Installed maps** to browse imported course packages in `Courses` and Unity scene bundles in `Maps`. The explorer also keeps packages with missing paths visible so you can open their JSON and repair the setup. Search by map, course ID, scene, or bundle path; use the status filter to narrow the list to ready maps, setup-needed packages, or missing files. Select **Scan maps** after adding bundles while Course Studio is open. Use **Edit course** to change a saved layout or **New layout** to create a variation.

**READY TO EDIT** means the scene bundle, scene path, and listed dependencies are present. The separate amber **AUTHORING CHECK** calls out default overview bounds and holes whose tee/basket positions are missing or planned automatically. Calibrate the bounds and review every placement before treating a map as a finished course.

The Original courses tab lists built-in courses exported by the installed DGM build. Launch the game and open its offline course selector once if the list has not been generated yet. The Installed maps tab browses imported map packages in the plugin folder. The overview map is zoomable and pannable. In the editor, drag tee and basket markers, then use the inspector to set exact X/Y/Z positions (including height) and yaw. Use the hole settings to edit par, distance, and shot limit. Use the OB tab to draw boundary polygons. Use the Card tab to customize the card displayed by the tee.

## Export and load a course

1. Select **Export to game** in Course Studio.
2. When the local launcher/server is active, the editor installs course JSON, card PNGs, and selected bundle files into the plugin folder.
3. Restart Disc Golf Masters to load the updated course registry.
4. Open the offline course selector and choose the custom course card.

If direct install is unavailable, the editor offers a folder picker or downloads the files. In the folder picker, choose the `BepInEx/plugins/CustomCourseLoader` folder; the editor creates or updates `Courses` and the required `Maps` paths beneath it. If the browser only downloads files, move the course JSON and PNGs into `Courses` and imported scene bundles into the paths named in `mapScene`. Then restart the game.

Exporting a course with an existing ID replaces that course JSON and its generated card files. Choose a new ID to keep the previous course as a separate entry. Back up `Courses` and `Maps` before bulk changes.

## Share a course

For a custom layout on an original DGM course, share the course JSON and every course or hole-card image it references. The recipient needs Course Studio and the same built-in course, but no copy of DGM's map assets.

For an imported map, the course JSON points to a scene bundle and any dependency bundles under `Maps`. Share those files only when the source author and applicable terms allow redistribution. Otherwise, share the source mod page, the scene name and path, and the course JSON separately. Each recipient must obtain their own compatible PC map files, convert or install them locally, and ensure the bundle paths in the JSON match their files. A source link by itself does not supply the scene bundle or grant redistribution rights.

To package one course as its own ZIP, use `Tools/New-CourseModPackage.ps1` from a source checkout. It gathers that course's JSON, referenced card images, and (only when explicitly enabled) the imported scene and dependencies. Each course ZIP is independent, so authors can publish a loader/editor package and course packages separately. Recipients extract the ZIP and run its `Install-CourseModPackage.ps1` after installing Course Studio. Imported bundles should be included only with redistribution permission; the package's permission/source field is an author record, not proof of a license.

Before posting, use a unique course ID and validate the package with `Tools/ValidateCoursePackage.ps1` when the tools are available. For a Discord post, include the course name and ID, base map or source page, hole count, required Course Studio version, and whether imported bundles may be redistributed. The BONELAB source links retained for Discord sharing are in the [porting guide](PORTING_GUIDE.md#bonelab-map-source-references).

~~~text
Course:
Course ID:
Base map / source page:
Holes / par:
Required Course Studio version:
Imported bundles: included with permission / obtain from source page
Notes:
~~~

## Uninstall

1. Close Disc Golf Masters.
2. Copy `BepInEx/plugins/CustomCourseLoader/Courses` and `Maps` somewhere safe if you want to keep your courses. Also back up `BepInEx/config/CustomCourseLoader.personal-bests.json` if you want to keep custom-course personal bests.
3. Remove the `BepInEx/plugins/CustomCourseLoader` folder to uninstall the plugin and editor. Keep or remove the backed-up course data independently.
4. Remove BepInEx only if no other installed mod depends on it.

## Course editing notes

- A shot limit of `0` means no limit. A positive limit is the maximum throws before the round advances from that hole.
- Removing tee or basket art hides the visible object. The basket's scoring trigger remains available to the game.
- Original-course offsets use local right/up/forward meters relative to the built-in tee. Imported-map positions use absolute Unity world X/Y/Z meters.
- Imported map bounds calibrate the editor's 2D map preview to Unity world coordinates. They do not change the Unity scene.
- **Map settings → Advanced lighting** includes optional realtime shadows and a switch to hide imported particle effects. The nested **Terrain & vegetation** group offers a lit foliage cutout shader, lightmap preservation, terrain material repair, textured terrain-detail repair, and distant tree meshes. These are per-map rendering fixes; they can change appearance and performance, so check each result in game. Hiding particles removes those effects entirely.
- A map preview image is for editing. When you play a custom course, the loader attempts to capture a scenic selector image; until then, it shows the course card image. Per-hole cards are shown near tees. Captures are stored separately in `BepInEx/config/CustomCourseLoader/Previews` and do not overwrite your cards.
- Custom-course personal bests are stored separately in `BepInEx/config/CustomCourseLoader.personal-bests.json`, keyed by layout ID. A record is written only after every hole in the round has a score; values are relative to par and a lower value replaces a higher one. This does not change DGM's built-in course progression. Older custom-course results in the game's PlayerStats data are not imported automatically.
- Course tools currently target offline rounds.

## Create a custom map

Use **Add map package…** in the Installed maps view to create a course on a Unity scene bundle. Enter the scene's `.unity` asset path and its bundle path under `Maps`. Add required dependency bundles, provide a top-down screenshot, then set world bounds and place every tee and basket.

If a source game exports its hole data, the included `Tools/ConvertCourseLayoutJson.ps1` can convert it after mapping the fields to `Tools/Schemas/course-layout-interchange.schema.json`. For an imported map, reference the already-installed scene bundle and use `coordinateSpace: "unityWorld"`. Without source layout data, place the holes in Course Studio.

For bundle extraction, see the [porting guide](PORTING_GUIDE.md). Use the [troubleshooting guide](TROUBLESHOOTING.md) if the editor, course card, or imported scene does not appear.
