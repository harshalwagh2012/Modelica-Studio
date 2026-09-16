# Architecture

## Layering

```text
Avalonia Desktop / UI
        ↓
Application contracts and workflows
        ↓
Domain models and coordinate logic
        ↓
OpenModelica adapter / infrastructure
        ↓
external omc process
```

`ModelicaStudio.Domain` has no UI or compiler dependencies. `ModelicaStudio.Application` owns backend abstractions such as `IOpenModelicaService`. The `ModelicaStudio.OpenModelica` adapter is the only production project that creates OpenModelica scripting commands or interprets raw OMC responses. `ModelicaStudio.UI` consumes application contracts, and `ModelicaStudio.Desktop` is the dependency-injection composition root.

## OpenModelica integration

The primary adapter follows the current OMPython transport sequence:

1. locate and validate `omc --version`;
2. start `omc --locale=C --interactive=zmq -z=<unique-session-id>` without a shell;
3. read OMC's `Dumped server port in file: ...` startup message;
4. read the generated endpoint and communicate using ZeroMQ REQ/REP;
5. serialize commands, enforce per-operation timeouts, and send `quit()` during orderly shutdown.

The current OMPython source uses the same launch arguments and a ZeroMQ request socket. `OpenModelicaZmqTransport` owns process and protocol lifecycle. `OpenModelicaZmqService` owns typed operations and state transitions. UI ViewModels never receive raw command access.

Transport and typed service are separate interfaces. This allows a MOS/process fallback or a future non-OpenModelica backend without changing the UI. A MOS fallback is not implemented yet.

## Safety and error handling

Process arguments use `ProcessStartInfo.ArgumentList`; no shell command is constructed. Modelica type names are validated as qualified identifiers, and Modelica string arguments are escaped centrally. External paths are canonicalized and existence-checked before loading. OMC diagnostics are parsed into neutral `CompilerDiagnostic` records while the raw simulation response remains available for diagnosis.

Missing OpenModelica is an expected state. The service publishes `NotDetected` and the desktop displays an actionable offline state instead of failing startup. Unexpected OMC termination transitions the session to `Faulted`. Restart is part of the application contract.

## Concurrency

All public compiler operations are asynchronous. A service gate preserves OMC session ordering. The transport creates one short-lived NetMQ request socket per command while retaining the long-lived OMC process; this respects NetMQ socket thread affinity and makes cancellation/timeout cleanup deterministic. No compiler work is performed on Avalonia's UI thread.

## Simulation and result pipeline

Simulation setup remains a neutral `SimulationConfiguration` and is stored per model in the project document. The UI workflow saves and reloads the active source, performs an authoritative model check, blocks simulation on errors, calls the typed asynchronous `simulate` operation, retains raw output/diagnostics, discovers variables with `readSimulationResultVars`, and honors cancellation throughout the service boundary. OMC currently performs translation, compilation, and execution inside one request, so intermediate progress is intentionally reported as one real stage rather than fabricated percentages.

Simulation returns a neutral `SimulationResult`; selected signals use `readSimulationResult` and become neutral `SimulationSeries` values before entering the chart layer. LiveCharts2 was selected over OxyPlot because the maintained LiveCharts2 Avalonia source and samples target Avalonia 12/.NET 10, while the published OxyPlot Avalonia integration remains tied to older Avalonia generations. LiveCharts' stable 2.0.5 package compiles against Avalonia 12 but fails at runtime because it references the removed `Avalonia.Input.Gestures.PinchEvent`; the repository therefore pins and packaged-app-smoke-tests the Avalonia-12 development build `2.1.0-dev-798` until a compatible stable release is available. The chart receives only real, finite series points and supports multi-signal selection plus zoom/pan. Direct MAT reading, export, cursors, and run comparison are future slices.

## Graphics coordinates

