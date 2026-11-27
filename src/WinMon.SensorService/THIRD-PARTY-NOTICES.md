# CPU sensor service third-party notices

WinMon's service code is MIT licensed. This optional service uses the unmodified **LibreHardwareMonitorLib 0.9.6** NuGet package and its resolved dependencies, recorded in `packages.lock.json`.

LibreHardwareMonitor and contributors hold copyright in their library. Its MPL-2.0 license is included in `Licenses/LibreHardwareMonitor.MPL-2.0.txt`; the corresponding source is available at [the v0.9.6 tag](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6). WinMon does not modify the library.

The library embeds signed PawnIO modules. The upstream resource notice identifies [PawnIO.Modules 0.1.6](https://github.com/namazso/PawnIO.Modules/releases/tag/0.1.6) as their release. Its notice and LGPL-2.1 license are included in `Licenses/PawnIO.Modules.README.txt` and `Licenses/PawnIO.Modules.LGPL-2.1.txt`. Corresponding module source and release artifacts are available at that upstream release. These module blobs are supplied by the pinned library, not built or modified by WinMon.

The signed WinMon setup helper embeds the unmodified official [PawnIO installer](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0). Its separate license and installer terms continue to apply. The driver package is also available from [pawnio.eu](https://pawnio.eu/).

Other NuGet dependencies and the .NET runtime retain their respective licenses. See the [pinned package metadata](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6) and resolved dependency graph for their identities. Release acceptance must verify the complete signed distribution and its notices.
