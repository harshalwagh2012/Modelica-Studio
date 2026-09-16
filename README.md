# Modelica Studio

Modelica Studio is a cross-platform Modelica modeling and simulation desktop application under active development. It uses OpenModelica as an external compiler and simulation engine while keeping the application, domain model, and UI independently designed.

The current vertical slice includes:

- .NET 10 and Avalonia 12 desktop solution structure;
- OpenModelica discovery on macOS, Windows, Linux, `PATH`, and `OPENMODELICAHOME`;
- a long-lived ZeroMQ OMC session with lifecycle, timeouts, cancellation, and unexpected-exit reporting;
- typed service operations for version, library/file/source loading, class browsing and metadata, component/connection authoring, model checking, simulation, and result reading;
- structured parsing of OMC diagnostics and result data;
- a real BouncingBall integration test that runs when `omc` is available;
- versioned project persistence with atomic writes and safe project-relative paths;
- Modelica class creation plus source open/edit/save and invalid-source preservation;
- a professional desktop shell that remains usable when OpenModelica is missing;
- unsaved-change protection for close and document-replacing actions;
- persistent cross-platform settings and a validated `omc` executable picker with live reconnect;
- lazy compiler-backed library expansion with retry-safe node loading and top-level filtering;
- typed `getModelInstanceAnnotation` and full `getModelInstance` queries and parsing based on current OpenModelica instance-API output;
- Icon/Diagram rendering for core Modelica graphical primitives, including inherited layers and static `DynamicSelect` fallbacks;
- diagram composition for placed component and nested connector icons plus annotated or inferred connection lines, including nested scaling, mirroring, translation, and rotation;
- placement-aware component and route-aware connection selection with visual highlights and a typed property inspector;
- compiler-confirmed single/group placement editing with marquee and Shift-toggle selection, drag/arrow movement, corner resize, 15° handle rotation, 90° actions, Modelica grid snapping, persisted Placement annotations, rollback, and graphical undo/redo;
- compiler-confirmed library-to-diagram component insertion with OpenModelica-recommended unique naming, snapped Placement annotations, real type icons, guarded deletion, rollback, and add/delete undo/redo;
- exact-source component/group duplication through OpenModelica's `loadClassContentString`, preserving modifiers, prefixes, comments, annotations, and connections internal to the selection while applying a compiler-confirmed placement offset;
- compiler-derived connector discovery and structural/direction compatibility, with fixed connector-array expansion, hover markers, click-click or drag connection gestures, and real `connect()` equation creation through OpenModelica;
- compiler-confirmed connection deletion and orthogonal route editing with complete Line style serialization, source-exact destructive undo, rollback, and graphical undo/redo;
- an AvaloniaEdit-based Modelica source editor with line numbers, syntax highlighting, current-line emphasis, indentation, search, and undo/redo;
- a non-destructive lightweight source outline for classes, inheritance, declarations, and equation/algorithm sections;
- source-linked compiler Problems with current-location squiggles, stale-result tracking, and click navigation;
- validated, project-persisted simulation setup plus asynchronous save/check/simulate/cancel/result-discovery workflow; and
- a real result workspace that reads selected signals through OpenModelica and plots up to twelve signals with LiveCharts2 zoom/pan support.

## Build

Prerequisites are .NET SDK 10.0.401 or a compatible .NET 10 SDK. OpenModelica is optional for unit builds but required for native integration and simulation.

```bash
./scripts/dotnet.sh restore
./scripts/dotnet.sh build
./scripts/dotnet.sh test
./scripts/dotnet.sh run --project src/ModelicaStudio.Desktop
```

Create a self-contained macOS app bundle (defaults to Apple Silicon and Release):

```bash
./scripts/package-macos.sh
# or: ./scripts/package-macos.sh osx-x64 Release
```

The bundle is written to `artifacts/Modelica Studio.app`. Signing and notarization are not implemented yet.

If a project-local SDK is absent, the wrapper uses `dotnet` from `PATH`. See [DEVELOPMENT.md](DEVELOPMENT.md) for setup and troubleshooting, [ARCHITECTURE.md](ARCHITECTURE.md) for design decisions, and [STATUS.md](STATUS.md) for verified scope.

## OpenModelica boundary

[OpenModelica](https://github.com/OpenModelica/OpenModelica) is launched as a separate process and is not bundled. Modelica Studio communicates through OpenModelica's public interactive scripting interface. No OMEdit source has been copied. Any future decision to bundle OpenModelica requires license and distribution review; see [LICENSE-NOTICES.md](LICENSE-NOTICES.md).
