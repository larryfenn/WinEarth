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
    /// executable. New settings can be added as properties here and to config.json.
    /// </summary>
    public class Config
    {
        /// <summary>Directory where downloaded/cropped wallpaper images are written.</summary>
        public string StoragePath { get; set; } = @"C:\Users\larry\Downloads\Desktop\WinEarth";

        /// <summary>File that errors and status messages are appended to.</summary>
        public string LogPath { get; set; } = @"C:\Users\larry\corelog.txt";

        /// <summary>Per-desktop image source and crop settings chosen in the GUI.</summary>
        public List<DesktopSource> Sources { get; set; } = new List<DesktopSource>();

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
            string path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                FileName);
            Save(path);
        }

        private void Save(string path)
        {
            string json = new JavaScriptSerializer().Serialize(this);
            File.WriteAllText(path, json);
        }
    }
}
