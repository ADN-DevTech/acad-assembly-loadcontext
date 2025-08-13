## Resolving Assembly Version Conflicts in AutoCAD with AssemblyLoadContext

### Problem

When developing AutoCAD .NET plugins, you may encounter **assembly version conflicts**.  
For example:

- AutoCAD ships with **System.Configuration.ConfigurationManager** version **8.x**.
- A third-party plugin or client code wants to use **System.Configuration.ConfigurationManager** version **9.x** for new API features or bug fixes.
- The .NET runtime **always prefers the lowest available version** in the load context for backward compatibility.
- This means your plugin will end up using AutoCAD’s **older 8.x version**, even if you package the newer 9.x version — causing missing API members or unexpected behavior.

This is a classic **version conflict** problem.

---

### Solution — `AssemblyLoadContext`

Starting with the **.NET Core** platform, Microsoft introduced [`System.Runtime.Loader.AssemblyLoadContext`](https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext).  
It allows you to:

- Load assemblies into **isolated contexts** separate from the default AppDomain.
- Load a **different version** of the same assembly without conflicting with AutoCAD’s copy.
- Unload and reload contexts at runtime (useful for **hot-reload** scenarios).

In this sample, we use `AssemblyLoadContext` to load **ConfigurationManager 9.x** in isolation so our plugin can use it without interfering with AutoCAD’s own dependencies.

---

### How It Works

1. **IsolatedContext** is created using `AssemblyLoadContext`.
2. The context explicitly loads:
   - Your target assembly (ConfigurationManager 9.x).
   - Your helper DLL (`IsolatedConfigurationManager`) that proxies configuration access.
3. All calls to `ConfigurationManager` go through this isolated helper, ensuring they **never resolve to AutoCAD’s 8.x version**.
4. You can unload and reload the context during a session for testing.

---

### Project Structure

**Roles:**

- **Command** — Implements the AutoCAD command (`TESTEXECONFIG`) that triggers the process.
- **Interface** — Defines common types shared between the AutoCAD host and the isolated loader.
- **Isolator** — Runs in a separate `AssemblyLoadContext` to load the desired version of `ConfigurationManager`.

---

#### Example Config (`app.config`)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>

    <!-- Application-wide settings -->
    <appSettings>
        <add key="TestKey" value="Hello World" />
        <add key="AnotherKey" value="42" />
        <add key="Environment" value="Development" />
        <add key="FeatureFlag_UseNewUI" value="true" />
    </appSettings>

    <!-- Example connection strings -->
    <connectionStrings>
        <add name="MainDb" connectionString="Server=localhost;Database=MainDB;User Id=testuser;Password=pass123;" providerName="System.Data.SqlClient" />
        <add name="AnalyticsDb" connectionString="Server=analytics.local;Database=Analytics;Integrated Security=True;" providerName="System.Data.SqlClient" />
    </connectionStrings>

    <!-- Example system.web section -->
    <system.web>
        <compilation debug="true" targetFramework="net8.0" />
        <customErrors mode="Off" />
    </system.web>

</configuration>
```

#### Building the Plugin

1. Build the solution in **Release** mode.

2. The output should be arranged into an **AutoCAD App Bundle**:

```.txt
Command.bundle
|
PackageContents.xml
│
└───Contents
    │   App.config
    │   Command.deps.json
    │   Command.dll
    │   Command.dll.config
    │   Command.Interface.dll
    │
    └───Isolator
            Command.Interface.dll
            Isolator.deps.json
            Isolator.dll
            Isolator.runtimeconfig.json
            System.Configuration.ConfigurationManager.dll
            System.Diagnostics.EventLog.dll
            System.Diagnostics.EventLog.Messages.dll
            System.Security.Cryptography.ProtectedData.dll
```

#### Deploying

1. Copy the `Command.bundle` folder to:
   
   `%APPDATA%\Autodesk\ApplicationPlugins\`

2. Start **AutoCAD 2025** or **2026**.

AutoCAD will detect and load the plugin automatically.

#### Running the Demo

In the AutoCAD command line:

`Command: TESTEXECONFIG`

Expected output:

```txt
[AppSetting] TestKey = Hello World
[ConnectionString] MainDb = Server=localhost;Database=MainDB;User Id=testuser;Password=pass123;
[CustomSection] Returned object is null or unexpected type.
Loaded from: D:\Work\Cases\24640596\Command\Bundle\Command.bundle\Contents\Isolator\Isolator.dll
```

#### Requirements

- AutoCAD 2025 or 2026 (Windows)

- .NET 8 SDK

- Visual Studio 2022

#### License

MIT

##### Written by

Madhukar Moogala (APS , Autodesk)
