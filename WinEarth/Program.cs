using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

namespace WinEarth
{
    static class Program
    {
        private static Config config;

        // HttpClient is thread-safe and intended to be shared for the life of the app;
        // a single instance handles all concurrent downloads.
        private static readonly HttpClient httpClient = new HttpClient();

        [STAThread]
        static void Main(string[] args)
        {
            config = Config.Load();

            // Config mode opens the GUI instead of running the updater loop. It is a
            // separate concern from the background updater, so it skips the single-
            // instance mutex and can run alongside a running updater.
            if (args != null && args.Any(a => string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase)))
            {
                bool launchRequested = ShowConfig();

                // The user clicked "Run WinEarth": spawn a fresh, detached instance with
                // no args so it goes down the normal background-updater path (acquiring
                // the single-instance mutex), then let this config process exit.
                if (launchRequested)
                {
                    LaunchBackgroundInstance();
                }
                return;
            }

            // Enforce a single running instance. The mutex name is prefixed with "Global\"
            // so the check spans all user sessions on the machine.
            bool createdNew;
            using (Mutex instanceMutex = new Mutex(true, @"Global\WinEarth-SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    Log("Another instance of WinEarth is already running. Exiting.");
                    return;
                }
                Run();
            }
        }

        /// <summary>
        /// Launches the configuration GUI and blocks until it is closed. Returns
        /// <c>true</c> if the user closed it via the "Run WinEarth" button.
        /// </summary>
        private static bool ShowConfig()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            using (ConfigForm form = new ConfigForm(config))
            {
                System.Windows.Forms.Application.Run(form);
                return form.LaunchRequested;
            }
        }

        /// <summary>
        /// Starts a new, detached WinEarth process with no arguments so it runs the
        /// background updater. The new process outlives this one, so closing the config
        /// GUI leaves WinEarth running in the background.
        /// </summary>
        private static void LaunchBackgroundInstance()
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Windows.Forms.Application.ExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception e)
            {
                Log("Failed to launch background instance|" + e.GetType().Name + "|" + e.Message);
            }
        }

        /// <summary>Appends a timestamped line to the configured log file.</summary>
        private static void Log(string message)
        {
            using (StreamWriter sw = File.AppendText(config.LogPath))
            {
                sw.WriteLine(DateTime.Now + "|" + message);
            }
        }

        private static void Run()
        {
            while (true)
            {
                // Per-monitor sources are picked in the --config GUI and persisted to
                // config.json. Standard monitors need a URL + crop; mesoscale monitors
                // resolve their URL at update time, so they only need the (fixed) crop.
                List<DesktopSource> sources = config.Sources
                    .Where(s => s.HasCrop && (s.IsMesoscale || !string.IsNullOrWhiteSpace(s.PageUrl)))
                    .ToList();

                if (sources.Count == 0)
                {
                    Log("No desktop sources configured. Run WinEarth --config to set up monitors.");
                }

                List<Task> tasks = new List<Task>();
                foreach (DesktopSource source in sources)
                {
                    Wallpaper screen;
                    try
                    {
                        screen = new Wallpaper(source.MonitorDevicePath);
                    }
                    catch (Exception e)
                    {
                        Log("Skipping monitor (unavailable)|" + source.MonitorDevicePath + "|" + e.Message);
                        continue;
                    }

                    // Stable per-monitor filename so concurrent updates don't collide.
                    string filename = "monitor_" + (uint)source.MonitorDevicePath.GetHashCode() + ".png";
                    Rectangle crop = new Rectangle(source.CropX, source.CropY, source.CropWidth, source.CropHeight);
                    tasks.Add(DownloadImageFileAsync(source, filename, crop, screen));
                }

                try
                {
                    Task.WhenAll(tasks).Wait();
                }
                catch (AggregateException ae)
                {
                    // Per-task failures are already logged inside DownloadImageFileAsync;
                    // this is a safety net for anything that still escapes.
                    foreach (Exception e in ae.Flatten().InnerExceptions)
                    {
                        Log("Update cycle error|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                    }
                }
                catch (Exception e)
                {
                    Log("Update cycle error|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                }
                // Mesoscale products refresh every minute; standard sectors every five.
                // Use the faster cadence whenever any mesoscale monitor is configured.
                Thread.Sleep(sources.Any(s => s.IsMesoscale) ? 60000 : 300000);
            }
        }
        private static async Task DownloadImageFileAsync(DesktopSource source, string filename, Rectangle crop, Wallpaper screen)
        {
            string filePath = Path.Combine(config.StoragePath, filename);
            bool success = false;
            int retries = 0;
            while (!success & retries < 3)
            {
                try
                {
                    // Resolving the page is inside the retry loop so a transient failure
                    // reading NOAA's mesoscale list is retried like any other hiccup.
                    Uri pageUrl = await ResolvePageUrlAsync(source);
                    // Resolve the scraped href against the page so relative links work too.
                    string imageUrl = new Uri(pageUrl, GetImageUrl(await httpClient.GetStringAsync(pageUrl), source.ItemIndex)).ToString();
                    await DownloadFileAsync(imageUrl, filePath);
                    Crop(filePath, crop, source.HighRes);
                    success = true;
                    screen.Set(filePath);
                }
                catch (Exception e)
                {
                    retries++;
                    Log(e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                }
            }
            if (!success)
            {
                Log("Failed to update!");
            }
        }

        /// <summary>
        /// Resolves the GOES page to scrape for a source. Standard sources use their
        /// saved URL; mesoscale sources look up the top available view for their tab
        /// from NOAA's running list each time, since those URLs are not fixed.
        /// </summary>
        private static async Task<Uri> ResolvePageUrlAsync(DesktopSource source)
        {
            if (source.IsMesoscale)
            {
                string page = await GoesImage.ResolveTopMesoscalePageAsync(
                    source.Mode == DesktopSource.ModeMesoWest);
                return new Uri(page);
            }
            return new Uri(source.PageUrl);
        }

        /// <summary>
        /// Downloads the resource at <paramref name="url"/> to <paramref name="filePath"/>.
        /// HttpClient has no built-in file download, so the response stream is copied to disk.
        /// </summary>
        private static async Task DownloadFileAsync(string url, string filePath)
        {
            using (HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
        }
        private static string GetImageUrl(string html, int index)
        {
            return GoesImage.ExtractImageUrl(html, index);
        }
        public static void Crop(string filePath, Rectangle crop, bool hd_screen)
        {
            Bitmap b = new Bitmap(filePath);
            using (Bitmap scaled_bitmap = new Bitmap(hd_screen ? 3840 : 1920, hd_screen ? 2160 : 1080))
            {
                using (Bitmap cropped_bitmap = new Bitmap(crop.Width, crop.Height))
                {
                    using (Graphics g = Graphics.FromImage(cropped_bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var wrapMode = new ImageAttributes())
                        {
                            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(b, -crop.X, -crop.Y);
                            b.Dispose();
                        }
                    }
                    using (Graphics g = Graphics.FromImage(scaled_bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var wrapMode = new ImageAttributes())
                        {
                            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(cropped_bitmap, new Rectangle(0, 0, hd_screen ? 3840 : 1920, hd_screen ? 2160 : 1080), 0, 0, cropped_bitmap.Width, cropped_bitmap.Height, GraphicsUnit.Pixel, wrapMode);
                            scaled_bitmap.Save(filePath, ImageFormat.Png);
                        }
                    }
                }
            }
        }
    }
}
