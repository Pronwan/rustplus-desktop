using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesktop.Tests
{
    [TestClass]
    public class OpenRouterModelServiceTests
    {
        [TestMethod]
        public async Task GetModelsAsync_ReturnsModelsWithPricingAndMetadata()
        {
            var models = await OpenRouterModelService.GetModelsAsync();

            Assert.IsNotNull(models);
            Assert.IsTrue(models.Count > 0, "Models list should not be empty");

            // Verify free models exist in the list
            var freeModels = models.Where(m => m.IsFree).ToList();
            Assert.IsTrue(freeModels.Count > 0, "There should be free models identified");

            foreach (var free in freeModels)
            {
                Assert.IsTrue(free.IsFree);
                Assert.AreEqual("$0.00", free.FormattedPromptPricePerMillion);
                Assert.AreEqual("$0.00", free.FormattedCompletionPricePerMillion);
            }
        }

        [TestMethod]
        public void SearchModels_ByQueryAndFreeFilter_FiltersCorrectly()
        {
            var testModels = new OpenRouterModelInfo[]
            {
                new() { Id = "openai/gpt-4o", Name = "OpenAI: GPT-4o", IsFree = false, ContextLength = 128000, SupportsVision = true, PromptPrice = 0.0000025m, CompletionPrice = 0.00001m },
                new() { Id = "anthropic/claude-3.5-sonnet", Name = "Anthropic: Claude 3.5 Sonnet", IsFree = false, ContextLength = 200000, SupportsVision = true, PromptPrice = 0.000003m, CompletionPrice = 0.000015m },
                new() { Id = "meta-llama/llama-3.3-70b-instruct:free", Name = "Meta: Llama 3.3 70B Instruct (free)", IsFree = true, ContextLength = 131072, SupportsVision = false, PromptPrice = 0, CompletionPrice = 0 },
                new() { Id = "deepseek/deepseek-r1:free", Name = "DeepSeek: R1 (free)", IsFree = true, ContextLength = 64000, SupportsVision = false, PromptPrice = 0, CompletionPrice = 0 },
                new() { Id = "google/gemini-2.0-flash-exp:free", Name = "Google: Gemini 2.0 Flash (free)", IsFree = true, ContextLength = 1048576, SupportsVision = true, PromptPrice = 0, CompletionPrice = 0 },
            };

            // Search "free" with freeOnly = true
            var freeOnly = OpenRouterModelService.SearchModels(testModels, null, freeOnly: true).ToList();
            Assert.AreEqual(3, freeOnly.Count);
            Assert.IsTrue(freeOnly.All(m => m.IsFree));

            // Search "claude"
            var claudeResults = OpenRouterModelService.SearchModels(testModels, "claude").ToList();
            Assert.AreEqual(1, claudeResults.Count);
            Assert.AreEqual("anthropic/claude-3.5-sonnet", claudeResults[0].Id);

            // Search with provider filter
            var googleResults = OpenRouterModelService.SearchModels(testModels, null, provider: "google").ToList();
            Assert.AreEqual(1, googleResults.Count);
            Assert.AreEqual("google/gemini-2.0-flash-exp:free", googleResults[0].Id);

            // Search with vision filter
            var visionResults = OpenRouterModelService.SearchModels(testModels, null, visionOnly: true).ToList();
            Assert.AreEqual(3, visionResults.Count);
            Assert.IsTrue(visionResults.All(m => m.SupportsVision));
        }

        [TestMethod]
        public void ModelInfo_FormattedContextLength_FormatsAppropriately()
        {
            var model1 = new OpenRouterModelInfo { ContextLength = 128000 };
            Assert.IsTrue(model1.FormattedContextLength.Contains("128K") || model1.FormattedContextLength.Contains("128,000"));

            var model2 = new OpenRouterModelInfo { ContextLength = 1048576 };
            Assert.IsTrue(model2.FormattedContextLength.Contains("1M") || model2.FormattedContextLength.Contains("1,048,576"));
        }
    }
}
