# UnityMeshSimplifier (editor-only subset)

Upstream: https://github.com/Whinarn/UnityMeshSimplifier

Pinned commit: `53fdb3122645bcd3ad2c258235200dff3dfbaa9b`.

MIT licensed; see LICENSE.md and the notices in each source file. Source files are
unmodified. Only MeshSimplifier and its dependencies are included, under Editor so
none of the simplifier code ships in Android players. WallKitImporter uses this to
bake the user's high-resolution Tripo models into 1,500-triangle mobile meshes.

Included dependencies: BlendShape, SimplificationOptions, Math, Internal,
ValidateSimplificationOptionsException, MeshUtils, and ResizableArray. LOD
components, mesh combiners, runtime helpers, tests, and upstream assembly definitions
are intentionally omitted.
