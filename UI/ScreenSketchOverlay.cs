using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AIsketch.UI;
using Rhino;

namespace AIsketch.UI
{
    /// <summary>
    /// Screenshot-based sketch editor (snip-style):
    /// - Captures the entire virtual screen to a bitmap.
    /// - Shows a modal editor where the user can draw annotations on the screenshot.
    /// - Provides Clear / Done / Cancel controls and saves the annotated PNG to Documents/Pictures/AIsketch.
    /// - Raises Completed(filePath) or Canceled.
    ///
    /// This implementation runs the editor on a dedicated STA thread so the WinForms message loop
    /// is isolated from Rhino's UI thread.
    /// </summary>
    public class ScreenSketchOverlay : IDisposable
    {
        private Thread _uiThread;
        public event Action<string> Completed;
        public event Action Canceled;
        public static ScreenSketchOverlay Instance { get; private set; }

        public ScreenSketchOverlay()
        {
            Instance = this;
        }

        public void ShowOverlay()
        {
            if (_uiThread != null && _uiThread.IsAlive)
                return;

            _uiThread = new Thread(RunEditorThread);
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Start();
        }

        private void RunEditorThread()
        {
            try
            {
                // Capture union of all screens
                Rectangle bounds = Rectangle.Empty;
                foreach (var scr in Screen.AllScreens)
                {
                    if (bounds == Rectangle.Empty) bounds = scr.Bounds;
                    else bounds = Rectangle.Union(bounds, scr.Bounds);
                }
                if (bounds.Width <= 1 || bounds.Height <= 1)
                {
                    bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 800, 600);
                }

                using (var screenshot = new Bitmap(bounds.Width, bounds.Height))
                using (var g = Graphics.FromImage(screenshot))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

                    // Build the editor form
                    var form = new Form();
                    form.FormBorderStyle = FormBorderStyle.Sizable;
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.Text = "AI Sketch - Edit Screenshot";
                    form.ClientSize = new Size(Math.Min(bounds.Width, 1600), Math.Min(bounds.Height, 900));

                    // Picture box to show the screenshot scaled to fit the form client area
                    var picture = new PictureBox { Dock = DockStyle.Fill, Image = (Image)screenshot.Clone(), SizeMode = PictureBoxSizeMode.Zoom };
                    form.Controls.Add(picture);

                    // Canvas bitmap tracks the user annotations at the screenshot's native resolution
                    var canvas = new Bitmap(screenshot.Width, screenshot.Height);
                    var canvasG = Graphics.FromImage(canvas);
                    canvasG.SmoothingMode = SmoothingMode.AntiAlias;
                    canvasG.Clear(Color.Transparent);

                    // UI panel with buttons
                    var panel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(220, Color.Black) };
                    var doneBtn = new Button { Text = "Done", Width = 100, Height = 30, Left = 10, Top = 8 };
                    var clearBtn = new Button { Text = "Clear", Width = 100, Height = 30, Left = 120, Top = 8 };
                    var cancelBtn = new Button { Text = "Cancel", Width = 100, Height = 30, Left = 230, Top = 8 };
                    panel.Controls.Add(doneBtn); panel.Controls.Add(clearBtn); panel.Controls.Add(cancelBtn);
                    form.Controls.Add(panel);
                    panel.BringToFront();

