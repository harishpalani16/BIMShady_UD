using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIsketch.AI;
using AIsketch.Services;

namespace AIsketch.AI
{
    public class ClaudeMcpClient : IClaudeClient
    {
        private readonly string _apiKey;
        private readonly string _apiUrl;

        public ClaudeMcpClient()
        {
            _apiKey = SettingsService.GetApiKey();
            _apiUrl = SettingsService.GetApiUrl();
        }

        public Task<AIResponse> ProcessImageAsync(string imagePath, string prompt = null)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiUrl))
            {
                // Return a failed task so callers can catch and fallback to mock
                return Task.FromException<AIResponse>(new InvalidOperationException("Claude MCP not configured (missing API url or key)."));
            }

            if (!File.Exists(imagePath))
                return Task.FromResult(new AIResponse { ImagePath = imagePath, Message = "Image file not found." });

            // Use Task.Run to perform blocking I/O off the calling thread for POC
            return Task.Run(() =>
            {
                try
                {
                    var bytes = File.ReadAllBytes(imagePath);
                    var base64 = Convert.ToBase64String(bytes);
                    
                    // Determine media type based on file extension
                    var extension = Path.GetExtension(imagePath).ToLowerInvariant();
                    var mediaType = extension == ".png" ? "image/png" : 
                                    extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : 
                                    extension == ".gif" ? "image/gif" : 
                                    extension == ".webp" ? "image/webp" : "image/png";

                    // Build the Anthropic API request payload according to their specification
                    var payloadObj = new
                    {
                        model = "claude-sonnet-4-5-20250929", // or use "claude-3-opus-20240229" or other model
                        max_tokens = 1024,
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new
                                    {
                                        type = "image",
                                        source = new
                                        {
                                            type = "base64",
                                            media_type = mediaType,
                                            data = base64
                                        }
                                    },
                                    new
                                    {
                                        type = "text",
                                        text = string.IsNullOrEmpty(prompt) ? "What's in this image?" : prompt
                                    }
                                }
                            }
                        }
                    };

                    var payloadJson = JsonSerializer.Serialize(payloadObj);

                    using (var client = new HttpClient())
                    {
                        // Some MCP endpoints (eg. Anthropic) expect the API key in the 'x-api-key' header.
                        // Add both headers for compatibility: x-api-key plus Authorization: Bearer (if supported).
                        client.DefaultRequestHeaders.Remove("x-api-key");
                        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                        
                        // Add required anthropic-version header
                        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                        client.Timeout = TimeSpan.FromSeconds(60);

                        var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                        var resp = client.PostAsync(_apiUrl, content).Result;
                        var body = resp.Content.ReadAsStringAsync().Result;
                        if (resp.IsSuccessStatusCode)
                        {
                            // Parse the response to extract the actual message text
                            try
                            {
                                using (var doc = JsonDocument.Parse(body))
                                {
                                    var root = doc.RootElement;
                                    if (root.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                                    {
                                        var firstContent = contentArray[0];
                                        if (firstContent.TryGetProperty("text", out var textProp))
                                        {
                                            return new AIResponse { ImagePath = imagePath, Message = textProp.GetString() };
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // If parsing fails, return the raw body
                            }
                            
                            return new AIResponse { ImagePath = imagePath, Message = body };
                        }
                        else
                        {
                            return new AIResponse { ImagePath = imagePath, Message = $"Request failed: {resp.StatusCode} - {body}" };
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new AIResponse { ImagePath = imagePath, Message = "Exception: " + ex.Message };
                }
            });
        }
    }
}
