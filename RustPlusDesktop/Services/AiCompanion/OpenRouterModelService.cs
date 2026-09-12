using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Represents detailed metadata and pricing for an OpenRouter model.
    /// </summary>
    public sealed class OpenRouterModelInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int ContextLength { get; set; }
        public int MaxCompletionTokens { get; set; }
        public decimal PromptPrice { get; set; }
        public decimal CompletionPrice { get; set; }
        public decimal ImagePrice { get; set; }
        public decimal CacheReadPrice { get; set; }
        public bool IsFree { get; set; }
        public bool SupportsVision { get; set; }
        public string Modality { get; set; } = "text->text";
        public string Tokenizer { get; set; } = "";
        public bool IsModerated { get; set; }
        public List<string> SupportedParameters { get; set; } = new();

        public string FormattedPromptPricePerMillion
        {
            get
            {
                if (IsFree || PromptPrice <= 0) return "$0.00";
                decimal perMillion = PromptPrice * 1_000_000m;
                return "$" + (perMillion < 0.01m ? perMillion.ToString("0.0000", CultureInfo.InvariantCulture) : perMillion.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }

        public string FormattedCompletionPricePerMillion
        {
            get
            {
                if (IsFree || CompletionPrice <= 0) return "$0.00";
                decimal perMillion = CompletionPrice * 1_000_000m;
                return "$" + (perMillion < 0.01m ? perMillion.ToString("0.0000", CultureInfo.InvariantCulture) : perMillion.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }

        public string FormattedImagePrice
        {
            get
            {
                if (ImagePrice <= 0) return "$0.00";
                return "$" + ImagePrice.ToString("0.0000", CultureInfo.InvariantCulture);
            }
        }

        public string PricingSummary
        {
            get
            {
                if (IsFree) return "Free";
                return $"Prompt: {FormattedPromptPricePerMillion}/M  ·  Output: {FormattedCompletionPricePerMillion}/M";
            }
        }

        public string ContextSummary
        {
            get
            {
                if (ContextLength <= 0) return "";
                if (ContextLength >= 1_000_000) return $"{ContextLength / 1_000_000.0:0.#}M ctx";
                if (ContextLength >= 1000) return $"{ContextLength / 1000}K ctx";
                return $"{ContextLength} ctx";
            }
        }

        public string FormattedContextLength
        {
            get
            {
                if (ContextLength <= 0) return "Unknown";
                if (ContextLength >= 1_000_000) return $"{ContextLength:N0} tokens ({ContextLength / 1_000_000.0:0.#}M)";
                if (ContextLength >= 1000) return $"{ContextLength:N0} tokens ({ContextLength / 1000}K)";
                return $"{ContextLength:N0} tokens";
            }
        }

        public string FormattedMaxOutput
        {
            get
            {
                if (MaxCompletionTokens <= 0) return "Default";
                if (MaxCompletionTokens >= 1000) return $"{MaxCompletionTokens:N0} tokens ({MaxCompletionTokens / 1000}K)";
                return $"{MaxCompletionTokens:N0} tokens";
            }
        }

        public string ProviderName
        {
            get
            {
                int slash = Id.IndexOf('/');
                if (slash > 0)
                {
                    string provider = Id.Substring(0, slash).TrimStart('~');
                    return char.ToUpperInvariant(provider[0]) + provider.Substring(1);
                }
                return "OpenRouter";
            }
        }

        public string DisplayPickerText => IsFree ? $"[Free] {Name}" : $"{Name} ({PricingSummary})";
    }

    /// <summary>
    /// Service to auto-fetch, cache, and search OpenRouter models with full metadata.
    /// </summary>
    public static class OpenRouterModelService
    {
        private const string ModelsEndpoint = "https://openrouter.ai/api/v1/models";

        private static readonly SemaphoreSlim Lock = new(1, 1);
        private static List<OpenRouterModelInfo>? _cachedModels;
        private static DateTime _lastFetchUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        /// <summary>Event raised when models have been updated / fetched in the background.</summary>
        public static event Action? ModelsUpdated;

        /// <summary>
        /// Returns all models from OpenRouter, caching the result for 30 minutes.
        /// </summary>
        public static async Task<IReadOnlyList<OpenRouterModelInfo>> GetModelsAsync(
            bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && _cachedModels != null && (DateTime.UtcNow - _lastFetchUtc) < CacheTtl)
            {
                return _cachedModels;
            }

            await Lock.WaitAsync(ct);
            try
            {
                if (!forceRefresh && _cachedModels != null && (DateTime.UtcNow - _lastFetchUtc) < CacheTtl)
                {
                    return _cachedModels;
                }

                AiLog.Info("[OpenRouter] Fetching model catalog from " + ModelsEndpoint);

                using var response = await AiHttp.Client.GetAsync(ModelsEndpoint, ct);
                if (!response.IsSuccessStatusCode)
                {
                    AiLog.Info($"[OpenRouter] Failed to fetch models: HTTP {response.StatusCode}");
                    return _cachedModels ?? FallbackModels();
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return _cachedModels ?? FallbackModels();
                }

                var list = new List<OpenRouterModelInfo>();

                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    string id = idProp.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    string name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                    string description = item.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";
                    int contextLength = item.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number && ctxProp.TryGetInt32(out var ctx) ? ctx : 0;

                    int maxCompletionTokens = 0;
                    bool isModerated = false;
                    if (item.TryGetProperty("top_provider", out var topProvider) && topProvider.ValueKind == JsonValueKind.Object)
                    {
                        if (topProvider.TryGetProperty("max_completion_tokens", out var maxTokensProp) && maxTokensProp.ValueKind == JsonValueKind.Number && maxTokensProp.TryGetInt32(out var mt))
                            maxCompletionTokens = mt;
                        if (topProvider.TryGetProperty("is_moderated", out var modProp) && modProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            isModerated = modProp.GetBoolean();
                    }

                    string modality = "text->text";
                    string tokenizer = "";
                    bool supportsVision = false;

                    if (item.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object)
                    {
                        if (arch.TryGetProperty("modality", out var modProp))
                            modality = modProp.GetString() ?? modality;
                        if (arch.TryGetProperty("tokenizer", out var tokProp))
                            tokenizer = tokProp.GetString() ?? "";

                        if (arch.TryGetProperty("input_modalities", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var input in inputs.EnumerateArray())
                            {
                                if (input.GetString() == "image")
                                    supportsVision = true;
                            }
                        }
                    }

                    if (!supportsVision && (modality.Contains("image") || modality.Contains("multimodal") || id.Contains("vision") || id.Contains("-vl")))
                    {
                        supportsVision = true;
                    }

                    decimal promptPrice = 0;
                    decimal completionPrice = 0;
                    decimal imagePrice = 0;
                    decimal cacheReadPrice = 0;

                    if (item.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
                    {
                        if (pricing.TryGetProperty("prompt", out var pProp))
                            promptPrice = ParseDecimal(pProp);
                        if (pricing.TryGetProperty("completion", out var cProp))
                            completionPrice = ParseDecimal(cProp);
                        if (pricing.TryGetProperty("image", out var imgProp))
                            imagePrice = ParseDecimal(imgProp);
                        if (pricing.TryGetProperty("input_cache_read", out var crProp))
                            cacheReadPrice = ParseDecimal(crProp);
                    }

                    var supportedParams = new List<string>();
                    if (item.TryGetProperty("supported_parameters", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in paramsProp.EnumerateArray())
                        {
                            var s = p.GetString();
                            if (!string.IsNullOrEmpty(s)) supportedParams.Add(s);
                        }
                    }

                    bool isFree = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ||
                                  (promptPrice == 0 && completionPrice == 0 && !id.StartsWith("~"));

                    list.Add(new OpenRouterModelInfo
                    {
                        Id = id,
                        Name = name,
                        Description = description,
                        ContextLength = contextLength,
                        MaxCompletionTokens = maxCompletionTokens,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        ImagePrice = imagePrice,
                        CacheReadPrice = cacheReadPrice,
                        IsFree = isFree,
                        SupportsVision = supportsVision,
                        Modality = modality,
                        Tokenizer = tokenizer,
                        IsModerated = isModerated,
                        SupportedParameters = supportedParams,
                    });
                }

                // Sort: Free models first (alphabetically), then paid models by name
                list.Sort((a, b) =>
                {
                    if (a.IsFree != b.IsFree) return a.IsFree ? -1 : 1;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                _cachedModels = list;
                _lastFetchUtc = DateTime.UtcNow;

                AiLog.Info($"[OpenRouter] Loaded {list.Count} models ({list.Count(m => m.IsFree)} free)");
                ModelsUpdated?.Invoke();

                return list;
            }
            catch (Exception ex)
            {
                AiLog.Info($"[OpenRouter] Error fetching models: {ex.Message}");
                return _cachedModels ?? FallbackModels();
            }
            finally
            {
                Lock.Release();
            }
        }

        /// <summary>
        /// Gets only free models from OpenRouter (async).
        /// </summary>
        public static async Task<IReadOnlyList<OpenRouterModelInfo>> GetFreeModelsAsync(
            bool forceRefresh = false, CancellationToken ct = default)
        {
            var all = await GetModelsAsync(forceRefresh, ct);
            return all.Where(m => m.IsFree).ToList();
        }

        /// <summary>
        /// Returns currently cached models immediately without blocking the caller or performing network calls.
        /// </summary>
        public static IReadOnlyList<OpenRouterModelInfo> GetCachedModels() =>
            _cachedModels ?? FallbackModels();

        /// <summary>
        /// Returns currently cached free models immediately without blocking.
        /// </summary>
        public static IReadOnlyList<OpenRouterModelInfo> GetCachedFreeModels() =>
            GetCachedModels().Where(m => m.IsFree).ToList();

        /// <summary>
        /// Finds a model by its exact ID or null if not found.
        /// </summary>
        public static OpenRouterModelInfo? FindModel(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || _cachedModels == null) return null;
            return _cachedModels.FirstOrDefault(m => string.Equals(m.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Filters a list of models by search query, free filter, and provider.
        /// </summary>
        public static IEnumerable<OpenRouterModelInfo> SearchModels(
            IEnumerable<OpenRouterModelInfo> models, string? query, bool? freeOnly = null, string? provider = null, bool? visionOnly = null)
        {
            var filtered = models;

            if (freeOnly.HasValue)
            {
                filtered = freeOnly.Value ? filtered.Where(m => m.IsFree) : filtered.Where(m => !m.IsFree);
            }

            if (visionOnly.HasValue && visionOnly.Value)
            {
                filtered = filtered.Where(m => m.SupportsVision);
            }

            if (!string.IsNullOrWhiteSpace(provider) && !string.Equals(provider, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(m =>
                    m.ProviderName.Contains(provider, StringComparison.OrdinalIgnoreCase) ||
                    m.Id.StartsWith(provider + "/", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return filtered;
            }

            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return filtered.Where(m =>
            {
                foreach (var term in terms)
                {
                    bool match = m.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                 m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                 m.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                 m.ProviderName.Contains(term, StringComparison.OrdinalIgnoreCase);

                    if (string.Equals(term, "free", StringComparison.OrdinalIgnoreCase) && m.IsFree)
                        match = true;

                    if (string.Equals(term, "vision", StringComparison.OrdinalIgnoreCase) && m.SupportsVision)
                        match = true;

                    if (!match) return false;
                }
                return true;
            });
        }

        private static decimal ParseDecimal(JsonElement element)
        {
            try
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var d))
                    return d;

                if (element.ValueKind == JsonValueKind.String)
                {
                    var str = element.GetString();
                    if (!string.IsNullOrWhiteSpace(str) && decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }
            catch { }
            return 0;
        }

        private static List<OpenRouterModelInfo> FallbackModels()
        {
            return new List<OpenRouterModelInfo>
            {
                new() { Id = "openai/gpt-4o-mini", Name = "OpenAI: GPT-4o-mini", ContextLength = 128000, MaxCompletionTokens = 16384, SupportsVision = true, IsFree = false, PromptPrice = 0.00000015m, CompletionPrice = 0.0000006m, Description = "Fast, lightweight model with vision support for everyday questions." },
                new() { Id = "openai/gpt-4o", Name = "OpenAI: GPT-4o", ContextLength = 128000, MaxCompletionTokens = 16384, SupportsVision = true, IsFree = false, PromptPrice = 0.0000025m, CompletionPrice = 0.00001m, Description = "Flagship OpenAI model with superior reasoning and vision understanding." },
                new() { Id = "anthropic/claude-3.5-sonnet", Name = "Anthropic: Claude 3.5 Sonnet", ContextLength = 200000, MaxCompletionTokens = 8192, SupportsVision = true, IsFree = false, PromptPrice = 0.000003m, CompletionPrice = 0.000015m, Description = "Anthropic's smartest model, excellent at reasoning, coding, and game analysis." },
                new() { Id = "google/gemini-2.5-flash", Name = "Google: Gemini 2.5 Flash", ContextLength = 1000000, MaxCompletionTokens = 8192, SupportsVision = true, IsFree = false, PromptPrice = 0.000000075m, CompletionPrice = 0.0000003m, Description = "Ultra-fast model with massive 1M context window and low latency." },
                new() { Id = "deepseek/deepseek-chat", Name = "DeepSeek: DeepSeek V3", ContextLength = 64000, MaxCompletionTokens = 8192, SupportsVision = false, IsFree = false, PromptPrice = 0.00000014m, CompletionPrice = 0.00000028m, Description = "High capability mixture-of-experts model with incredible cost efficiency." },
                new() { Id = "meta-llama/llama-3.3-70b-instruct:free", Name = "Meta: Llama 3.3 70B Instruct (free)", ContextLength = 131072, MaxCompletionTokens = 4096, SupportsVision = false, IsFree = true, PromptPrice = 0, CompletionPrice = 0, Description = "Meta's flagship open weights 70B model hosted free on OpenRouter." },
                new() { Id = "deepseek/deepseek-r1:free", Name = "DeepSeek: R1 (free)", ContextLength = 64000, MaxCompletionTokens = 8192, SupportsVision = false, IsFree = true, PromptPrice = 0, CompletionPrice = 0, Description = "DeepSeek's advanced reasoning model with chain-of-thought, free on OpenRouter." },
                new() { Id = "google/gemini-2.0-flash-exp:free", Name = "Google: Gemini 2.0 Flash (free)", ContextLength = 1048576, MaxCompletionTokens = 8192, SupportsVision = true, IsFree = true, PromptPrice = 0, CompletionPrice = 0, Description = "Experimental next-gen Gemini Flash with 1M context window and image support." },
            };
        }
    }
}
