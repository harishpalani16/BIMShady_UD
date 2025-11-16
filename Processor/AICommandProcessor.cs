using System;
using System.Collections.Generic;
using System.Text.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace AIsketch.Processor
{
    /// <summary>
    /// Simple processor that parses a structured AI response (JSON) and applies supported operations to the Rhino document.
    /// Supported ops (POC):
    /// - add_line: { op: "add_line", start: [x,y,z], end: [x,y,z] }
    ///
    /// This class validates inputs and applies all changes inside a single undo record.
    /// </summary>
    public static class AICommandProcessor
    {
        public static int ApplyResponse(RhinoDoc doc, string json)
        {
            if (doc == null)
            {
                RhinoApp.WriteLine("AICommandProcessor: no active document.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                RhinoApp.WriteLine("AICommandProcessor: empty AI response.");
                return 0;
            }

            List<JsonElement> ops = new List<JsonElement>();
            try
            {
                using (JsonDocument docJson = JsonDocument.Parse(json))
                {
                    if (docJson.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in docJson.RootElement.EnumerateArray()) ops.Add(el);
                    }
                    else if (docJson.RootElement.ValueKind == JsonValueKind.Object && docJson.RootElement.TryGetProperty("ops", out var opsEl) && opsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in opsEl.EnumerateArray()) ops.Add(el);
                    }
                    else
                    {
                        RhinoApp.WriteLine("AICommandProcessor: unexpected JSON format - expected array or { ops: [...] }.");
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("AICommandProcessor: failed to parse JSON: " + ex.Message);
                return 0;
            }

            if (ops.Count == 0)
            {
                RhinoApp.WriteLine("AICommandProcessor: no ops found in AI response.");
                return 0;
            }

            uint undo = doc.BeginUndoRecord("AI: apply ops");
            int applied = 0;
            try
            {
                foreach (var op in ops)
                {
                    if (!op.TryGetProperty("op", out var opNameEl))
                    {
                        RhinoApp.WriteLine("Skipping op with no 'op' property.");
                        continue;
                    }

                    var opName = opNameEl.GetString();
                    if (string.Equals(opName, "add_line", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadPoint(op, "start", out var start) && TryReadPoint(op, "end", out var end))
                        {
                            var id = doc.Objects.AddLine(start, end);
                            if (id != Guid.Empty)
                            {
                                applied++;
                                RhinoApp.WriteLine($"AI: added line from {start} to {end}");
                            }
                        }
                        else
                        {
                            RhinoApp.WriteLine("AI: add_line missing start or end point.");
                        }
                    }
                    else if (string.Equals(opName, "delete_object", StringComparison.OrdinalIgnoreCase))
                    {
                        // POC: identify by GUID string id
                        if (op.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var guid))
                        {
                            var obj = doc.Objects.Find(guid);
                            if (obj != null)
                            {
                                if (doc.Objects.Delete(guid, true))
                                {
                                    applied++;
                                    RhinoApp.WriteLine($"AI: deleted object {guid}");
                                }
                            }
                            else
                            {
                                RhinoApp.WriteLine($"AI: object {guid} not found.");
                            }
                        }
                    }
                    else
                    {
                        RhinoApp.WriteLine($"AICommandProcessor: unsupported op '{opName}' (ignored).");
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("AICommandProcessor: exception while applying ops: " + ex.Message);
            }
            finally
            {
                doc.EndUndoRecord(undo);
                doc.Views.Redraw();
            }

            RhinoApp.WriteLine($"AICommandProcessor: applied {applied} ops.");
            return applied;
        }

        private static bool TryReadPoint(JsonElement op, string propName, out Point3d pt)
        {
            pt = Point3d.Unset;
            if (!op.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return false;

            var list = new List<double>();
            foreach (var v in arr.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)) list.Add(d);
            }
            if (list.Count >= 3)
            {
                pt = new Point3d(list[0], list[1], list[2]);
                return true;
            }
            return false;
        }
    }
}
