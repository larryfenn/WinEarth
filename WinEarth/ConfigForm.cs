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
        private FlowLayoutPanel layout;

        /// <summary>
        /// Set when the user closes the GUI via the "Run WinEarth" button, signalling
        /// that the background updater should be started after the form closes.
        /// </summary>
        public bool LaunchRequested { get; private set; }

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

            layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
            };

            var runButton = new Button
            {
                Text = "Run WinEarth",
                Width = 140,
                Height = 32,
                Anchor = AnchorStyles.Right,
                FlatStyle = FlatStyle.System,
            };
            runButton.Click += (s, e) =>
            {
                // Closing with this flag set tells Program to start the background
                // updater, just as if WinEarth had been launched without --config.
                LaunchRequested = true;
                Close();
            };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(16, 8, 16, 8),
            };
            runButton.Location = new Point(
                footer.ClientSize.Width - runButton.Width - footer.Padding.Right,
                footer.Padding.Top);
            footer.Controls.Add(runButton);

            // Header and footer are added after the fill panel so they dock to the
            // edges above it in z-order.
            Controls.Add(layout);
            Controls.Add(footer);
            Controls.Add(header);

            ReloadDesktops();
        }

        /// <summary>Rebuilds the desktop cards from the current shell + config state.</summary>
        private void ReloadDesktops()
        {
            layout.SuspendLayout();
            layout.Controls.Clear();
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
            finally
            {
                layout.ResumeLayout();
            }
        }

        /// <summary>Opens the per-desktop editor and refreshes the cards if it applied.</summary>
        private void OpenEditor(DesktopInfo desktop)
        {
            using (var editor = new DesktopEditorForm(config, desktop.DevicePath, (int)desktop.Index + 1))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    ReloadDesktops();
                }
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

        private Control CreateDesktopCard(DesktopInfo desktop)
        {
            var card = new Panel
            {
                Width = 240,
                Height = 256,
                Margin = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
            };

            var preview = new PictureBox
            {
                Dock = DockStyle.Top,
                Height = 135,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(24, 24, 24),
                Image = LoadThumbnail(desktop.WallpaperPath),
                Cursor = Cursors.Hand,
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
                Text = DescribeSource(desktop, resolution),
            };

            var configureButton = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                Text = "Configure…",
                FlatStyle = FlatStyle.System,
            };
            configureButton.Click += (s, e) => OpenEditor(desktop);

            // Clicking anywhere on the card (image, title, details) also opens the editor.
            EventHandler open = (s, e) => OpenEditor(desktop);
            card.Click += open;
            preview.Click += open;
            title.Click += open;
            details.Click += open;

            // Docked controls are added inner-most first so they stack correctly.
            card.Controls.Add(details);
            card.Controls.Add(configureButton);
            card.Controls.Add(title);
            card.Controls.Add(preview);
            return card;
        }

        /// <summary>Builds the card's detail text, including any configured GOES source.</summary>
        private string DescribeSource(DesktopInfo desktop, string resolution)
        {
            DesktopSource source = config.FindSource(desktop.DevicePath);
            string baseLine = string.Format(
                "{0}{1}Position: ({2}, {3})",
                resolution,
                Environment.NewLine,
                desktop.Bounds.Left,
                desktop.Bounds.Top);

            if (source == null || !source.HasCrop)
            {
                return baseLine + Environment.NewLine + "Source: not configured";
            }

            if (source.IsMesoscale)
            {
                return baseLine + Environment.NewLine + string.Format(
                    "Source: Mesoscale {0}",
                    source.Mode == DesktopSource.ModeMesoEast ? "East" : "West");
            }

            return string.Format(
                "{0}{1}Crop: {2} × {3} (item {4}){5}",
                baseLine,
                Environment.NewLine,
                source.CropWidth,
                source.CropHeight,
                source.ItemIndex,
                source.HighRes ? " · 4K" : string.Empty);
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
