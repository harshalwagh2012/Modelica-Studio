# Status

Last verified: 2026-09-11

Modelica Studio is an early, runnable engineering application—not yet a complete OMEdit replacement. The backend, source-document, graphics, compiler-confirmed component/connection authoring transactions, and initial simulation/results foundations are real; parameter editing and several production workflows remain future phases.

## Completed and verified

- Phase 0 solution layering for Domain, Application, OpenModelica, Infrastructure, UI, Desktop, and five test projects.
- .NET 10/Avalonia 12 baseline, dependency-injection composition root, nullable analysis, warnings-as-errors, and structured Microsoft logging.
- OpenModelica discovery and executable validation on macOS, Windows, Linux, `PATH`, and `OPENMODELICAHOME`.
- Long-lived OMC process lifecycle using the interactive ZeroMQ protocol, with serialized requests, cancellation, timeouts, restart, shutdown, and unexpected-exit reporting.
- Typed backend calls for version, MSL/library load, file load, model check, first-level class browsing, simulation, result-variable discovery, and signal-series reading.
- Central OMC identifier validation, Modelica string escaping, response parsing, and neutral structured diagnostics/results.
- BouncingBall native integration scenario and RC example source.
- Versioned `.modelicaproj` documents with atomic JSON writes, directory scaffolding, safe relative paths, recent-model metadata, simulation configurations, and UI-state fields.
- New Modelica class generation for model, class, block, connector, record, package, function, and type definitions.
- Modelica source create/open/edit/save flow that preserves invalid source text instead of discarding it.
- Desktop shell with welcome/offline state, menu/toolbar, resizable engineering panels, source workspace, inspector, messages, and live engine/MSL first-level loading.
- Unsaved-change protection on close and document-replacing actions, including Save, Discard, and Cancel paths.
- Versioned cross-platform application settings with corrupt/future-version preservation, plus a validated `omc` executable picker that persists successful configuration and reconnects immediately.
- Lazy compiler-backed Modelica library tree with per-node loading, deterministic child ordering, retry-safe failures, and a live top-level class filter.
- Typed graphical-annotation domain models plus a safe `getModelInstanceAnnotation` backend operation, parser, and fixtures derived from current upstream OpenModelica instance-API output.
- Read-only Icon/Diagram tabs and canvas rendering for rectangles, ellipses/arcs, lines, polygons, text, bitmap placeholders, inherited layers, primitive origin/rotation, visibility, colors, fills, line patterns, and static `DynamicSelect` branches.
- Typed full `getModelInstance` retrieval for inherited/local component instances, prefixes, type icons, Placement transformations, and connection endpoints/Line annotations using the current OpenModelica instance-API schema.
- Read-only placed component and connector composition in Diagram/Icon views, including independent scaling, axis mirroring, origin translation, rotation, inherited type-icon layers, `%name` substitution, and annotated connection routes.
- Placement-aware component and route-aware connection hit-testing, selection overlays, and a typed Inspector for component prefixes/type/placement and connection endpoints/line properties.
- Typed `setElementAnnotation(..., $Code((Placement(...))))` and `save(className)` operations matching current OpenModelica/OMEdit usage, with invariant serialization and identifier validation.
- Transactional single/group placement editing: marquee selection, Shift-toggle selection, group drag preview, snap-preserving relative offsets, arrow/Shift+arrow movement, four local-coordinate resize handles, a 15°-snapped rotation handle, 90° rotation actions, authoritative whole-batch instance confirmation, one class persistence step, source reload, simulation-result invalidation, compiler rollback on partial failure, and command-based undo/redo.
- Typed `getDefaultComponentName`, `addComponent(..., annotate=Placement(...))`, `deleteComponent`, `isPartial`, and replace-mode `loadString` operations matching the current OpenModelica scripting interfaces.
- Library-to-Diagram component insertion through an Avalonia 12 drag payload and snapped drop preview. Instantiable compiler classes are filtered by restriction/partial status, names honor `defaultComponentName` before deterministic uniquification, and the real type icon appears only after a refreshed compiler instance confirms the addition.
- Transactional single/group component deletion with authoritative absence confirmation, one persistence step, rollback, and undo/redo. Existing deletions retain a complete source snapshot for lossless restoration of modifiers, prefixes, comments, and annotations; attached connections must be deleted first rather than leaving dangling equations.
- Exact-source single/group duplication built on OpenModelica's `loadClassContentString`. The copy payload retains each declaration's prefixes, modifiers/bindings, dimensions, comments, and annotations plus exact `connect(...)` equations whose endpoints are both selected; OpenModelica resolves new names and offsets graphical annotations. The refreshed model must confirm copy count, type multiset, placement offsets, and internal-connection count before one save, with source-snapshot undo/rollback.
- Typed nested/inherited connector discovery from full model-instance type trees, including connector Icon/Placement composition, component/type direction prefixes, root scalar types, fixed integer dimensions, and bounded expansion to safe indexed component references.
- Pure connector compatibility checks modeled after current OMEdit behavior: duplicate/self-connection rejection, inside/outside input/output rules, expandable-connector handling, and recursive structural checks for named connector fields, flow/stream prefixes, dimensions, and root types.
- Typed `addConnection`, `deleteConnection`, and `updateConnectionAnnotation` adapter operations using validated conventional/quoted connector references and complete standard Line serialization for visibility, origin, rotation, points, RGB color, pattern, thickness, arrows, arrow size, and smoothing metadata.
- Transactional connection creation, deletion, and rerouting. Every mutation is re-read through `getModelInstance`, checked for the requested endpoints/Line state, persisted once, reloaded into the source baseline, and excluded from history if confirmation fails. Destructive deletion and route undo restore the exact synchronized source; creation rollback removes the inserted equation.
- Diagram connection UX with compiler-derived connector symbols and hover labels, crosshair connection mode, click-click and press-drag gestures, live orthogonal route previews, compatible/incompatible feedback, connection selection/deletion, Delete-key support, circular segment-routing handles, endpoint-preserving grid snapping, arrows, and inferred display/hit-testing for existing `connect()` equations without Line annotations.
- AvaloniaEdit-based Modelica source surface with line numbers, current-line highlighting, a Modelica keyword/type/comment/string/number grammar, two-space indentation, editor undo/redo, cut/copy/paste, and search-panel access.
- Lightweight, non-semantic source analysis that indexes `within` scope, nested/short classes, inheritance, parameters, inputs/outputs, constants, and equation/algorithm sections while tolerating invalid snapshots.
- Saved-text dirty baselines, so undoing back to the saved source clears the modified marker without claiming that the compiler representation is synchronized.
- Document-owned compiler diagnostics with start/end source ranges, a Problems list, active-file navigation, severity-colored editor squiggles, and explicit stale-location state after edits.
- Validated Simulation Setup dialog for experiment times, intervals, tolerance, method, output format, and variable filter; per-model settings persist in `.modelicaproj` files.
- Asynchronous simulation orchestration that saves/synchronizes, checks, stops on diagnostics, invokes the real OpenModelica simulation service, supports cancellation requests, captures output, discovers result variables, and opens a Results workspace.
- LiveCharts2 multi-signal plotting backed only by typed `readSimulationResult` series, with non-finite point filtering and interactive X-axis zoom/pan.
- Reproducible self-contained macOS `.app` bundle script for a selected runtime identifier.

