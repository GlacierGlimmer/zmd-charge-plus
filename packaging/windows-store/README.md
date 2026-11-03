# Microsoft Store packaging

`scripts/package-store.ps1` builds organized, self-contained directories for x64, x86 and ARM64. As in v0.1.1, Store installation registers only **Endfield Charge Plus** (`Application Id="App"`). It adds no administrator or uninstall application icons. Windows/Microsoft Store manages removal.

The native entry remains at `App/EndfieldChargePlus.exe`, matching the v0.1.1 application and startup executable path. It directly loads `Runtime/EndfieldChargePlus.dll`. Other files are grouped into Runtime, Documents, Licenses and system Assets. There is no single-file runtime extraction or additional launcher process.

Keep the package Name, Publisher, App ID and `EndfieldChargePlusStartup` TaskId stable for updates. Packaged settings use `ApplicationData.Current.LocalFolder/EndfieldChargePlus`; an existing same-package LocalCache/AC configuration takes priority over portable settings when migrating to LocalState.

Store compares the four-part MSIX package version. The current user policy maps software a.b.c to Store (a+1).b.c.0: 0.1.2 maps to 1.1.2.0. A software major-version increase also increases the Store major component (1.0.0 maps to 2.0.0.0). A future update must exceed the highest applicable published/uploaded version; never downgrade an installed `1.0.2.0` package to `0.1.2.0`. Local preview builds do not publish or update the installed app. Build/schema/fixture checks do not constitute installed Store upgrade acceptance. Any restricted capabilities still require Store approval.
