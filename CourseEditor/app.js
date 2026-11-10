"use strict";

renderLibrary();
bindEvents();
restoreState();
if (isImportedMap() && !state.installedMapsLoaded) loadInstalledMaps();
