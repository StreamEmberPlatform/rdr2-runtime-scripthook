# ScriptHookRDR2 SDK (subset)

`inc/main.h` and `lib/ScriptHookRDR2.lib` from the ScriptHookRDR2 SDK v1.0.1207.73 by Alexander Blade
(http://www.dev-c.com/rdr2/scripthookrdr2/). The runtime only needs these two files: the header declares the
ScriptHookRDR2 exports (script registration, native invocation, entity pools, keyboard hook) and the import library
links them. The SDK has not changed since its first release, so it is kept in the repository and builds need nothing
from outside.

Nothing from this folder is shipped. Players install `ScriptHookRDR2.dll` (and its `dinput8.dll` ASI loader)
themselves from dev-c.com; the SDK terms do not allow redistributing `ScriptHookRDR2.dll`.
