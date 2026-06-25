using System;
using System.Runtime.InteropServices;

namespace WinEarth
{
    public class Wallpaper : IDisposable
    {
        private readonly string monitor;
        private IDesktopWallpaper wallpaper;

        /// <summary>
        /// Targets a monitor by its stable shell device path (as saved per-desktop in
        /// the config). This is the same identifier <see cref="IDesktopWallpaper"/>
        /// returns from GetMonitorDevicePathAt and accepts in SetWallpaper.
        /// </summary>
        public Wallpaper(string monitorDevicePath) {
            wallpaper = new DesktopWallpaper() as IDesktopWallpaper;
            monitor = monitorDevicePath;
        }

        public void Set(string file)
        {
            wallpaper.SetWallpaper(monitor, file);
        }

        /// <summary>
        /// Releases the underlying COM RCW. The updater allocates one per monitor per
        /// cycle; without this the finalizer eventually reclaims them, but it churns a
        /// COM object per monitor every minute.
        /// </summary>
        public void Dispose()
        {
            if (wallpaper != null)
            {
                Marshal.ReleaseComObject(wallpaper);
                wallpaper = null;
            }
        }
    }
}
