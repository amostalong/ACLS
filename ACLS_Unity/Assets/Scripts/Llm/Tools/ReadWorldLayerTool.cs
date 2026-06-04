using System.Threading;
using System.Threading.Tasks;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 世界层级阅读工具。LLM 在构建 L1 场景时调用此工具读取 L2/L3/L4 上下文。
    ///
    /// 参数：
    ///   layer (string, 必填): 层级——"L2" / "L3" / "L4"
    /// </summary>
    public sealed class ReadWorldLayerTool : ILlmTool
    {
        private readonly World world;

        public ReadWorldLayerTool(World world)
        {
            this.world = world;
        }

        public string Name => "read_world_layer";
        public string Description => "读取指定层级的世界背景文本。"
            + "L4=宏观时代/势力/历史锚点, L3=区域势力/张力, L2=近域人脉/压力/机遇。"
            + "构建L1场景前应先读取L4和L3了解背景。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["layer"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "层级：L2（近域层）/ L3（区域层）/ L4（宏观层）",
                    ["enum"] = new JArray { "L2", "L3", "L4" },
                },
            },
            ["required"] = new JArray { "layer" },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string layer = "";
            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                layer = (string)args["layer"] ?? "";
            }
            catch
            {
                return Task.FromResult("参数解析失败，请提供 layer（层级）。");
            }

            if (world?.Stage == null)
                return Task.FromResult("世界尚未初始化。");

            string result = layer.Trim().ToUpperInvariant() switch
            {
                "L4" => world.Stage.L4World,
                "L3" => world.Stage.L3Expanse,
                "L2" => world.Stage.L2Arena,
                _ => $"未知层级：{layer}，可用层级：L2 / L3 / L4",
            };

            if (string.IsNullOrWhiteSpace(result))
                return Task.FromResult($"{layer} 暂无内容。");

            return Task.FromResult(result);
        }
    }
}
