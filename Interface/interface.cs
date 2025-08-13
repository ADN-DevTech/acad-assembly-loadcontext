namespace Command.Interface
{
    public interface ISystemConfigurationManager
    {
        string GetAppSetting(string key);
        string GetConnectionString(string name);
        object GetSection(string sectionName);
        void RefreshSection(string sectionName);
    }
}

