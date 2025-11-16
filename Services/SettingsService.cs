using System;

namespace AIsketch.Services
{
    public static class SettingsService
    {
        // For POC store in environment variables. In production use secure storage.
        // This helper will try multiple common variable names (underscore and space variants) to be more forgiving.
        private static string GetEnv(params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var v = Environment.GetEnvironmentVariable(name);
                    if (!string.IsNullOrWhiteSpace(v))
                        return v.Trim();
                }
                catch { }
            }
            return null;
        }

        public static string GetApiKey()
        {
            // Try common names: prefer CLAUDE_MCP_KEY, then CLAUDE_MCP_URL-style, then older CLAUDE_API_KEY
            var key = GetEnv("CLAUDE_MCP_KEY", "CLAUDE MCP KEY", "CLAUDE_API_KEY", "CLAUDE API KEY");

            // Do not return a hard-coded key. If the environment variable is not set, return null so
            // callers can fallback to the mock client. Developers can set CLAUDE_MCP_KEY in their
            // environment for local testing. Do NOT commit real API keys to source control.
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return key;
        }

        public static string GetApiUrl()
        {
            // Try several variants for the URL env var
            var url = GetEnv("CLAUDE_MCP_URL", "CLAUDE MCP URL", "CLAUDE_API_URL", "CLAUDE API URL");
            if (string.IsNullOrWhiteSpace(url))
            {
                // Provide a sensible default endpoint for local testing.
                return "https://api.anthropic.com/v1/messages";
            }
            return url;
        }
    }
}
