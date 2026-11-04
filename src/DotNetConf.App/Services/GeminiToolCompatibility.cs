using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotNetConf.App.Services;

public static class GeminiToolCompatibility
{
    public static AIFunction Adapt(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var schema = function.JsonSchema;
        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The schema for {function.Name} must be an object.");
        if (!schema.TryGetProperty("additionalProperties", out var additionalProperties))
            return function;
        if (additionalProperties.ValueKind != JsonValueKind.False)
            throw new NotSupportedException($"The schema for {function.Name} allows additional properties, which Gemini Nano cannot represent.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"The properties for {function.Name} must be an object.");
            foreach (var property in properties.EnumerateObject())
                names.Add(property.Name);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in schema.EnumerateObject())
                if (property.Name != "additionalProperties")
                    property.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return new CompatibleFunction(function, document.RootElement.Clone(), names);
    }

    private sealed class CompatibleFunction(AIFunction inner, JsonElement schema, HashSet<string> names)
        : DelegatingAIFunction(inner)
    {
        public override JsonElement JsonSchema => schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var name in arguments.Keys)
                if (!names.Contains(name))
                    throw new ArgumentException($"Tool {Name} does not accept argument '{name}'.", nameof(arguments));
            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
