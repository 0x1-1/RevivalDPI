using System;
using System.Reflection;

namespace RevivalDPI
{
    public static class VersionHelper
    {
        public static string GetAssemblyVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        public static string GetFileVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fileVersionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                return fileVersionAttribute?.Version ?? "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        public static string GetProductVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return informationalVersionAttribute?.InformationalVersion ?? GetAssemblyVersion();
            }
            catch
            {
                return GetAssemblyVersion();
            }
        }
    }
}
