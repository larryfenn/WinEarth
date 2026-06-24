using System;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace WinEarth
{
    /// <summary>
    /// Runtime configuration for WinEarth, loaded from a JSON file next to the
    /// executable. New settings can be added as properties here and to config.json.
    /// </summary>
    public class Config
    {
        /// <summary>Directory where downloaded/cropped wallpaper images are written.</summary>
        public string StoragePath { get; set; } = @"C:\Users\larry\Downloads\Desktop\WinEarth";

        /// <summary>File that errors and status messages are appended to.</summary>
        public string LogPath { get; set; } = @"C:\Users\larry\corelog.txt";

        /// <summary>Default name of the config file, expected next to the executable.</summary>
        private const string FileName = "config.json";

        /// <summary>
        /// Loads configuration from config.json next to the executable. If the file is
        /// missing or cannot be parsed, defaults are used and a fresh file is written so
        /// the user has something to edit.
        /// </summary>
        public static Config Load()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                FileName);

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    Config loaded = new JavaScriptSerializer().Deserialize<Config>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
                catch
                {
                    // Fall through to defaults if the file is malformed.
                }
            }

            Config defaults = new Config();
            try
            {
                defaults.Save(path);
            }
            catch
            {
                // If we can't write the file, just run with in-memory defaults.
            }
            return defaults;
        }

        private void Save(string path)
        {
            string json = new JavaScriptSerializer().Serialize(this);
            File.WriteAllText(path, json);
        }
    }
}
