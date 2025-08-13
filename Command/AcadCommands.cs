using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Command.Interface;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

[assembly: CommandClass(typeof(Command.AcadCommands))]
[assembly: ExtensionApplication(typeof(Command.CommandsRegistrar))]

namespace Command
{
    /// <summary>
    /// Registers commands in the AutoCAD environment.
    /// </summary>
    public class CommandsRegistrar : IExtensionApplication
    {
        public void Initialize()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\nAcadCommands plugin loaded successfully!");
        }

        public void Terminate()
        {
            AcadCommands.UnloadIsolator();
        }
    }

    /// <summary>
    /// Contains AutoCAD commands for testing and managing the isolated loader.
    /// </summary>
    public class AcadCommands
    {
        static IsolatedContext _isolatedContext;

        [CommandMethod("ACADCMD", "TestExeConfig", CommandFlags.Modal)]
        public static void TestExeConfig()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor
                    ?.WriteMessage("\nNo active document found.");
                return;
            }

            var ed = doc.Editor;

            try
            {
                // Always unload the old context to allow hot reload
                UnloadIsolator();

                string isolatorPath = GetIsolatorPath();
                _isolatedContext = new IsolatedContext(isolatorPath);

                var isolatedConfigManager = _isolatedContext.CreateConfigurationManager();
                if (isolatedConfigManager == null)
                {
                    ed.WriteMessage("\nError: Configuration Manager instance is null.");
                    return;
                }

                // 1️ Get app setting
                string testKeyValue = isolatedConfigManager.GetAppSetting("TestKey");
                ed.WriteMessage($"\n[AppSetting] TestKey = {testKeyValue ?? "<null>"}");

                // 2️ Get connection string
                try
                {
                    string mainDbConn = isolatedConfigManager.GetConnectionString("MainDb");
                    ed.WriteMessage($"\n[ConnectionString] MainDb = {mainDbConn ?? "<null>"}");
                }
                catch (NotImplementedException)
                {
                    ed.WriteMessage("\n[ConnectionString] Method not implemented yet.");
                }

                // 3️ Get custom section
                try
                {
                    object customSection = isolatedConfigManager.GetSection("customSection");
                    if (customSection is System.Collections.Specialized.NameValueCollection nvc)
                    {
                        ed.WriteMessage("\n[CustomSection] customSection values:");
                        foreach (string key in nvc.AllKeys)
                        {
                            ed.WriteMessage($"\n   {key} = {nvc[key]}");
                        }
                    }
                    else
                    {
                        ed.WriteMessage("\n[CustomSection] Returned object is null or unexpected type.");
                    }
                }
                catch (NotImplementedException)
                {
                    ed.WriteMessage("\n[CustomSection] Method not implemented yet.");
                }

                // 4️ Get system.web section
                try
                {
                    object sysWeb = isolatedConfigManager.GetSection("system.web/compilation");
                    if (sysWeb != null)
                    {
                        ed.WriteMessage($"\n[System.Web] Compilation section type: {sysWeb.GetType().FullName}");
                    }
                }
                catch (NotImplementedException)
                {
                    ed.WriteMessage("\n[System.Web] Method not implemented yet.");
                }

                ed.WriteMessage($"\nLoaded from: {isolatorPath}");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[ACADCMD.TestExeConfig] Error: {ex.Message}");
                ed.WriteMessage($"\nStackTrace: {ex.StackTrace}");
            }
        }



        private static string GetIsolatorPath()
        {
            var currentAssemblyLocation = Assembly.GetExecutingAssembly().Location;
            var contentsDirectory = Path.GetDirectoryName(currentAssemblyLocation)
                ?? throw new DirectoryNotFoundException("Unable to find assembly directory");
            return Path.Combine(contentsDirectory, "Isolator", "Isolator.dll");
        }

        public static void UnloadIsolator()
        {
            if (_isolatedContext != null)
            {
                try
                {
                    _isolatedContext.Unload();
                    _isolatedContext = null;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                    // Swallow exceptions to avoid blocking other plugins during shutdown
                }
            }
        }
    }
}

