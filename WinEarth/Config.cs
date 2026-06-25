using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace WinEarth
{
    /// <summary>
    /// Per-desktop image source picked in the configuration GUI: which NOAA GOES
    /// page to scrape, which link on that page (the "item index" => resolution
    /// level), and the crop rectangle (in source-image pixels) to apply. Keyed by
    /// the shell's stable monitor device path so it survives index reordering.
    /// </summary>
    public class DesktopSource
    {
        /// <summary>Standard view: a fixed page URL plus a user-chosen crop.</summary>
        public const string ModeStandard = "Standard";

        /// <summary>GOES-East mesoscale view: scraped from NOAA's running meso list.</summary>
        public const string ModeMesoEast = "MesoEast";

        /// <summary>GOES-West mesoscale view: scraped from NOAA's running meso list.</summary>
        public const string ModeMesoWest = "MesoWest";

        /// <summary>Stable shell identifier for the target monitor.</summary>
        public string MonitorDevicePath { get; set; }

        /// <summary>
        /// View mode for this desktop: <see cref="ModeStandard"/>, <see cref="ModeMesoEast"/>,
        /// or <see cref="ModeMesoWest"/>. Mesoscale modes ignore <see cref="PageUrl"/> and
        /// instead resolve the top available mesoscale product from NOAA's running list,
        /// updating every minute. Null/empty is treated as <see cref="ModeStandard"/> so
        /// configs written before this field existed keep working.
        /// </summary>
        public string Mode { get; set; }

        /// <summary>GOES page to scrape for the image link (e.g. a conus/sector php page).</summary>
        public string PageUrl { get; set; }

        /// <summary>Index of the anchor in the page's "Links" list; higher = higher resolution.</summary>
        public int ItemIndex { get; set; }

        /// <summary>Crop rectangle in source-image pixel coordinates (16:9 aspect).</summary>
        public int CropX { get; set; }
        public int CropY { get; set; }
        public int CropWidth { get; set; }
        public int CropHeight { get; set; }

        /// <summary>
        /// When true, the crop is scaled to 4K (3840×2160) output instead of 1080p
        /// (1920×1080). Set per-monitor in the config GUI for high-DPI displays.
        /// </summary>
        public bool HighRes { get; set; }

        /// <summary>True once a crop has been chosen for this desktop.</summary>
        public bool HasCrop
        {
            get { return CropWidth > 0 && CropHeight > 0; }
        }

        /// <summary>True when this desktop tracks a mesoscale view (East or West).</summary>
        public bool IsMesoscale
        {
            get { return Mode == ModeMesoEast || Mode == ModeMesoWest; }
        }
    }

    /// <summary>
    /// Runtime configuration for WinEarth, loaded from a JSON file next to the
    /// executable. The only persisted setting is the per-desktop <see cref="Sources"/>
    /// list; all file locations are fixed relative to the executable so the install is
    /// self-contained.
    /// </summary>
    public class Config
    {
        /// <summary>Per-desktop image source and crop settings chosen in the GUI.</summary>
        public List<DesktopSource> Sources { get; set; } = new List<DesktopSource>();

        /// <summary>Default name of the config file, expected next to the executable.</summary>
        private const string FileName = "config.json";

        /// <summary>Directory the executable runs from; all WinEarth files live here.</summary>
        private static string AppDirectory
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        /// <summary>Full path of the config file, fixed alongside the executable.</summary>
        private static string ConfigFilePath
        {
            get { return Path.Combine(AppDirectory, FileName); }
        }

        /// <summary>
        /// Directory where downloaded/cropped wallpaper images are written. Fixed to a
        /// subfolder next to the executable; not user-configurable.
        /// </summary>
        public static string StoragePath
        {
            get { return Path.Combine(AppDirectory, "wallpapers"); }
        }

        /// <summary>
        /// File that errors and status messages are appended to. Fixed alongside the
        /// executable; not user-configurable.
        /// </summary>
        public static string LogPath
        {
            get { return Path.Combine(AppDirectory, "corelog.txt"); }
        }

        /// <summary>True if a config.json already exists next to the executable.</summary>
        public static bool Exists()
        {
            return File.Exists(ConfigFilePath);
        }

        /// <summary>
        /// Loads configuration from config.json next to the executable. If the file is
        /// missing or cannot be parsed, defaults are used and a fresh file is written so
        /// the user has something to edit.
        /// </summary>
        public static Config Load()
        {
            EnsureDirectories();

            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    Config loaded = new JavaScriptSerializer().Deserialize<Config>(json);
                    if (loaded != null)
                    {
                        // A file containing {"Sources":null} parses fine but leaves the
                        // list null, overriding the field initializer; downstream code
                        // iterates Sources unconditionally, so normalize it here.
                        if (loaded.Sources == null)
                        {
                            loaded.Sources = new List<DesktopSource>();
                        }
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
                defaults.Save();
            }
            catch
            {
                // If we can't write the file, just run with in-memory defaults.
            }
            return defaults;
        }

        /// <summary>
        /// Re-reads config.json from disk, returning the parsed config or <c>null</c> if
        /// the file is missing or cannot be read/parsed. Unlike <see cref="Load"/> this
        /// neither writes a defaults file nor substitutes an empty config, so the running
        /// updater can keep using its last-known-good config when a reload fails (for
        /// example a partial write racing the read).
        /// </summary>
        public static Config Reload()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return null;
                }

                string json = File.ReadAllText(ConfigFilePath);
                Config loaded = new JavaScriptSerializer().Deserialize<Config>(json);
                if (loaded == null)
                {
                    return null;
                }
                if (loaded.Sources == null)
                {
                    loaded.Sources = new List<DesktopSource>();
                }
                return loaded;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates the <see cref="StoragePath"/> subfolder if it doesn't already exist, so
        /// the first download doesn't fail. <see cref="LogPath"/> sits directly in the
        /// (already-existing) executable directory. Failures are swallowed; the subsequent
        /// write will surface them.
        /// </summary>
        private static void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(StoragePath);
            }
            catch
            {
                // Ignore; the actual write will report a meaningful error.
            }
        }

        /// <summary>Returns the configured source for a monitor, or null if none is set.</summary>
        public DesktopSource FindSource(string monitorDevicePath)
        {
            return Sources.FirstOrDefault(
                s => string.Equals(s.MonitorDevicePath, monitorDevicePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Inserts or replaces the source for a monitor (matched by device path) and
        /// persists the config to disk.
        /// </summary>
        public void SaveSource(DesktopSource source)
        {
            Sources.RemoveAll(
                s => string.Equals(s.MonitorDevicePath, source.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            Sources.Add(source);
            Save();
        }

        /// <summary>Persists the config to config.json next to the executable.</summary>
        public void Save()
        {
            string json = new JavaScriptSerializer().Serialize(this);
            File.WriteAllText(ConfigFilePath, json);
        }
    }
}
