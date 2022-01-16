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