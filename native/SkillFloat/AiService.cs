using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SkillFloat
{
    internal sealed class AiService
    {
        private static readonly string[] Categories = { "开发与代码", "文档与内容", "设计与多媒体", "数据与自动化", "法律与专业", "沟通与协作", "其他" };
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public bool IsConfigured
        {
            get
            {
                var settings = Storage.LoadSettings();
                return !string.IsNullOrWhiteSpace(settings.model) && !string.IsNullOrWhiteSpace(Storage.LoadApiKey());
            }
        }

        public async Task<TranslationSuggestion> RecommendAsync(SkillItem skill, CancellationToken token)
        {
            var result = await RequestAsync(skill, true, token).ConfigureAwait(false);
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts == null) drafts.drafts = new Dictionary<string, TranslationDraft>(StringComparer.OrdinalIgnoreCase);
            drafts.drafts[skill.Invocation] = new TranslationDraft
            {
                invocation = skill.Invocation,
                suggestion = result,
                generatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Storage.SaveDrafts(drafts);
            return result;
        }

        public Task<TranslationSuggestion> ClassifyAsync(SkillItem skill, CancellationToken token) => RequestAsync(skill, false, token);

        public async Task<Dictionary<string, TranslationSuggestion>> ClassifyBatchAsync(IEnumerable<SkillItem> source, CancellationToken token)
        {
            var skills = source.Take(20).ToList();
            var results = new Dictionary<string, TranslationSuggestion>(StringComparer.OrdinalIgnoreCase);
            if (skills.Count == 0) return results;
            var settings = Storage.LoadSettings();
            var key = Storage.LoadApiKey();
            if (string.IsNullOrWhiteSpace(settings.model) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未配置 AI 接口、模型或 API 密钥");
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.model,
                ["temperature"] = 0.1,
                ["messages"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["role"] = "system",
                        ["content"] = "你是软件 Skill 分类助手。只返回严格 JSON：{\"items\":[{\"invocation\":\"原调用名\",\"category\":\"分类\",\"tags\":[\"标签1\",\"标签2\"]}]}。必须逐项保留输入 invocation；分类只能从开发与代码、文档与内容、设计与多媒体、数据与自动化、法律与专业、沟通与协作、其他中选择；每项给2-4个简短标签；不要输出解释。"
                    },
                    new Dictionary<string, object>
                    {
                        ["role"] = "user",
                        ["content"] = Json.Serialize(skills.Select(skill => new Dictionary<string, object>
                        {
                            ["invocation"] = Limit(skill.Invocation, 160),
                            ["name"] = Limit(skill.Name, 160),
                            ["description"] = Limit(skill.Description, 900)
                        }).ToArray())
                    }
                }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings.endpoint)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Content = new StringContent(Json.Serialize(payload), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("AI 接口返回 " + (int)response.StatusCode);
                    var root = Json.DeserializeObject(body) as Dictionary<string, object>;
                    var parsed = ExtractJson(ExtractContent(root));
                    object itemsObject;
                    if (!parsed.TryGetValue("items", out itemsObject)) throw new InvalidOperationException("AI 未返回批量分类结果");
                    foreach (var itemObject in AsObjects(itemsObject))
                    {
                        var item = itemObject as Dictionary<string, object>;
                        var invocation = Clean(GetString(item, "invocation"), 180);
                        if (invocation.Length == 0 || !skills.Any(skill => skill.Invocation.Equals(invocation, StringComparison.OrdinalIgnoreCase))) continue;
                        var suggestion = new TranslationSuggestion
                        {
                            category = NormalizeCategory(GetString(item, "category")),
                            tags = NormalizeTags(GetArray(item, "tags")),
                            engine = "ai"
                        };
                        if (suggestion.tags.Count == 0) suggestion.tags.Add("通用");
                        results[invocation] = suggestion;
                    }
                }
            }
            if (results.Count == 0) throw new InvalidOperationException("AI 未返回可用的批量分类结果");
            return results;
        }

        private static async Task<TranslationSuggestion> RequestAsync(SkillItem skill, bool includeTranslation, CancellationToken token)
        {
            var settings = Storage.LoadSettings();
            var key = Storage.LoadApiKey();
            if (string.IsNullOrWhiteSpace(settings.model) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未配置 AI 接口、模型或 API 密钥");
            var endpoint = BuildEndpoint(settings.endpoint);
            var system = includeTranslation
                ? "你是软件能力名称本地化与分类助手。只返回严格 JSON：{\"short_name\":\"中文简称\",\"description_zh\":\"简洁中文用途\",\"category\":\"分类\",\"tags\":[\"标签1\",\"标签2\"]}。分类只能从开发与代码、文档与内容、设计与多媒体、数据与自动化、法律与专业、沟通与协作、其他中选择；标签给2-4个简短词；不要增加原始说明没有的能力。"
                : "你是软件 Skill 分类助手。只返回严格 JSON：{\"category\":\"分类\",\"tags\":[\"标签1\",\"标签2\"]}。分类只能从开发与代码、文档与内容、设计与多媒体、数据与自动化、法律与专业、沟通与协作、其他中选择；标签给2-4个简短词；不要输出解释。";
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.model,
                ["temperature"] = 0.15,
                ["messages"] = new object[]
                {
                    new Dictionary<string, object> { ["role"] = "system", ["content"] = system },
                    new Dictionary<string, object> { ["role"] = "user", ["content"] = "调用名：" + Limit(skill.Invocation, 160) + "\n原始名称：" + Limit(skill.Name, 160) + "\n原始说明：" + Limit(skill.Description, 1600) }
                }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Content = new StringContent(Json.Serialize(payload), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("AI 接口返回 " + (int)response.StatusCode);
                    var root = Json.DeserializeObject(body) as Dictionary<string, object>;
                    var content = ExtractContent(root);
                    var parsed = ExtractJson(content);
                    var suggestion = new TranslationSuggestion
                    {
                        shortName = includeTranslation ? Clean(GetString(parsed, "short_name"), 24) : skill.DisplayName,
                        descriptionZh = includeTranslation ? Clean(GetString(parsed, "description_zh"), 300) : skill.LocalizedDescription,
                        category = NormalizeCategory(GetString(parsed, "category")),
                        tags = NormalizeTags(GetArray(parsed, "tags")),
                        engine = "ai"
                    };
                    if (suggestion.tags.Count == 0) suggestion.tags.Add("通用");
                    if (includeTranslation && (suggestion.shortName.Length == 0 || suggestion.descriptionZh.Length == 0)) throw new InvalidOperationException("AI 未返回有效的中文推荐");
                    return suggestion;
                }
            }
        }

        private static Uri BuildEndpoint(string value)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out uri)) throw new InvalidOperationException("接口地址格式无效");
            var local = uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1";
            if (uri.Scheme != Uri.UriSchemeHttps && !(local && uri.Scheme == Uri.UriSchemeHttp)) throw new InvalidOperationException("接口地址必须使用 HTTPS；本机接口可使用 HTTP");
            var builder = new UriBuilder(uri);
            if (!builder.Path.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) builder.Path = builder.Path.TrimEnd('/') + "/chat/completions";
            return builder.Uri;
        }

        private static string ExtractContent(Dictionary<string, object> root)
        {
            if (root == null) throw new InvalidOperationException("AI 响应格式无效");
            object choicesObject;
            if (!root.TryGetValue("choices", out choicesObject)) throw new InvalidOperationException("AI 响应缺少推荐内容");
            var choices = AsObjects(choicesObject).ToList();
            if (choices.Count == 0) throw new InvalidOperationException("AI 响应缺少推荐内容");
            var choice = choices[0] as Dictionary<string, object>;
            object messageObject;
            if (choice == null || !choice.TryGetValue("message", out messageObject)) throw new InvalidOperationException("AI 响应缺少推荐内容");
            var message = messageObject as Dictionary<string, object>;
            return message == null ? "" : GetString(message, "content");
        }

        private static Dictionary<string, object> ExtractJson(string content)
        {
            var start = (content ?? "").IndexOf('{');
            var end = (content ?? "").LastIndexOf('}');
            if (start < 0 || end < start) throw new InvalidOperationException("AI 返回内容不含有效 JSON");
            return Json.DeserializeObject(content.Substring(start, end - start + 1)) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static string GetString(Dictionary<string, object> value, string key)
        {
            object result;
            return value != null && value.TryGetValue(key, out result) && result != null ? Convert.ToString(result) : "";
        }

        private static IEnumerable<string> GetArray(Dictionary<string, object> value, string key)
        {
            object result;
            if (value == null || !value.TryGetValue(key, out result) || result == null) return Enumerable.Empty<string>();
            return AsObjects(result).Select(Convert.ToString);
        }

        private static IEnumerable<object> AsObjects(object value)
        {
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) return Enumerable.Empty<object>();
            return enumerable.Cast<object>();
        }

        private static string NormalizeCategory(string value) => Categories.Contains(value) ? value : "其他";
        private static List<string> NormalizeTags(IEnumerable<string> values) => values.Select(value => Clean(value, 12)).Where(value => value.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(4).ToList();
        private static string Clean(string value, int max)
        {
            var cleaned = string.Join(" ", (value ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).Trim('"', '\'', ' ');
            return cleaned.Length <= max ? cleaned : cleaned.Substring(0, max);
        }
        private static string Limit(string value, int max) => (value ?? "").Length <= max ? value ?? "" : value.Substring(0, max);
    }
}
