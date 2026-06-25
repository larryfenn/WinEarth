using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinEarth
{
    /// <summary>
    /// Modal dialog that shows a fetched GOES image and lets the user rubber-band a
    /// crop rectangle locked to 16:9. The chosen crop is exposed in source-image
    /// pixel coordinates via <see cref="Crop"/> (valid only when the dialog returns
    /// <see cref="DialogResult.OK"/>).
    /// </summary>
    public class CropSelectorForm : Form
    {
        // Width / Height of the locked aspect ratio.
        private const float AspectRatio = 16f / 9f;

        // Anything smaller than this (in image pixels, on the long edge) is ignored
        // so a stray click doesn't register as a tiny crop.
        private const int MinCropWidth = 32;

        private readonly Bitmap image;
        private readonly CanvasPanel canvas;
        private readonly Label statusLabel;
        private readonly Button okButton;

        // Selection in image-pixel coordinates. Empty until the user drags one out.
        private RectangleF selection;

        private bool dragging;
        private PointF dragAnchor;

        /// <summary>The chosen crop in source-image pixels. Only meaningful on OK.</summary>
        public Rectangle Crop { get; private set; }

        public CropSelectorForm(Bitmap image, Rectangle? initialCrop)
        {
            this.image = image;

            Text = "Select crop (locked to 16:9)";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(520, 400);
            Font = new Font("Segoe UI", 9f);

            var buttonBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(12, 8, 12, 8),
            };

            okButton = new Button
            {
                Text = "Use this crop",
                Dock = DockStyle.Right,
                Width = 120,
                DialogResult = DialogResult.OK,
                Enabled = false,
            };
            okButton.Click += (s, e) => CommitSelection();

            var cancelButton = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Right,
                Width = 90,
                Margin = new Padding(8, 0, 0, 0),
                DialogResult = DialogResult.Cancel,
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = false,
                Width = 520,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "Drag on the image to draw a 16:9 crop region.",
            };

            // Right-docked controls stack right-to-left in add order, so add Cancel
            // first (outermost right) then OK to its left.
            buttonBar.Controls.Add(cancelButton);
            buttonBar.Controls.Add(okButton);
            buttonBar.Controls.Add(statusLabel);

            canvas = new CanvasPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 24),
            };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;

            Controls.Add(canvas);
            Controls.Add(buttonBar);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            if (initialCrop.HasValue && initialCrop.Value.Width > 0 && initialCrop.Value.Height > 0)
            {
                selection = initialCrop.Value;
                okButton.Enabled = true;
                UpdateStatus();
            }
        }

        private void CommitSelection()
        {
            Crop = Rectangle.Round(selection);
        }

        // --- Coordinate mapping between the control and the zoomed image -------------

        /// <summary>
        /// The rectangle (in control coordinates) where the image is drawn, scaled to
        /// fit the canvas while preserving aspect (letterboxed), plus the scale used.
        /// </summary>
        private RectangleF GetDisplayRect(out float scale)
        {
            float panelW = canvas.ClientSize.Width;
            float panelH = canvas.ClientSize.Height;
            scale = Math.Min(panelW / image.Width, panelH / image.Height);
            float w = image.Width * scale;
            float h = image.Height * scale;
            float x = (panelW - w) / 2f;
            float y = (panelH - h) / 2f;
            return new RectangleF(x, y, w, h);
        }

        /// <summary>Maps a control point to image-pixel coordinates, clamped to the image.</summary>
        private PointF ControlToImage(Point p)
        {
            float scale;
            RectangleF disp = GetDisplayRect(out scale);
            float ix = (p.X - disp.X) / scale;
            float iy = (p.Y - disp.Y) / scale;
            ix = Clamp(ix, 0, image.Width);
            iy = Clamp(iy, 0, image.Height);
            return new PointF(ix, iy);
        }

        /// <summary>Maps an image-pixel rectangle to control coordinates for drawing.</summary>
        private RectangleF ImageToControl(RectangleF r)
        {
            float scale;
            RectangleF disp = GetDisplayRect(out scale);
            return new RectangleF(
                disp.X + r.X * scale,
                disp.Y + r.Y * scale,
                r.Width * scale,
                r.Height * scale);
        }

        // --- Selection geometry ------------------------------------------------------

        /// <summary>
        /// Builds a 16:9 rectangle anchored at <paramref name="anchor"/>, growing toward
        /// <paramref name="cursor"/>, clamped to the image so it never spills past an edge.
        /// </summary>
        private RectangleF BuildRatioRect(PointF anchor, PointF cursor)
        {
            int dirX = cursor.X >= anchor.X ? 1 : -1;
            int dirY = cursor.Y >= anchor.Y ? 1 : -1;

            // How much room the box has from the anchor toward the cursor.
            float availW = dirX > 0 ? image.Width - anchor.X : anchor.X;
            float availH = dirY > 0 ? image.Height - anchor.Y : anchor.Y;

            float w = Math.Abs(cursor.X - anchor.X);
            float h = Math.Abs(cursor.Y - anchor.Y);

            // Enforce 16:9 from whichever axis the user pulled further.
            if (w / AspectRatio >= h)
            {
                h = w / AspectRatio;
            }
            else
            {
                w = h * AspectRatio;
            }

            // Keep the whole box inside the image while preserving the ratio.
            w = Math.Min(w, Math.Min(availW, availH * AspectRatio));
            h = w / AspectRatio;

            float x = dirX > 0 ? anchor.X : anchor.X - w;
            float y = dirY > 0 ? anchor.Y : anchor.Y - h;
            return new RectangleF(x, y, w, h);
        }

        // --- Mouse handling ----------------------------------------------------------

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            dragging = true;
            dragAnchor = ControlToImage(e.Location);
            selection = RectangleF.Empty;
            canvas.Invalidate();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }
            selection = BuildRatioRect(dragAnchor, ControlToImage(e.Location));
            UpdateStatus();
            canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            dragging = false;
            bool valid = selection.Width >= MinCropWidth;
            if (!valid)
            {
                selection = RectangleF.Empty;
            }
            okButton.Enabled = valid;
            UpdateStatus();
            canvas.Invalidate();
        }

        private void UpdateStatus()
        {
            if (selection.Width < 1)
            {
                statusLabel.Text = "Drag on the image to draw a 16:9 crop region.";
                return;
            }
            statusLabel.Text = string.Format(
                "Crop: {0} × {1} at ({2}, {3})  —  source image is {4} × {5}",
                (int)Math.Round(selection.Width),
                (int)Math.Round(selection.Height),
                (int)Math.Round(selection.X),
                (int)Math.Round(selection.Y),
                image.Width,
                image.Height);
        }

        // --- Painting ----------------------------------------------------------------

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float scale;
            RectangleF disp = GetDisplayRect(out scale);
            g.DrawImage(image, disp);

            if (selection.Width < 1)
            {
                return;
            }

            RectangleF sel = ImageToControl(selection);

            // Dim everything outside the selection to make the crop region pop.
            using (var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                var region = new Region(disp);
                region.Exclude(sel);
                g.FillRegion(shade, region);
                region.Dispose();
            }

            using (var pen = new Pen(Color.FromArgb(255, 80, 180, 255), 2f))
            {
                g.DrawRectangle(pen, sel.X, sel.Y, sel.Width, sel.Height);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // The image is owned by the caller; do not dispose it here.
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Double-buffered panel so the rubber-band redraw doesn't flicker.</summary>
        private sealed class CanvasPanel : Panel
        {
            public CanvasPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}