`IModelicaCoordinateTransformer` is the single boundary between Modelica coordinates (positive Y upward) and canvas coordinates (positive Y downward). It supports independent axes or centered aspect-preserving scaling. `ModelicaGraphicTransform` applies each primitive's Modelica origin and counter-clockwise rotation. `ModelicaPlacementTransform` then maps a component type's Icon extent through the instance Placement extent, preserving mirrored axes before rotation and translation into the owning class coordinate system.

## Graphical annotation pipeline

The OpenModelica adapter sends typed `getModelInstanceAnnotation` and `getModelInstance` queries and owns all JSON interpretation. `ModelInstanceAnnotationParser` converts current instance-API records into neutral Domain types for Icon/Diagram coordinate systems, inheritance, dynamic/static values, graphical primitives, styles, and compiler-reported annotation issues. `ModelicaModelInstanceParser` additionally extracts inherited/local components, type-icon snapshots, prefixes, Placement transforms, connection references, and connection Line annotations. Raw JSON is retained at snapshot, component, connection, and primitive boundaries so unsupported fields are not silently destroyed.

`ModelicaGraphicsView` is an Avalonia renderer and interaction surface. It consumes only neutral Domain types, paints deepest inherited layers first, uses the static branch of `DynamicSelect`, composes component and nested connector type icons through placements, and draws annotated or endpoint-inferred connection routes without generating compiler commands. Pure Domain geometry applies the same transforms for component/connection/connector hit-testing, compatibility, orthogonal routing, intersecting box selection, rotated-corner resize, and handle rotation. The control owns only ephemeral marquee, group-move, resize, rotation, library-drop, connection, and route previews, then reports typed requests to the ViewModel. Avalonia's in-process typed data-transfer format carries compiler-derived `ModelicaClassInfo` values from instantiable library nodes. Bitmap payload decoding, complete fill-pattern fidelity, Bezier rendering, and collision-aware automatic routing remain outside the renderer.

Graphical mutation workflows keep compiler writes out of the renderer. During a drag, `ModelicaGraphicsView` owns only ephemeral preview transformations and snaps them through pure Domain helpers. On pointer release or arrow-key movement it emits a typed placement batch. The ViewModel records a `ReversibleModelCommand` only after the OpenModelica adapter accepts every `setElementAnnotation`, one fresh `getModelInstance` confirms the whole batch, one `save(className)` succeeds, and the persisted source reloads. A failed post-mutation step restores every already-applied prior Placement through OMC and saves one rollback; a failed command never enters history.

The same confirmation gate applies to component composition. Library classes carry compiler restriction and partial metadata; valid types are named from `getDefaultComponentName` or a deterministic lower-camel fallback. A Diagram drop emits only a type and snapped model-space origin. The ViewModel calls `addComponent(..., annotate=Placement(...))`, confirms name/type/placement in a fresh instance, saves, reloads, then exposes the real type graphics. Undo deletes that exact component. Group deletion calls `deleteComponent` for every local selection, confirms authoritative absence, and refuses attached connections until connection transactions can remove them safely. Undoing deletion replaces the compiler class from the saved pre-delete source through `loadString(..., merge=false)`, preserving declarations that cannot be reconstructed losslessly from instance JSON.

Duplication follows the upstream compiler-supported merge path rather than reconstructing components with `addComponent`. `ModelicaClassContentCopy` lexically locates exact declaration spans and internal `connect(...)` equations in the synchronized source while ignoring strings/comments and rejecting ambiguous shared declarations. The adapter sends that content through `loadClassContentString` with a grid-derived offset; a refreshed instance must prove the new name set, type multiset, offset placements, and internal-connection count before persistence. Undo and rollback restore the complete pre-duplicate source snapshot. Every restore and rollback is re-confirmed before persistence. Successful mutations replace the document's saved source baseline, stale prior diagnostics, and invalidate simulation results from the earlier model revision.

