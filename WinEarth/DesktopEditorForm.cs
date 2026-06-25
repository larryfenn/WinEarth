using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinEarth
{
    /// <summary>
    /// Per-desktop configuration dialog opened from a card in <see cref="ConfigForm"/>.
    /// Lets the user enter the GOES page URL and item index, fetch the image to pick a
    /// 16:9 crop, and apply (persist) the source for that monitor.
    /// </summary>
    public class DesktopEditorForm : Form
    {
        private readonly Config config;
        private readonly string monitorDevicePath;

        private readonly TextBox urlBox;
        private readonly NumericUpDown indexBox;
        private readonly CheckBox highResBox;
        private readonly Label cropLabel;
        private readonly Button selectCropButton;
        private readonly Button applyButton;

        // The crop chosen via the selector, in source-image pixels. Null until picked.
        private Rectangle? crop;

        public DesktopEditorForm(Config config, string monitorDevicePath, int desktopNumber)
        {
            this.config = config;
            this.monitorDevicePath = monitorDevicePath;

            Text = string.Format("Configure Desktop {0}", desktopNumber);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 290);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var urlLabel = new Label
            {
                Text = "GOES page URL",
                Location = new Point(16, 18),
                AutoSize = true,
            };
            urlBox = new TextBox
            {
                Location = new Point(16, 38),
                Width = 488,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            urlBox.TextChanged += (s, e) => UpdateApplyState();

            var indexLabel = new Label
            {
                Text = "Item index (resolution level)",
                Location = new Point(16, 74),
                AutoSize = true,
            };
            indexBox = new NumericUpDown
            {
                Location = new Point(16, 94),
                Width = 80,
                Minimum = 0,
                Maximum = 100,
                Value = 6,
            };

            selectCropButton = new Button
            {
                Text = "Select crop…",
                Location = new Point(112, 92),
                Width = 110,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            selectCropButton.Click += SelectCropButton_Click;

            highResBox = new CheckBox
            {
                Text = "High-resolution (4K) output — scale crop to 3840 × 2160",
                Location = new Point(16, 130),
                AutoSize = true,
            };

            cropLabel = new Label
            {
                Location = new Point(16, 162),
                Width = 488,
                Height = 40,
                ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "No crop selected.",
            };

            applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(328, 238),
                Width = 90,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false,
            };
            applyButton.Click += ApplyButton_Click;

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(424, 238),
                Width = 80,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

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
            UpdateApplyState();
        }

        /// <summary>Pre-fills the form from any already-saved source for this monitor.</summary>
        private void LoadExisting()
        {
            DesktopSource existing = config.FindSource(monitorDevicePath);
            if (existing == null)
            {
                return;
            }

            urlBox.Text = existing.PageUrl ?? string.Empty;
            indexBox.Value = Math.Max(indexBox.Minimum, Math.Min(indexBox.Maximum, existing.ItemIndex));
            highResBox.Checked = existing.HighRes;
            if (existing.HasCrop)
            {
                crop = new Rectangle(existing.CropX, existing.CropY, existing.CropWidth, existing.CropHeight);
                UpdateCropLabel();
            }
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
            if (!crop.HasValue)
            {
                return;
            }

            Rectangle c = crop.Value;
            var source = new DesktopSource
            {
                MonitorDevicePath = monitorDevicePath,
                PageUrl = urlBox.Text.Trim(),
                ItemIndex = (int)indexBox.Value,
                CropX = c.X,
                CropY = c.Y,
                CropWidth = c.Width,
                CropHeight = c.Height,
                HighRes = highResBox.Checked,
            };

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
            // A crop is required so the updater always has a complete source to apply.
            applyButton.Enabled = crop.HasValue && urlBox.Text.Trim().Length > 0;
        }
    }
}
