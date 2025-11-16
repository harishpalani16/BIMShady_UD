using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using AIsketch.UI;

namespace AIsketch
{
    public class AIsketchCommand : Command
    {
        public AIsketchCommand()
        {
            // Rhino only creates one instance of each command class defined in a
            // plug-in, so it is safe to store a refence in a static property.
            Instance = this;
        }

        ///<summary>The only instance of this command.</summary>
        public static AIsketchCommand Instance { get; private set; }

        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName => "AIsketchCommand";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Instead of performing a direct geometry operation, this POC opens
            // a small dockable/dialog window that contains a chat area and
            // buttons for sketching. The sketch capture itself will be executed
            // from Rhino commands; the UI only triggers the request.

            try
            {
                var panel = new AIPanel();
                // Show the panel modelessly so the user can interact with Rhino.
                panel.Show();
                RhinoApp.WriteLine("AIPanel opened.");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Failed to open AI Panel: {0}", ex.Message);
                return Result.Failure;
            }
        }
    }
}
