using System.Threading.Tasks;

namespace AIsketch.AI
{
    public class AIResponse
    {
        public string ImagePath { get; set; }
        public string Message { get; set; }
    }

    public interface IClaudeClient
    {
        /// <summary>
        /// Process an image (screenshot/overlay) asynchronously and return a structured response.
        /// </summary>
        /// <param name="imagePath">Path to the PNG image to process.</param>
        /// <param name="prompt">Optional prompt text to include with the image.</param>
        /// <returns>AIResponse with the model's response (JSON ops in Message for this POC).</returns>
        Task<AIResponse> ProcessImageAsync(string imagePath, string prompt = null);
    }
}
