namespace WinEarth
{
    using System.Drawing;
    using System.Drawing.Imaging;
    public class Wallpaper
    {
        private readonly string monitor;
        private readonly IDesktopWallpaper wallpaper;
        public Wallpaper(uint monitorIndex) {
            wallpaper = new DesktopWallpaper() as IDesktopWallpaper;
            monitor = wallpaper.GetMonitorDevicePathAt(monitorIndex);
        }

        /// <summary>
        /// Targets a monitor by its stable shell device path (as saved per-desktop in
        /// the config). This is the same identifier <see cref="IDesktopWallpaper"/>
        /// returns from GetMonitorDevicePathAt and accepts in SetWallpaper.
        /// </summary>
        public Wallpaper(string monitorDevicePath) {
            wallpaper = new DesktopWallpaper() as IDesktopWallpaper;
            monitor = monitorDevicePath;
        }

        public void Set(string file, string storagePath)
        {
            Image img = Image.FromFile(file);
            img.Save(storagePath, ImageFormat.Png);
            wallpaper.SetWallpaper(monitor, storagePath);
        }

        public void Set(string file)
        {
            wallpaper.SetWallpaper(monitor, file);
        }
    }
}