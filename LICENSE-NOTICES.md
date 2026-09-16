# Third-party notices

This file tracks direct dependencies and external runtime boundaries. It is not legal advice. Versions are controlled by `Directory.Packages.props`.

| Dependency | Version | License | Source | Purpose |
|---|---:|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.2 | MIT | https://github.com/AvaloniaUI/Avalonia | Cross-platform desktop UI |
| Avalonia.AvaloniaEdit | 12.0.0 | MIT | https://github.com/AvaloniaUI/AvaloniaEdit | Source-editor control and infrastructure |
| LiveChartsCore.SkiaSharpView.Avalonia | 2.1.0-dev-798 | MIT | https://github.com/Live-Charts/LiveCharts2 | Interactive simulation-result plotting; Avalonia 12-compatible development build |
| NetMQ | 4.0.4.3 | LGPL-3.0 | https://github.com/zeromq/netmq | Managed ZeroMQ-compatible OMC transport |
| Microsoft.Extensions.DependencyInjection / Logging | 10.0.0 | MIT | https://github.com/dotnet/runtime | Composition and structured logging |
| xUnit.net / runner | 2.9.3 / 3.1.4 | Apache-2.0 | https://github.com/xunit/xunit | Automated tests |
| coverlet.collector | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet | Test coverage collection |

## OpenModelica

OpenModelica is an external runtime, is not included in this repository, and is not currently bundled with Modelica Studio. The application invokes `omc` as a separate process through its public scripting interface.

The graphical-annotation and model-instance JSON fixtures under `tests/ModelicaStudio.OpenModelica.Tests/Fixtures` are derived from expected compiler output in OpenModelica's upstream instance-API tests. Their fixture READMEs record the source test names. They are retained solely for compatibility testing and remain subject to the applicable upstream OpenModelica licensing terms.

OpenModelica source identifies the OSMC Public License 1.8 with subsidiary licensing modes including GNU AGPL v3. Any future installer that redistributes OpenModelica binaries must undergo human/legal review, select and document an applicable usage mode, and satisfy all source/notice/distribution obligations. Do not infer bundling permission from this notice.

Reference: https://github.com/OpenModelica/OpenModelica/blob/master/OSMC-License.txt

## NetMQ review note

NetMQ's repository currently identifies LGPL v3. Distribution and replacement/linking obligations must be reviewed before production release. If that boundary is unsuitable, evaluate a separately installed native ZeroMQ binding or the MOS process adapter before shipping.
