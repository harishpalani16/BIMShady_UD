using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using AIsketch.UI;

namespace AIsketch.Commands
{
    /// <summary>
    /// Captures a freehand sketch overlay in the active Rhino viewport. The sketch is a visual overlay
    /// and does not interact with Rhino geometry. When finished, the command captures the viewport to a bitmap
    /// and stores it in a temporary PNG file. The file path is reported back to the AIPanel (if open).
    ///
    /// This is intentionally simple: the "sketch" is collected by sampling the mouse position during DynamicDraw
    /// while the GetPoint command is active. The user can move the mouse to sketch; pressing Enter finishes the sketch.
    /// </summary>
    public class AIsketchCaptureCommand : Command
    {
        public AIsketchCaptureCommand()
        {
            Instance = this;
        }

        public static AIsketchCaptureCommand Instance { get; private set; }

        public override string EnglishName => "AIsketchCapture";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var stroke = new List<Point3d>();
            Point3d lastAdded = Point3d.Unset;
            const double sampleDistance = 0.5; // model units - adjust as needed

            RhinoApp.WriteLine("AIsketch: Move the mouse to sketch, press Enter to finish, Esc to cancel.");

            // Use repeated GetPoint sessions so DynamicDraw continues to fire while user interacts.
            while (true)
            {
                using (var gp = new GetPoint())
                {
                    gp.SetCommandPrompt("Sketch: move mouse to draw; press Enter to finish");
                    gp.AcceptNothing(true);

                    gp.DynamicDraw += (sender, e) =>
                    {
                        var current = e.CurrentPoint;
                        if (!lastAdded.IsValid)
                        {
                            // first sample
                            stroke.Add(current);
                            lastAdded = current;
                        }
                        else if (current.DistanceTo(lastAdded) >= sampleDistance)
                        {
                            stroke.Add(current);
                            lastAdded = current;
                        }

                        // draw the current stroke preview
                        if (stroke.Count >= 2)
                        {
                            e.Display.DrawPolyline(stroke.ToArray(), System.Drawing.Color.Red);
                        }
                    };

                    var res = gp.Get();
                    if (res == GetResult.Nothing)
                    {
                        // user pressed Enter to finish
                        break;
                    }
                    else if (res == GetResult.Cancel)
                    {
                        RhinoApp.WriteLine("Sketch cancelled.");
                        return gp.CommandResult();
                    }
                    else if (res == GetResult.Point)
                    {
                        // user clicked - also record the clicked point (if far enough)
                        var p = gp.Point();
                        if (!lastAdded.IsValid || p.DistanceTo(lastAdded) >= sampleDistance)
                        {
                            stroke.Add(p);
                            lastAdded = p;
                        }

                        // continue loop to allow more sketching
                    }
                }
            }

            if (stroke.Count < 2)
            {
                RhinoApp.WriteLine("Not enough points collected.");
                return Result.Success;
            }

            // Draw the polyline into the active viewport temporarily and capture the screen
            var view = doc.Views.ActiveView;
            if (view == null)
            {
                RhinoApp.WriteLine("No active view to capture.");
                return Result.Failure;
            }

            // Capture the viewport image
            var bitmap = view.CaptureToBitmap();
            if (bitmap == null)
            {
                RhinoApp.WriteLine("Failed to capture viewport bitmap.");
                return Result.Failure;
            }

            // Draw the polyline on the bitmap
            try
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    if (stroke.Count >= 2)
                    {
                        var pts = new System.Drawing.Point[stroke.Count];
                        for (int i = 0; i < stroke.Count; i++)
                        {
                            var sp = view.ActiveViewport.WorldToClient(stroke[i]);
                            pts[i] = new System.Drawing.Point((int)sp.X, (int)sp.Y);
                        }

                        using (var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 2))
                        {
                            g.DrawLines(pen, pts);
                        }
                    }
                }

                // Save to temp file
                var tempPath = Path.GetTempFileName() + ".png";
                bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

                RhinoApp.WriteLine("Sketch saved to: " + tempPath);

                // Notify the panel if it's open
                var panel = AIPanel.Instance;
                if (panel != null)
                {
                    panel.AppendChat("System: Sketch captured and saved to " + tempPath);
                }
                else
                {
                    RhinoApp.WriteLine("AIPanel not open to receive sketch path.");
                }

                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Failed during bitmap overlay: " + ex.Message);
                return Result.Failure;
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }
}