## Verification evidence

- Clean full solution build: **0 warnings, 0 errors**.
- Automated tests: **180 passed, 1 skipped, 0 failed** across Domain, Application, OpenModelica, UI, and native-integration test projects.
- The skipped test is explicitly reported by xUnit because `omc` is not installed; it is not counted as a pass.
- Packaged macOS UI smoke tests: launch while OpenModelica is absent; verify the Avalonia 12-compatible LiveCharts binary loads without the startup failure present in the stable 2.0.5 package; inspect the offline engineering workspace; confirm Select / Move, Delete, Duplicate, left/right rotation, Connect, and unified source/graphical undo and redo are rendered, expose the expected accessible labels/tooltips/menu entries, and remain correctly disabled without a synchronized model/compiler; and close cleanly. Earlier smoke coverage opened `BouncingBall.mo`, verified Modelica highlighting, line numbers, current-line emphasis, the four-item live source outline, dirty/undo behavior, and search-panel access, plus the `omc` picker and Save/Discard/Cancel unsaved-change paths.

## Partially working

- Phase 1 backend has unit coverage, but native OMC verification cannot run on this machine until OpenModelica is installed.
- The library tree and top-level filter are implemented, but expansion against a real MSL cannot be exercised on this machine without `omc`; deep search, documentation, and icon previews are pending.
- The source workspace now has core language-editing ergonomics and current compiler markers, but replace UI, bracket-match adorners, configurable whitespace/tab settings, folding, completion, and multi-document editing are pending.
- The graphical canvas renders core primitives, placed component/nested-connector type icons, annotated and endpoint-inferred connection routes, and Line arrows. It supports connector hover, click/drag connection creation, connection deletion/rerouting, component/connection inspection, marquee and Shift-toggle multi-selection, compiler-confirmed move/resize/rotate/duplicate, library-to-Diagram insertion, and deletion of unconnected local component groups. Bitmap decoding, hatch/gradient fill fidelity, all text substitutions/rotations, Bezier smoothing display, symbolic-size connector-array element choice, Icon-layer connector insertion, and richer collision-aware routing are pending.
- Project persistence exists, but recent-project UI, autosave/recovery, layout persistence, and multi-document tabs are pending.
- Simulation setup, orchestration, cancellation requests, result-variable discovery, and multi-signal plotting are implemented and unit-tested through the typed backend, but cannot be exercised end-to-end on this host without `omc`. Translation/compilation/runtime progress is currently coarse because OMC exposes this workflow through the single synchronous `simulate` request.
- The result workspace supports signal selection and interactive charts, but cursor readouts, axis/unit grouping, image/CSV export, retained multi-run comparison, and resimulation controls are pending.
- macOS app bundling exists; signing, notarization, installers, Windows/Linux packages, and CI release jobs do not.

