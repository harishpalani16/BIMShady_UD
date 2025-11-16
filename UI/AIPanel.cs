using System;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using AIsketch.UI;
using AIsketch.AI;
using AIsketch.Processor;
using System.Threading.Tasks;

namespace AIsketch.UI
{
    public class AIPanel : Form
    {
        // Controls
        private readonly TextArea _chatHistory;
        private readonly TextBox _inputBox;
        private readonly Button _sendButton;
        private readonly Button _startSketchButton;
        private readonly Button _closeButton;

        // Make the panel accessible to other parts of the plugin for POC convenience
        public static AIPanel Instance { get; private set; }

        public AIPanel()
        {
            Instance = this;

            Title = "AI Sketch - Chat";
            ClientSize = new Size(480, 360);
            Resizable = true;

            // Chat history - read-only multiline box
            _chatHistory = new TextArea
            {
                ReadOnly = true,
                Wrap = true,
                Text = "Welcome to AI Sketch POC.\n\nUse the input box below to send a prompt to the mock AI.\nClick 'Start Sketch' to begin sketch capture.",
                Height = 220
            };

            // Input box and send button
            _inputBox = new TextBox { PlaceholderText = "Type your prompt here..." };
            _sendButton = new Button { Text = "Send" };
            _sendButton.Click += SendButton_Click;

            // Start Sketch button - triggers the capture command
            _startSketchButton = new Button { Text = "Start Sketch" };
            _startSketchButton.Click += StartSketchButton_Click;

            // Close
            _closeButton = new Button { Text = "Close" };
            _closeButton.Click += (s, e) => Close();

            // Layout - simple and clear structure
            var inputLayout = new TableLayout
            {
                Spacing = Size.Empty,
                Padding = Padding.Empty,
                Rows =
                {
                    new TableRow(
                        new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 5,
                            Items =
                            {
                                new TableLayout(
                                    new TableRow(new TableCell(_inputBox, true), new TableCell(_sendButton))),
                            }
                        }
                    )
                }
            };

            var buttonsLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { _startSketchButton, _closeButton }
            };

            // Main layout
            Content = new TableLayout
            {
                Padding = new Padding(10),
                Spacing = new Size(5, 5),
                Rows =
                {
                    new TableRow(new TableCell(_chatHistory, true)),
                    new TableRow(new TableCell(inputLayout)),
                    new TableRow(new TableCell(buttonsLayout)),
                    new TableRow(new TableCell(null, true))
                }
            };
        }

        /// <summary>
        /// Appends a line to the chat history area and scrolls to the end.
        /// </summary>
        /// <param name="text">Text to append.</param>
        public void AppendChat(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (!string.IsNullOrEmpty(_chatHistory.Text))
                _chatHistory.Text += "\n\n" + text;
            else
                _chatHistory.Text = text;

            // There is no direct scroll-to-end API on Eto.TextArea; a simple trick is to set the caret position.
            try
            {
                _chatHistory.CaretIndex = _chatHistory.Text.Length;
            }
            catch
            {
                // ignore if not supported on some platforms
            }
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            var prompt = _inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(prompt))
                return;

            // Echo in the chat window (local POC behaviour)
            AppendChat("You: " + prompt);

            // Mock AI reply - in a real implementation, this would call the AI client asynchronously
            AppendChat("AI: (mock) I received your prompt and will suggest edits.");

            _inputBox.Text = string.Empty;
        }

        private void StartSketchButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Dynamically load the overlay type (Windows-only) so multi-target builds succeed when overlay is excluded
                var asm = Assembly.GetExecutingAssembly();
                var overlayType = asm.GetType("AIsketch.UI.ScreenSketchOverlay");
                if (overlayType == null)
                {
                    AppendChat("System: Overlay not available on this build/OS.");
                    return;
                }

                var overlay = Activator.CreateInstance(overlayType);

                // Wire events
                var completedEvent = overlayType.GetEvent("Completed");
                if (completedEvent != null)
                {
                    completedEvent.AddEventHandler(overlay, new Action<string>(async path =>
                    {
                        AppendChat("System: Overlay completed. Saved to " + path);

                        AIResponse response = null;
                        try
                        {
                            var claude = new ClaudeMcpClient();
                            response = await claude.ProcessImageAsync(path, _inputBox.Text);
                        }
                        catch (Exception ex)
                        {
                            AppendChat("Claude client failed: " + ex.Message + " — falling back to mock.");
                            var mock = new MockClaudeClient();
                            response = await mock.ProcessImageAsync(path, _inputBox.Text);
                        }

                        // Show approval dialog on Rhino UI thread
                        Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                        {
                            var dialog = new AIApprovalDialog(response.Message);
                            dialog.ShowModal();
                        }));

                        // Dispose overlay
                        var disposeMethod = overlayType.GetMethod("Dispose");
                        disposeMethod?.Invoke(overlay, null);
                    }));
                }

                var canceledEvent = overlayType.GetEvent("Canceled");
                if (canceledEvent != null)
                {
                    canceledEvent.AddEventHandler(overlay, new Action(() =>
                    {
                        AppendChat("System: Overlay canceled.");
                        var disposeMethod = overlayType.GetMethod("Dispose");
                        disposeMethod?.Invoke(overlay, null);
                    }));
                }

                // Show overlay
                var showMethod = overlayType.GetMethod("ShowOverlay");
                showMethod?.Invoke(overlay, null);
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine("Failed to start sketch overlay: {0}", ex.Message);
                AppendChat("System: Failed to start sketch overlay.");
            }
        }
    }
}
