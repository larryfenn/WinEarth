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

        // Serializes log writes: downloads run concurrently and File.AppendText opens
        // the log with exclusive access, so concurrent writers would throw IOException.
        private static readonly object logLock = new object();

        [STAThread]
        static void Main(string[] args)
        {
            // Check before Load(), which writes a defaults file when one is missing.
            bool firstRun = !Config.Exists();
            config = Config.Load();

            // Config mode opens the GUI instead of running the updater loop. It is a
            // separate concern from the background updater, so it skips the single-
            // instance mutex and can run alongside a running updater. We also open it
            // automatically on a fresh install (no config.json yet) so the user lands in
            // setup instead of an updater loop with nothing configured.
            bool configRequested = args != null && args.Any(a => string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase));
            if (configRequested || firstRun)
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
            // If an updater is already running, a new process would just lose the
            // single-instance race and exit silently. The running instance reloads
            // config every cycle (see Run), so it will pick up the just-saved settings
            // within a minute — tell the user that instead of spawning a doomed process.
            if (InstanceAlreadyRunning())
            {
                System.Windows.Forms.MessageBox.Show(
                    "WinEarth is already running in the background. Your changes will take effect within a minute.",
                    "WinEarth",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

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

        /// <summary>
        /// Reports whether a background updater is already running, by probing for the
        /// single-instance mutex without taking ownership. If the mutex can't be opened
        /// for reasons other than non-existence (e.g. access denied), we conservatively
        /// report "not running" and let the spawned process's own check arbitrate.
        /// </summary>
        private static bool InstanceAlreadyRunning()
        {
            try
            {
                using (Mutex.OpenExisting(@"Global\WinEarth-SingleInstance"))
                {
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (Exception e)
            {
                Log("Could not probe single-instance mutex|" + e.GetType().Name + "|" + e.Message);
                return false;
            }
        }

        // Cap the log at ~5 MB and keep a single rotated archive, so a long-running
        // instance that keeps failing can't grow corelog.txt without bound.
        private const long MaxLogBytes = 5 * 1024 * 1024;

        /// <summary>Appends a timestamped line to the configured log file.</summary>
        private static void Log(string message)
        {
            lock (logLock)
            {
                try
                {
                    RotateLogIfNeeded();
                }
                catch
                {
                    // Rotation is best-effort; never let it block the actual write.
                }

                using (StreamWriter sw = File.AppendText(Config.LogPath))
                {
                    sw.WriteLine(DateTime.Now + "|" + message);
                }
            }
        }

        /// <summary>
        /// Rolls the log over to a single ".1" archive once it exceeds
        /// <see cref="MaxLogBytes"/>, replacing any previous archive. Caps total log
        /// footprint at roughly twice the limit. Caller holds <see cref="logLock"/>.
        /// </summary>
        private static void RotateLogIfNeeded()
        {
            FileInfo info = new FileInfo(Config.LogPath);
            if (!info.Exists || info.Length <= MaxLogBytes)
            {
                return;
            }

            string archive = Config.LogPath + ".1";
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
            File.Move(Config.LogPath, archive);
        }

        private static void Run()
        {
            // The loop wakes on each wall-clock minute boundary. Mesoscale monitors
            // refresh every minute; standard sectors refresh only on minutes ending in
            // 2 or 7 (minute % 5 == 2), a couple of minutes after NOAA posts each new
            // 5-minute image. The first pass updates everything so a freshly launched
            // WinEarth fills its wallpapers immediately instead of waiting for the slot.
            bool firstPass = true;
            while (true)
            {
                // Pick up edits made through --config while this updater is running.
                // Reload returns null (and we keep the last-known-good config) if the
                // file is briefly unreadable, e.g. mid-write from the config GUI.
                Config reloaded = Config.Reload();
                if (reloaded != null)
                {
                    config = reloaded;
                }

                bool standardDue = firstPass || DateTime.Now.Minute % 5 == 2;

                // Per-monitor sources are picked in the --config GUI and persisted to
                // config.json. Standard monitors need a URL + crop; mesoscale monitors
                // resolve their URL at update time, so they only need the (fixed) crop.
                List<DesktopSource> configured = config.Sources
                    .Where(s => s.HasCrop && (s.IsMesoscale || !string.IsNullOrWhiteSpace(s.PageUrl)))
                    .ToList();

                // Gate the "nothing configured" hint behind the standard slot so it keeps
                // its old ~5-minute cadence instead of repeating every single minute.
                if (configured.Count == 0 && standardDue)
                {
                    Log("No desktop sources configured. Run WinEarth --config to set up monitors.");
                }

                List<DesktopSource> due = configured
                    .Where(s => s.IsMesoscale || standardDue)
                    .ToList();

                List<Task> tasks = new List<Task>();
                List<UpdateResult> results = new List<UpdateResult>();
                foreach (DesktopSource source in due)
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
                    string filename = "monitor_" + StableHash(source.MonitorDevicePath) + ".png";
                    Rectangle crop = new Rectangle(source.CropX, source.CropY, source.CropWidth, source.CropHeight);
                    UpdateResult result = new UpdateResult { Screen = screen };
                    results.Add(result);
                    tasks.Add(DownloadImageFileAsync(source, filename, crop, result));
                }

                // Blocking the STA Main thread here is safe only because the updater path
                // never installs a SynchronizationContext (no Application.Run), so the
                // awaited continuations inside the tasks resume on the thread pool rather
                // than trying to marshal back to this now-blocked thread. Do not add a
                // sync-context-bound await to this path or .Wait() can deadlock.
                try
                {
                    Task.WhenAll(tasks).Wait();
                }
                catch (AggregateException ae)
                {
                    // Each task body swallows and logs its own failures, so today nothing
                    // faults the combined task. These handlers are a safety net that only
                    // fires if a future change lets an exception escape a task body.
                    foreach (Exception e in ae.Flatten().InnerExceptions)
                    {
                        Log("Update cycle error|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                    }
                }
                catch (Exception e)
                {
                    Log("Update cycle error|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                }

                // Apply the wallpapers here, on the STA Main thread. SetWallpaper is an
                // apartment-threaded shell COM call; invoking it from the MTA download
                // threads relies on COM marshaling and can throw COMException /
                // InvalidCastException. Each Wallpaper RCW is released once it's used.
                foreach (UpdateResult result in results)
                {
                    try
                    {
                        if (result.Downloaded)
                        {
                            result.Screen.Set(result.FilePath);
                        }
                    }
                    catch (Exception e)
                    {
                        Log("Failed to set wallpaper|" + e.GetType().Name + "|" + e.Message);
                    }
                    finally
                    {
                        result.Screen.Dispose();
                    }
                }

                firstPass = false;
                SleepUntilNextMinute();
            }
        }

        /// <summary>
        /// Blocks until the next wall-clock minute boundary so updates land on the
        /// minute ("sharp"). A download cycle that overruns a minute simply realigns to
        /// the following boundary on the next pass.
        /// </summary>
        private static void SleepUntilNextMinute()
        {
            DateTime now = DateTime.Now;
            DateTime nextMinute = now.AddMinutes(1).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
            TimeSpan delay = nextMinute - now;
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }
        }
        private static async Task DownloadImageFileAsync(DesktopSource source, string filename, Rectangle crop, UpdateResult result)
        {
            string filePath = Path.Combine(Config.StoragePath, filename);
            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    // Resolving the page is inside the retry loop so a transient failure
                    // reading NOAA's mesoscale list is retried like any other hiccup.
                    Uri pageUrl = await ResolvePageUrlAsync(source);
                    // Resolve the scraped href against the page so relative links work too.
                    string imageUrl = new Uri(pageUrl, GoesImage.ExtractImageUrl(await GoesImage.HttpClient.GetStringAsync(pageUrl), source.ItemIndex)).ToString();
                    await DownloadFileAsync(imageUrl, filePath);
                    Crop(filePath, crop, source.HighRes);
                    // The COM SetWallpaper is deferred to the STA Main thread (see Run),
                    // so success here means the image is downloaded and ready to apply.
                    result.FilePath = filePath;
                    result.Downloaded = true;
                    return;
                }
                catch (Exception e)
                {
                    retries++;
                    Log(e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);

                    // Back off before retrying so a transient NOAA failure isn't hammered.
                    if (retries < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * retries));
                    }
                }
            }
            Log("Failed to update!");
        }

        /// <summary>
        /// Carries one monitor's update from the concurrent download stage to the
        /// STA-thread apply stage: the COM <see cref="Wallpaper"/> handle plus, once the
        /// image is downloaded and cropped, the file to set.
        /// </summary>
        private sealed class UpdateResult
        {
            public Wallpaper Screen;
            public string FilePath;
            public bool Downloaded;
        }

        /// <summary>
        /// Runtime-independent FNV-1a hash, used for the per-monitor cache filename.
        /// <see cref="string.GetHashCode"/> is not stable across processes if randomized
        /// string hashing is ever enabled, which would orphan the per-monitor PNGs.
        /// </summary>
        private static uint StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261; // FNV-1a 32-bit offset basis
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619; // FNV-1a 32-bit prime
                }
                return hash;
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
            using (HttpResponseMessage response = await GoesImage.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
        }
        private static void Crop(string filePath, Rectangle crop, bool hd_screen)
        {
            int destWidth = hd_screen ? 3840 : 1920;
            int destHeight = hd_screen ? 2160 : 1080;
            using (Bitmap scaled_bitmap = new Bitmap(destWidth, destHeight))
            {
                // Crop and scale in a single pass: copy the crop rectangle from the
                // source straight into the full-size destination.
                using (Bitmap source = new Bitmap(filePath))
                using (Graphics g = Graphics.FromImage(scaled_bitmap))
                using (var attrs = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // Avoid edge bleeding from neighboring pixels when sampling the crop.
                    attrs.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(0, 0, destWidth, destHeight), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, attrs);
                }
                // source is now disposed, releasing its lock on filePath so we can
                // overwrite it in place.
                scaled_bitmap.Save(filePath, ImageFormat.Png);
            }
        }
    }
}
