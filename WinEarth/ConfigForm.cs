using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinEarth
{
    /// <summary>
    /// GUI shown when WinEarth is launched with <c>--config</c>. It enumerates the
    /// desktops (monitors) reported by the shell's <see cref="IDesktopWallpaper"/>
    /// interface and presents one card per desktop. This is the foundation for the
    /// upcoming preview mode (per-desktop source URL entry and crop selection).
    /// </summary>
    public class ConfigForm : Form
    {
        private readonly Config config;

        public ConfigForm(Config config)
        {
            this.config = config;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "WinEarth Configuration";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(840, 560);
            MinimumSize = new Size(480, 360);
            Font = new Font("Segoe UI", 9f);

            var header = new Label
            {
                Text = "Desktops",
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(16, 12, 16, 0),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            };

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
            };

            // Header is added last so it docks above the fill panel in z-order.
            Controls.Add(layout);
            Controls.Add(header);

            try
            {
                foreach (DesktopInfo desktop in EnumerateDesktops())
                {
                    layout.Controls.Add(CreateDesktopCard(desktop));
                }
            }
            catch (Exception e)
            {
                layout.Controls.Add(new Label
                {
                    AutoSize = true,
                    ForeColor = Color.Firebrick,
                    Text = "Could not enumerate desktops:" + Environment.NewLine + e.Message,
                });
            }
        }

        /// <summary>
        /// Queries the shell for every connected monitor and its current wallpaper.
        /// </summary>
        private static IEnumerable<DesktopInfo> EnumerateDesktops()
        {
            var shell = new DesktopWallpaper() as IDesktopWallpaper;
            uint count = shell.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = shell.GetMonitorDevicePathAt(i);

                // A monitor path can be present but inactive (no display attached);
                // GetMonitorRECT throws for those, so treat them as unavailable.
                Rect rect;
                bool active = true;
                try
                {
                    rect = shell.GetMonitorRECT(id);
                }
                catch
                {
                    rect = default(Rect);
                    active = false;
                }

                string wallpaper = null;
                try
                {
                    wallpaper = shell.GetWallpaper(id);
                }
                catch
                {
                    // Leave wallpaper null if it can't be read.
                }

                yield return new DesktopInfo
                {
                    Index = i,
                    DevicePath = id,
                    Bounds = rect,
                    Active = active,
                    WallpaperPath = wallpaper,
                };
            }
        }

        private static Control CreateDesktopCard(DesktopInfo desktop)
        {
            var card = new Panel
            {
                Width = 240,
                Height = 220,
                Margin = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
            };

            var preview = new PictureBox
            {
                Dock = DockStyle.Top,
                Height = 135,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(24, 24, 24),
                Image = LoadThumbnail(desktop.WallpaperPath),
            };

            int width = desktop.Bounds.Right - desktop.Bounds.Left;
            int height = desktop.Bounds.Bottom - desktop.Bounds.Top;
            string resolution = desktop.Active
                ? string.Format("{0} × {1}", width, height)
                : "inactive";

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(8, 4, 8, 0),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Text = string.Format("Desktop {0}", desktop.Index + 1),
            };

            var details = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 0, 8, 4),
                ForeColor = Color.DimGray,
                Text = string.Format(
                    "{0}{1}Position: ({2}, {3})",
                    resolution,
                    Environment.NewLine,
                    desktop.Bounds.Left,
                    desktop.Bounds.Top),
            };

            // Docked controls are added inner-most first so they stack correctly.
            card.Controls.Add(details);
            card.Controls.Add(title);
            card.Controls.Add(preview);
            return card;
        }

        /// <summary>
        /// Loads the wallpaper image without locking the file on disk (the running
        /// updater rewrites these), returning <c>null</c> if it can't be read.
        /// </summary>
        private static Image LoadThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var source = Image.FromStream(stream))
                {
                    return new Bitmap(source);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A single enumerated desktop/monitor and its current state.</summary>
        private sealed class DesktopInfo
        {
            public uint Index;
            public string DevicePath;
            public Rect Bounds;
            public bool Active;
            public string WallpaperPath;
        }
    }
}
