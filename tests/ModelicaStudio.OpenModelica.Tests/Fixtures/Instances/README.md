# OpenModelica model-instance fixtures

`component-placement.json` is a compact compatibility fixture assembled from the JSON shapes emitted by these OpenModelica upstream instance-API tests:

- `testsuite/openmodelica/instance-API/GetModelInstanceDerived4.mos` for nested component annotations and `Placement.transformation`;
- `testsuite/openmodelica/instance-API/GetModelInstanceInnerOuter7.mos` and `GetModelInstanceAnnotation6.mos` for component type Icon annotations; and
- `testsuite/openmodelica/instance-API/GetModelInstanceConnection1.mos` for connection references and `Line` annotations.

The schema and tests were retrieved from `OpenModelica/OpenModelica` at the `master` branch on 2026-09-11. Names and values in this compact fixture are local, while its JSON object shapes follow the current upstream expected results. OpenModelica licensing is described in the repository root `LICENSE-NOTICES.md`.
