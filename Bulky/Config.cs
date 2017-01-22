using System.Configuration;

namespace Bulky
{
    public static class Config
    {
        static Config()
        {            
        }

        public static string UserName {
            get { return ConfigurationManager.AppSettings["UserName"]; }
        }

        public static string Password
        {
            get { return ConfigurationManager.AppSettings["Password"]; }
        }
    }
}