## Not started

- Full Modelica parsing/indexing and source/diagram round-trip synchronization.
- Installed-library management, deep library search, documentation view, and external library workflow.
- Parameter editing and broader application-level undo/redo operations.
- Result cursor tools, CSV/image export, retained run comparison, and direct MAT-file reading.
- FMI workflows, debugging/profiling, collaboration, extension ecosystem, and other advanced roadmap items.
- Autosave/recovery, accessibility audit, telemetry policy, production security review, signing, and release hardening.

## Known issues and constraints

- `omc` is not installed on the current macOS development machine. Official native macOS OpenModelica builds were discontinued after version 1.16; model checking and simulation correctly remain offline here, and the BouncingBall native integration test is skipped with an explicit reason.
- A MOS/process fallback is supported by the transport boundary but is not implemented.
- The dedicated Preferences window is not implemented; compiler configuration is currently exposed through the offline engine card and Tools menu.
- Panel sizes and other layout preferences are not persisted between sessions.
- The graphical mutation paths are unit-tested against the typed compiler boundary but cannot yet be exercised against native OMC on this host. Connection references support conventional identifiers, quoted identifiers, and positive fixed integer subscripts; inherited placements/connections must be edited in their declaring class.
- Component deletion deliberately requires attached connections to be deleted first. Transactional connection deletion is available as a separate undoable operation; automatic cascade confirmation is not yet implemented. Undo for component deletion retains one complete pre-delete source snapshot so arbitrary existing component modifiers and prefixes are restored without lossy reconstruction.
- Fixed positive connector-array dimensions are expanded to indexed endpoints. Symbolic, colon, zero-sized, and very large dimensions are not expanded into per-element canvas targets; a future chooser should resolve those through compiler dimension data without guessing.
- Exact duplication refuses a selected component declared in a shared comma-separated declaration; splitting it into one declaration per component provides an unambiguous lossless source span. This is a deliberate guard against accidentally duplicating unselected siblings.
- LiveCharts' stable 2.0.5 Avalonia package is binary-incompatible with Avalonia 12 (`PinchEvent` is absent). The application therefore pins the upstream Avalonia-12 development build `2.1.0-dev-798`; it builds, passes tests, and has been verified in the packaged app, but remains a prerelease dependency that should be revisited when a compatible stable release is available.

## Next phase

1. Run the native BouncingBall integration test against a supported OpenModelica installation and resolve version-specific protocol differences, if any.
2. Add parameter/modifier inspection and editing through typed compiler operations.
3. Add safe cascading component deletion using the completed connection transaction boundary.
4. Add Icon-layer connector insertion with dual Placement transformations.
5. Add symbolic connector-array selection, collision-aware routing, Bezier display, and remaining diagram-rendering fidelity.
6. Add a full diagnostics/preferences view around the now-persisted compiler configuration.