Connection authoring follows the same compiler-confirmation boundary. The instance parser retains nested/inherited connector components, fixed dimensions, type prefixes, root types, and full standard Line style. Pure Domain logic derives safe endpoint paths and positions, rejects duplicate/self/direction/structural mismatches, and creates or adjusts orthogonal routes. The adapter serializes only validated component references into `addConnection`, `deleteConnection`, and `updateConnectionAnnotation`. The ViewModel accepts a mutation only after a fresh instance proves its endpoints and Line geometry, then saves and reloads once. Creation undo uses endpoint deletion; connection deletion and reroute undo restore the exact pre-edit source so unsupported comments or annotations are not reconstructed lossily. Failed post-mutation confirmation triggers compiler rollback and never records history.

## Model document state

`ModelDocument` currently owns the source path, source text, class name, compiler synchronization status, selected view, dirty state, and latest compiler diagnostics. Source remains authoritative for persistence. Diagnostics retain OMC start/end ranges; an edit preserves the messages but marks their locations stale so outdated squiggles are not presented as current. `IModelicaDocumentService` creates, opens, and atomically saves documents; it preserves unrecognized or invalid source unchanged and marks it invalid rather than replacing it.

The ViewModel holds compiler-derived class-annotation and full model-instance snapshots for the active document, selection, graphical command history, and active simulation configuration. Source remains the persistence baseline while graphical mutations follow update/confirm/save/reload transactions. Multi-document state, parameter commands, and a longer-lived last-valid snapshot for invalid edited source are not implemented yet.

## Source editing and outline

The text surface uses AvaloniaEdit rather than a custom text widget. `ModelicaTextEditor` supplies the Modelica highlighting definition, current-line treatment, search panel, editor options, a small indentation strategy, and a background renderer for compiler source ranges. Problems remain neutral Domain diagnostics until the control maps active-file locations to AvaloniaEdit text segments. The View owns the editor-document synchronization bridge because AvaloniaEdit's `Text` member is not an Avalonia bindable property; the ViewModel continues to own the authoritative editable source string.

`IModelicaSourceAnalyzer` is explicitly a tolerant lexical outline service, not a Modelica compiler. It skips comments and strings, understands quoted identifiers and basic declaration structure, and returns source-positioned symbols/issues even for incomplete text. OpenModelica remains responsible for parsing, semantics, flattening, and authoritative diagnostics. This separation prevents highlighting or outline code from destructively normalizing source.

## Project persistence

`.modelicaproj` is a versioned JSON format containing project identity, model files, library paths, recent models, per-model simulation configurations, and UI-state fields. `ProjectService` owns format validation and project-directory setup. It rejects project-file entries that escape the project root. `AtomicFileWriter` writes a temporary file in the destination directory before replacement so an interrupted save does not truncate the prior document.

## Application settings

Application settings use a separate versioned JSON document in the platform application-data directory. The settings service preserves unreadable or future-version files and falls back to defaults instead of overwriting data it does not understand. A selected OpenModelica executable is validated before the current compiler session is stopped; only a successfully started selection is persisted. UI code uses application interfaces for both operations and does not depend on `ModelicaStudio.OpenModelica`.

## Library architecture

Library data comes from OMC. The desktop first requests the actual top-level `Modelica` classes, then each `LibraryNodeViewModel` requests its children only when expanded. Each result includes compiler-reported restriction and partial status, which determines whether the node can be dragged into a Diagram. A node caches a successful result for its lifetime, sorts children deterministically, and remains retryable after a compiler failure. The search field currently filters top-level classes without issuing speculative compiler calls. Documentation, deep search, icon previews in the tree, and a cache explicitly keyed to compiler-session generation are upcoming work.

## Cross-platform and licensing

The target is `net10.0` with Avalonia 12. Process discovery is OS-aware, while all application and domain contracts are platform-neutral. OpenModelica remains separately installed and separately licensed. The adapter was designed from public APIs and protocol behavior; no OMEdit implementation was copied.

The current packaging script produces an unsigned, self-contained macOS application bundle for a requested runtime identifier. Windows/Linux packaging, macOS signing/notarization, and installer formats remain release-engineering work.
