# Development

## macOS setup (VS Code or command line)

1. Install a .NET 10 SDK. The repository pins SDK `10.0.401` with feature-band roll-forward in `global.json`.
2. For backend integration, use a supported OpenModelica environment. Official native macOS builds were discontinued after OpenModelica 1.16, so current native verification should run on Windows/Linux or in a suitable VM until a remote/container adapter is implemented.
3. In that environment, confirm `omc --version` works, or set `OPENMODELICAHOME` to the OpenModelica installation root.
4. Run:

   ```bash
   ./scripts/dotnet.sh restore
   ./scripts/dotnet.sh build
   ./scripts/dotnet.sh test
   ./scripts/dotnet.sh run --project src/ModelicaStudio.Desktop
   ```

The wrapper prefers `.dotnet/dotnet` when a workspace-local SDK exists and otherwise uses the SDK from `PATH`. Visual Studio is not required.

## OpenModelica detection

Detection checks, in order:

- a configured executable path passed to `OpenModelicaOptions`;
- `OPENMODELICAHOME/bin/omc`;
- each directory on `PATH`;
- conventional macOS, Windows, and Linux installation paths.

Every candidate is verified by running `omc --version` without a shell and with a five-second timeout. A manually selected executable can be chosen from the offline engine card or Tools menu. It is persisted only after the interactive compiler session starts successfully; an invalid selection does not stop an already-running session.

## Tests

`ModelicaStudio.Domain.Tests`, `ModelicaStudio.Application.Tests`, `ModelicaStudio.OpenModelica.Tests`, and `ModelicaStudio.UI.Tests` run without OpenModelica. The integration test detects `omc` during test discovery; if unavailable, xUnit reports the test as skipped with a reason. On a configured machine it performs the required BouncingBall sequence: session start, version, MSL load, file load, check, typed graphical-annotation and full model-instance retrieval, simulation, variable discovery, and signal reading.

The verified baseline on 2026-09-11 is 180 passed, 1 skipped, and 0 failed. The skip is the native BouncingBall scenario on a host without `omc`.

Some constrained environments block the local IPC sockets used by MSBuild build servers and the .NET test host. The reliable single-node form is:

```bash
./scripts/dotnet.sh build ModelicaStudio.slnx --no-restore --disable-build-servers -p:UseSharedCompilation=false -m:1 -nr:false
./scripts/dotnet.sh test ModelicaStudio.slnx --no-build --no-restore --disable-build-servers -m:1 -nr:false
```

The repository wrapper sets `AVALONIA_TELEMETRY_OPTOUT=1`, following Avalonia's documented build-telemetry opt-out, and keeps .NET/NuGet state inside the workspace.

## Common problems

- **The .NET 10 SDK cannot be found:** install the SDK requested by `global.json`, or place a local SDK in `.dotnet` and use `scripts/dotnet.sh`.
- **OpenModelica is not detected:** verify `omc --version`, `OPENMODELICAHOME`, or the configured absolute executable path.
- **OMC starts but no endpoint appears:** inspect structured application logs. The adapter waits for OMC's port-file announcement and reports startup timeout separately from command timeout.
- **MSL does not load:** verify that the OpenModelica installation includes the Modelica library and that `getModelicaPath()` is correct in OMShell.

## Windows

Install .NET 10 x64 and OpenModelica, then use PowerShell equivalents:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/ModelicaStudio.Desktop
```

Initial publish commands (installer packaging is a later milestone):

```bash
dotnet publish src/ModelicaStudio.Desktop -c Release -r osx-arm64
dotnet publish src/ModelicaStudio.Desktop -c Release -r osx-x64
dotnet publish src/ModelicaStudio.Desktop -c Release -r win-x64
```

On macOS, `./scripts/package-macos.sh [runtime-id] [configuration]` builds a self-contained `.app` in `artifacts`. It currently performs no code signing or notarization.
