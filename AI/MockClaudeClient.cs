using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIsketch.AI
{
    public class MockClaudeClient : IClaudeClient
    {
        public Task<AIResponse> ProcessImageAsync(string imagePath, string prompt = null)
        {
            var ops = new[]
            {
                new
                {
                    op = "add_line",
                    start = new double[] { 0.0, 0.0, 0.0 },
                    end = new double[] { 10.0, 0.0, 0.0 }
                }
            };

            var json = JsonSerializer.Serialize(ops);

            var resp = new AIResponse
            {
                ImagePath = imagePath,
                Message = json
            };

            return Task.FromResult(resp);
        }
    }
}
