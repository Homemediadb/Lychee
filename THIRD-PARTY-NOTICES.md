# Third-Party Notices

Lychee as a whole is licensed under the **GNU General Public License v3.0** (see [LICENSE](LICENSE)). The
distributed binaries (the `dist/` folder, the portable zip and the installer) also include the third-party
components below, under their own licenses. The license texts are in [`licenses/`](licenses/), and ship
alongside the binaries.

Nothing here restricts your rights under the GPL for Lychee's own code; each third-party component is
governed solely by its own license.

Lychee uses no third-party libraries beyond the .NET and Windows App SDK runtimes; image decoding and
encoding is done by Windows itself (Windows Imaging Component), which is not redistributed.

---

## .NET runtime — MIT

The app is published self-contained and trimmed, so the parts of the .NET 10 runtime it uses are bundled:
`coreclr.dll`, `clrjit.dll`, `clrgc*.dll`, `hostfxr.dll`, `hostpolicy.dll`, `System.Private.CoreLib.dll`,
`System.*.dll`, `msquic.dll`, etc. (`Microsoft.NETCore.App` 10.0).

- **License:** MIT — [`licenses/dotnet-LICENSE.txt`](licenses/dotnet-LICENSE.txt)
- **Copyright:** © .NET Foundation and Contributors.
- **Third-party notices** of components within the runtime:
  [`licenses/dotnet-THIRD-PARTY-NOTICES.txt`](licenses/dotnet-THIRD-PARTY-NOTICES.txt)
- **Source:** <https://github.com/dotnet/runtime>

## C#/WinRT and the Windows SDK projection — MIT

`WinRT.Runtime.dll`, `Microsoft.Windows.SDK.NET.dll`

- **License:** MIT — same text as [`licenses/dotnet-LICENSE.txt`](licenses/dotnet-LICENSE.txt)
- **Copyright:** © Microsoft Corporation.
- **Source:** <https://github.com/microsoft/CsWinRT>

## Windows App SDK / WinUI 3 — Microsoft Software License Terms

`Microsoft.WinUI.dll`, `Microsoft.UI.*`, `Microsoft.ui.xaml.*`, `Microsoft.Windows.*`,
`Microsoft.WindowsAppRuntime.*`, `Microsoft.InteractiveExperiences.Projection.dll`,
`Microsoft.Internal.FrameworkUdk.dll`, `Microsoft.DirectManipulation.dll`, `Microsoft.Graphics.Display.dll`,
`Microsoft.InputStateManager.dll`, `CoreMessagingXP.dll`, `DWriteCore.dll`, `DwmSceneI.dll`, `dwmcorei.dll`,
`dcompi.dll`, `wuceffectsi.dll`, `marshal.dll`, `MRM.dll`, `WinUIEdit.dll`, `RestartAgent.exe`,
`PushNotificationsLongRunningTask.ProxyStub.dll`, `Microsoft.Web.WebView2.Core.dll`, `WebView2Loader.dll`, the
per-language resource folders, and the associated `.winmd` / `.pri` files.

- **License:** Microsoft Software License Terms for the Windows App SDK, which permit redistributing it with
  an application — [`licenses/WindowsAppSDK-LICENSE.txt`](licenses/WindowsAppSDK-LICENSE.txt)
- **Copyright:** © Microsoft Corporation.
- **Third-party notices:** [`licenses/WindowsAppSDK-NOTICE.txt`](licenses/WindowsAppSDK-NOTICE.txt),
  [`licenses/WinUI-NOTICE.txt`](licenses/WinUI-NOTICE.txt)
- **Source:** <https://github.com/microsoft/WindowsAppSDK>, <https://github.com/microsoft/microsoft-ui-xaml>

Only the parts of the Windows App SDK the app uses are referenced; its AI, ML, Search and Widgets runtimes are
left out of the build and not redistributed.

---

## Runtime requirements (not bundled)

Nothing needs installing besides Lychee: the .NET and Windows App SDK runtimes are baked in, and the WinUI 3
binaries need only the Universal CRT, part of Windows 10 1809 (build 17763) and newer.