                    // Drawing state
                    bool drawing = false;
                    Point lastPoint = Point.Empty;
                    var pen = new Pen(Color.Red, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

                    // Map mouse position from picture control to image coordinates
                    Point TranslateMouseToImage(Point mouse)
                    {
                        if (picture.Image == null) return Point.Empty;
                        var img = picture.Image;
                        // pictureBox zoom math
                        var pbSize = picture.ClientSize;
                        var imgRatio = (float)img.Width / img.Height;
                        var pbRatio = (float)pbSize.Width / pbSize.Height;
                        Rectangle imgRect;
                        if (imgRatio > pbRatio)
                        {
                            // image fits by width
                            int displayW = pbSize.Width;
                            int displayH = (int)(displayW / imgRatio);
                            int offsetY = (pbSize.Height - displayH) / 2;
                            imgRect = new Rectangle(0, offsetY, displayW, displayH);
                        }
                        else
                        {
                            int displayH = pbSize.Height;
                            int displayW = (int)(displayH * imgRatio);
                            int offsetX = (pbSize.Width - displayW) / 2;
                            imgRect = new Rectangle(offsetX, 0, displayW, displayH);
                        }

                        if (!imgRect.Contains(mouse))
                            return Point.Empty;

                        float scaleX = (float)img.Width / imgRect.Width;
                        float scaleY = (float)img.Height / imgRect.Height;
                        int ix = (int)((mouse.X - imgRect.X) * scaleX);
                        int iy = (int)((mouse.Y - imgRect.Y) * scaleY);
                        return new Point(ix, iy);
                    }

                    picture.MouseDown += (s, e) =>
                    {
                        if (e.Button != MouseButtons.Left) return;
                        var p = TranslateMouseToImage(e.Location);
                        if (p == Point.Empty) return;
                        drawing = true;
                        lastPoint = p;
                    };

                    picture.MouseMove += (s, e) =>
                    {
                        if (!drawing) return;
                        var p = TranslateMouseToImage(e.Location);
                        if (p == Point.Empty) return;
                        canvasG.DrawLine(pen, lastPoint, p);
                        lastPoint = p;
                        // Compose the display image by blending screenshot + scaled canvas
                        using (var disp = new Bitmap(screenshot.Width, screenshot.Height))
                        using (var dg = Graphics.FromImage(disp))
                        {
                            dg.DrawImage(screenshot, 0, 0);
                            dg.DrawImage(canvas, 0, 0);
                            // update picture image (scaled by PictureBox)
                            var old = picture.Image;
                            picture.Image = (Image)disp.Clone();
                            old?.Dispose();
                        }
                    };

                    picture.MouseUp += (s, e) => { drawing = false; };

                    clearBtn.Click += (s, e) =>
                    {
                        canvasG.Clear(Color.Transparent);
                        // restore picture to original screenshot
                        var old = picture.Image;
                        picture.Image = (Image)screenshot.Clone();
                        old?.Dispose();
                    };

                    cancelBtn.Click += (s, e) => { picture.Image?.Dispose(); form.Close(); };

                    doneBtn.Click += (s, e) =>
                    {
                        try
                        {
                            // composite screenshot + annotations to final image
                            using (var final = new Bitmap(screenshot.Width, screenshot.Height))
                            using (var fg = Graphics.FromImage(final))
                            {
                                fg.DrawImage(screenshot, 0, 0);
                                fg.DrawImage(canvas, 0, 0);
                                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                var saveFolder = Path.Combine(documents, "Pictures", "AIsketch");
                                Directory.CreateDirectory(saveFolder);
                                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                var filePath = Path.Combine(saveFolder, $"sketch_{timestamp}.png");
                                final.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                                RhinoApp.WriteLine($"Screenshot editor saved: {filePath}");
                                // invoke Completed on Rhino UI thread
                                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => Completed?.Invoke(filePath)));
                            }
                        }
                        catch (Exception ex)
                        {
                            RhinoApp.WriteLine("Failed to save annotated screenshot: " + ex.Message);
                            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => Canceled?.Invoke()));
                        }
                        finally
                        {
                            picture.Image?.Dispose();
                            form.Close();
                        }
                    };

                    // Run modal dialog on this STA thread
                    form.ShowDialog();

                    // Cleanup
                    canvasG.Dispose();
                    canvas.Dispose();
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("ScreenSketchEditor failed: " + ex.Message);
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => Canceled?.Invoke()));
            }
            finally
            {
                _uiThread = null;
            }
        }

        public void Dispose()
        {
            // nothing special; the UI thread ends when form closes
            Instance = null;
        }
    }
}
