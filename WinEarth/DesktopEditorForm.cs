using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinEarth
{
    /// <summary>
    /// Per-desktop configuration dialog opened from a card in <see cref="ConfigForm"/>.
    /// A view-mode selector at the top chooses between a Standard source (enter a GOES
    /// page URL and item index, then pick a 16:9 crop) and the two Mesoscale views
    /// (GOES-East / GOES-West), which scrape NOAA's running mesoscale list and use a
    /// fixed item index and crop, so the rest of the panel is disabled for them.
    /// </summary>
    public class DesktopEditorForm : Form
    {
        // Mesoscale views are small and uniform, so rather than ask the user for a crop
        // we always take a fixed item index and a fixed 2000×1125 (16:9) box.
        private const int MesoItemIndex = 5;
        private static readonly Rectangle MesoCrop = new Rectangle(0, 438, 2000, 1125);

        private readonly Config config;
        private readonly string monitorDevicePath;

        private readonly RadioButton standardRadio;
        private readonly RadioButton mesoEastRadio;
        private readonly RadioButton mesoWestRadio;

        private readonly Label urlLabel;
        private readonly TextBox urlBox;
        private readonly Label indexLabel;
        private readonly NumericUpDown indexBox;
        private readonly CheckBox highResBox;
        private readonly Label cropLabel;
        private readonly Button selectCropButton;
        private readonly Button applyButton;

        // The crop chosen via the selector, in source-image pixels. Null until picked.
        // Only used in Standard mode; mesoscale modes use the fixed MesoCrop.
        private Rectangle? crop;

        public DesktopEditorForm(Config config, string monitorDevicePath, int desktopNumber)
        {
            this.config = config;
            this.monitorDevicePath = monitorDevicePath;

            Text = string.Format("Configure Desktop {0}", desktopNumber);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 334);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            standardRadio = new RadioButton
            {
                Text = "Standard",
                Location = new Point(16, 16),
                Width = 92,
                Checked = true,
            };
            mesoEastRadio = new RadioButton
            {
                Text = "Mesoscale East",
                Location = new Point(112, 16),
                Width = 130,
            };
            mesoWestRadio = new RadioButton
            {
                Text = "Mesoscale West",
                Location = new Point(248, 16),
                Width = 130,
            };
            standardRadio.CheckedChanged += (s, e) => UpdateModeState();
            mesoEastRadio.CheckedChanged += (s, e) => UpdateModeState();
            mesoWestRadio.CheckedChanged += (s, e) => UpdateModeState();

            urlLabel = new Label
            {
                Text = "GOES page URL",
                Location = new Point(16, 62),
                AutoSize = true,
            };
            urlBox = new TextBox
            {
                Location = new Point(16, 82),
                Width = 488,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            urlBox.TextChanged += (s, e) => UpdateApplyState();

            indexLabel = new Label
            {
                Text = "Item index (resolution level)",
                Location = new Point(16, 118),
                AutoSize = true,
            };
            indexBox = new NumericUpDown
            {
                Location = new Point(16, 138),
                Width = 80,
                Minimum = 0,
                Maximum = 100,
                Value = 6,
            };

            selectCropButton = new Button
            {
                Text = "Select crop…",
                Location = new Point(112, 136),
                Width = 110,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            selectCropButton.Click += SelectCropButton_Click;

            highResBox = new CheckBox
            {
                Text = "High-resolution (4K) output — scale crop to 3840 × 2160",
                Location = new Point(16, 174),
                AutoSize = true,
            };

            cropLabel = new Label
            {
                Location = new Point(16, 206),
                Width = 488,
                Height = 40,
                ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "No crop selected.",
            };

            applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(328, 282),
                Width = 90,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false,
            };
            applyButton.Click += ApplyButton_Click;

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(424, 282),
                Width = 80,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

            Controls.Add(standardRadio);
            Controls.Add(mesoEastRadio);
            Controls.Add(mesoWestRadio);
            Controls.Add(urlLabel);
            Controls.Add(urlBox);
            Controls.Add(indexLabel);
            Controls.Add(indexBox);
            Controls.Add(selectCropButton);
            Controls.Add(highResBox);
            Controls.Add(cropLabel);
            Controls.Add(applyButton);
            Controls.Add(cancelButton);

            CancelButton = cancelButton;

            LoadExisting();
            UpdateModeState();
        }

        /// <summary>Pre-fills the form from any already-saved source for this monitor.</summary>
        private void LoadExisting()
        {
            DesktopSource existing = config.FindSource(monitorDevicePath);
            if (existing == null)
            {
                return;
            }

            if (existing.Mode == DesktopSource.ModeMesoEast)
            {
                mesoEastRadio.Checked = true;
            }
            else if (existing.Mode == DesktopSource.ModeMesoWest)
            {
                mesoWestRadio.Checked = true;
            }
            else
            {
                standardRadio.Checked = true;
            }

            urlBox.Text = existing.PageUrl ?? string.Empty;
            indexBox.Value = Math.Max(indexBox.Minimum, Math.Min(indexBox.Maximum, existing.ItemIndex));
            highResBox.Checked = existing.HighRes;
            if (existing.HasCrop && !existing.IsMesoscale)
            {
                crop = new Rectangle(existing.CropX, existing.CropY, existing.CropWidth, existing.CropHeight);
                UpdateCropLabel();
            }
        }

        /// <summary>
        /// Enables the Standard-only controls only when Standard is selected; the
        /// mesoscale modes need nothing but the Apply/Cancel buttons.
        /// </summary>
        private void UpdateModeState()
        {
            bool standard = standardRadio.Checked;

            urlLabel.Enabled = standard;
            urlBox.Enabled = standard;
            indexLabel.Enabled = standard;
            indexBox.Enabled = standard;
            selectCropButton.Enabled = standard;
            highResBox.Enabled = standard;
            cropLabel.Enabled = standard;

            UpdateApplyState();
        }

        private async void SelectCropButton_Click(object sender, EventArgs e)
        {
            string url = urlBox.Text.Trim();
            if (url.Length == 0)
            {
                MessageBox.Show(this, "Enter a GOES page URL first.", "Select crop",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            selectCropButton.Enabled = false;
            selectCropButton.Text = "Fetching…";
            Cursor = Cursors.WaitCursor;

            Bitmap image = null;
            try
            {
                image = await GoesImage.DownloadAsync(url, (int)indexBox.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not fetch the image:" + Environment.NewLine + ex.Message,
                    "Select crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
                selectCropButton.Text = "Select crop…";
                selectCropButton.Enabled = true;
            }

            if (image == null)
            {
                return;
            }

            using (image)
            using (var selector = new CropSelectorForm(image, crop))
            {
                if (selector.ShowDialog(this) == DialogResult.OK)
                {
                    crop = selector.Crop;
                    UpdateCropLabel();
                    UpdateApplyState();
                }
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            DesktopSource source = mesoEastRadio.Checked || mesoWestRadio.Checked
                ? BuildMesoscaleSource()
                : BuildStandardSource();
            if (source == null)
            {
                return;
            }

            try
            {
                config.SaveSource(source);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not save the configuration:" + Environment.NewLine + ex.Message,
                    "Apply", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Builds a Standard source from the URL/index/crop fields.</summary>
        private DesktopSource BuildStandardSource()
        {
            if (!crop.HasValue)
            {
                return null;
            }

            Rectangle c = crop.Value;
            return new DesktopSource
            {
                MonitorDevicePath = monitorDevicePath,
                Mode = DesktopSource.ModeStandard,
                PageUrl = urlBox.Text.Trim(),
                ItemIndex = (int)indexBox.Value,
                CropX = c.X,
                CropY = c.Y,
                CropWidth = c.Width,
                CropHeight = c.Height,
                HighRes = highResBox.Checked,
            };
        }

        /// <summary>
        /// Builds a mesoscale source: no URL or user crop, just the fixed item index
        /// and box. The background updater resolves the actual image from NOAA's
        /// running mesoscale list for the selected satellite.
        /// </summary>
        private DesktopSource BuildMesoscaleSource()
        {
            return new DesktopSource
            {
                MonitorDevicePath = monitorDevicePath,
                Mode = mesoEastRadio.Checked ? DesktopSource.ModeMesoEast : DesktopSource.ModeMesoWest,
                PageUrl = string.Empty,
                ItemIndex = MesoItemIndex,
                CropX = MesoCrop.X,
                CropY = MesoCrop.Y,
                CropWidth = MesoCrop.Width,
                CropHeight = MesoCrop.Height,
                HighRes = false,
            };
        }

        private void UpdateCropLabel()
        {
            if (!crop.HasValue)
            {
                cropLabel.Text = "No crop selected.";
                return;
            }
            Rectangle c = crop.Value;
            cropLabel.Text = string.Format(
                "Crop: {0} × {1} at ({2}, {3})", c.Width, c.Height, c.X, c.Y);
        }

        private void UpdateApplyState()
        {
            // Mesoscale modes need no further input, so Apply is always available.
            // Standard mode still requires a URL and a chosen crop.
            if (mesoEastRadio.Checked || mesoWestRadio.Checked)
            {
                applyButton.Enabled = true;
                return;
            }
            applyButton.Enabled = crop.HasValue && urlBox.Text.Trim().Length > 0;
        }
    }
}
