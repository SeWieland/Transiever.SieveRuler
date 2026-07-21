using System.Text.Json.Serialization;
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RuleDocument))]
[JsonSerializable(typeof(List<RuleDefinition>))]
[JsonSerializable(typeof(DeploymentPlan))]
internal sealed partial class SieveRulerJsonContext : JsonSerializerContext;