/// <summary>
/// Collectible AssemblyLoadContext for isolated loading.
/// </summary>
public class IsolatedContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _assemblyPath;

    public IsolatedContext(string assemblyPath) : base(isCollectible: true)
    {
        _assemblyPath = assemblyPath;
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    public IsolatedConfigurationManager CreateConfigurationManager()
    {
        var assembly = this.LoadFromAssemblyPath(_assemblyPath);

        // Match full interface name for safety
        var type = assembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract &&
                t.GetInterfaces().Any(i => i.FullName == typeof(ISystemConfigurationManager).FullName));

        if (type == null)
        {
            // Optional: fallback to type name match
            type = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "Isolator" && !t.IsInterface && !t.IsAbstract);
        }

        if (type == null)
        {
            throw new InvalidOperationException(
                $"No type implementing {typeof(ISystemConfigurationManager).FullName} found in {_assemblyPath}");
        }

        var instance = Activator.CreateInstance(type);
        return new IsolatedConfigurationManager(instance);
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }
        return IntPtr.Zero;
    }
}

/// <summary>
/// Wrapper to call methods across isolated assembly contexts.
/// Prevents leaking types from the isolated context.
/// </summary>
public class IsolatedConfigurationManager : ISystemConfigurationManager, IDisposable
{
    private readonly object _instance;
    private readonly MethodInfo _getAppSettingMethod;
    private readonly MethodInfo _getConnectionStringMethod;
    private readonly MethodInfo _getSectionMethod;
    private readonly MethodInfo _refreshSectionMethod;

    public IsolatedConfigurationManager(object instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));

        var type = instance.GetType();

        _getAppSettingMethod = type.GetMethod(nameof(GetAppSetting))
            ?? throw new InvalidOperationException("GetAppSetting method not found on the loaded type");

        _getConnectionStringMethod = type.GetMethod(nameof(GetConnectionString));
        _getSectionMethod = type.GetMethod(nameof(GetSection));
        _refreshSectionMethod = type.GetMethod(nameof(RefreshSection));
    }

    // Implementation of ISystemConfigurationManager interface
    public string GetAppSetting(string key)
    {
        try
        {
            return (string)_getAppSettingMethod.Invoke(_instance, new object[] { key });
        }
        catch (TargetInvocationException ex)
        {
        #if DEBUG
                Application.DocumentManager.MdiActiveDocument?.Editor
                    ?.WriteMessage($"\n[IsolatedConfigManager] GetAppSetting error: {ex.InnerException?.Message}");
        #endif
            return null;
        }
    }

    public string GetConnectionString(string name)
    {
        if (_getConnectionStringMethod == null) return null;

        try
        {
            return (string)_getConnectionStringMethod.Invoke(_instance, new object[] { name });
        }
        catch (TargetInvocationException ex)
        {
            #if DEBUG
                        Application.DocumentManager.MdiActiveDocument?.Editor
                            ?.WriteMessage($"\n[IsolatedConfigManager] GetConnectionString error: {ex.InnerException?.Message}");
            #endif
            return null;
        }
    }

    public object GetSection(string sectionName)
    {
        if (_getSectionMethod == null) return null;

        try
        {
            return _getSectionMethod.Invoke(_instance, new object[] { sectionName });
        }
        catch (TargetInvocationException ex)
        {
            #if DEBUG
                        Application.DocumentManager.MdiActiveDocument?.Editor
                            ?.WriteMessage($"\n[IsolatedConfigManager] GetSection error: {ex.InnerException?.Message}");
            #endif
            return null;
        }
    }

    public void RefreshSection(string sectionName)
    {
        if (_refreshSectionMethod == null) return;

        try
        {
            _refreshSectionMethod.Invoke(_instance, new object[] { sectionName });
        }
        catch (TargetInvocationException ex)
        {
            #if DEBUG
                        Application.DocumentManager.MdiActiveDocument?.Editor
                            ?.WriteMessage($"\n[IsolatedConfigManager] RefreshSection error: {ex.InnerException?.Message}");
            #endif
        }
    }

    public void Dispose()
    {
        if (_instance is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                #if DEBUG
                                Application.DocumentManager.MdiActiveDocument?.Editor
                                    ?.WriteMessage($"\n[IsolatedConfigManager] Dispose error: {ex.Message}");
                #endif
            }
        }
    }
}
