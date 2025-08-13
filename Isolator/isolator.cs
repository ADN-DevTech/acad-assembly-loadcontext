using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using Command.Interface;

namespace Isolator
{
    public class Isolator : ISystemConfigurationManager
    {
        private readonly Configuration _config;

        public Isolator()
        {
            string configPath = GetProjectAppConfigPath();
            if (File.Exists(configPath))
            {
                var configFileMap = new ExeConfigurationFileMap()
                {
                    ExeConfigFilename = configPath
                };
                _config = ConfigurationManager.OpenMappedExeConfiguration(configFileMap, ConfigurationUserLevel.None);
            }
            else
            {
                _config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            }
        }

        public string GetAppSetting(string key)
        {
            if (_config?.AppSettings?.Settings[key] != null)
                return _config.AppSettings.Settings[key].Value;

            return ConfigurationManager.AppSettings[key];
        }

        public string GetConnectionString(string name)
        {
            var conn = _config?.ConnectionStrings?.ConnectionStrings[name];
            if (conn != null)
                return conn.ConnectionString;

            var globalConn = ConfigurationManager.ConnectionStrings[name];
            return globalConn?.ConnectionString;
        }

        public object GetSection(string sectionName)
        {
            // Prefer local config section
            var section = _config?.GetSection(sectionName);
            return section ?? ConfigurationManager.GetSection(sectionName);
        }

        public void RefreshSection(string sectionName)
        {
            if (_config != null)
            {
                ConfigurationManager.RefreshSection(sectionName);
            }
        }

        private string GetProjectAppConfigPath()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            string contentsDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, ".."));
            return Path.Combine(contentsDirectory, "App.config");
        }
    }
}
