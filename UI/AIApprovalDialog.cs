using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using AIsketch.Processor;
using AIsketch.UI;

namespace AIsketch.UI
{
    /// <summary>
    /// Simple approval dialog that displays AI response (structured ops JSON + message) and lets the user Preview/Apply/Reject.
    /// When Apply is pressed, the dialog validates and applies the ops using AICommandProcessor on the Rhino main thread.
    /// </summary>
    public class AIApprovalDialog : Dialog
    {
        private readonly TextArea _responseArea;
        private readonly Button _applyButton;
        private readonly Button _rejectButton;

        private readonly string _responseJson;

        public AIApprovalDialog(string responseJson)
        {
            Title = "AI Response Approval";
            ClientSize = new Size(600, 420);
            Resizable = true;

            _responseJson = responseJson ?? string.Empty;

            _responseArea = new TextArea
            {
                ReadOnly = true,
                Wrap = true,
                Text = _responseJson
            };

            _applyButton = new Button { Text = "Apply" };
            _rejectButton = new Button { Text = "Reject" };

            _applyButton.Click += ApplyButton_Click;
            _rejectButton.Click += (s, e) => Close();

            var btnLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { _applyButton, _rejectButton }
            };

            Content = new TableLayout
            {
                Padding = 10,
                Spacing = new Size(5, 5),
                Rows =
                {
                    new TableRow(new TableCell(_responseArea, true)),
                    new TableRow(new TableCell(btnLayout))
                }
            };
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            try
            {
                // The dialog is shown on the Rhino UI thread; apply directly.
                var doc = Rhino.RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    Rhino.RhinoApp.WriteLine("AIApproval: No active document.");
                    MessageBox.Show(this, "No active Rhino document.", MessageBoxType.Warning);
                    return;
                }

                int applied = AICommandProcessor.ApplyResponse(doc, _responseJson);
                Rhino.RhinoApp.WriteLine("AIApproval: Applied AI operations. Count=" + applied);
                MessageBox.Show(this, "AI operations applied: " + applied, MessageBoxType.Information);

                // Report back to AIPanel chat if available
                try
                {
                    AIPanel.Instance?.AppendChat("System: Applied " + applied + " AI operations to the document.");
                }
                catch { }
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine("AIApproval: Failed to apply ops: " + ex.Message);
                MessageBox.Show(this, "Failed to apply AI operations: " + ex.Message, MessageBoxType.Error);
            }
            finally
            {
                Close();
            }
        }
    }
}
