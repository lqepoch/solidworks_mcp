using System.Collections.Immutable;
using System.Text.Json;

namespace SolidWorksMcp.Core;

/// <summary>Stable identity and version for the user-local runtime configuration document.</summary>
/// <remarks>
/// Configuration is separate from the MCP operation envelope so deployment settings can evolve independently.
/// 配置契约与 MCP operation envelope 分离，允许部署设置独立演进而不改变 CAD 业务结果契约。
/// </remarks>
public static class ConfigurationSchema
{
    /// <summary>Schema identifier written into user-local configuration files.</summary>
    public const string Id = "solidworks-mcp.configuration";

    /// <summary>Current configuration schema version.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>Allowlisted provider selection values understood by the composition root.</summary>
/// <remarks>
/// The value chooses a deployment profile; it never loads an arbitrary assembly or executes a command.
/// 该值只选择部署配置，不会加载任意程序集，也不会执行任意命令。
/// </remarks>
public static class ProviderModes
{
    /// <summary>Safe default when no native provider was explicitly supplied.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Future native SOLIDWORKS provider profile.</summary>
    public const string Native = "native";

    /// <summary>Hosted-safe deterministic FakeCad profile used by tests and examples.</summary>
    public const string Fake = "fake";

    /// <summary>Validates and normalizes an allowlisted provider mode.</summary>
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is not (Unavailable or Native or Fake))
        {
            throw new ArgumentException($"Unsupported provider mode '{value}'.", nameof(value));
        }

        return normalized;
    }
}

/// <summary>Stable names for experimental behavior switches.</summary>
/// <remarks>
/// Every flag is explicit and default-off. A new experimental behavior must add a named flag and a compatibility test;
/// it must not be enabled by an unrecognized configuration key. 所有 flag 都显式且默认关闭；新增实验行为必须增加
/// 命名 flag 和兼容性测试，不能被未知配置键意外打开。
/// </remarks>
public static class FeatureFlagNames
{
    /// <summary>Enables experimental deterministic drawing suggestions.</summary>
    public const string ExperimentalDrawing = "experimental.drawing";

    /// <summary>Enables experimental imported-feature recognition suggestions.</summary>
    public const string ExperimentalRecognition = "experimental.recognition";
}

/// <summary>Immutable allowlisted feature flag set.</summary>
public sealed record FeatureFlagSet
{
    /// <summary>Creates a feature set; all flags default to false for fail-closed behavior.</summary>
    public FeatureFlagSet(bool experimentalDrawing = false, bool experimentalRecognition = false)
    {
        ExperimentalDrawing = experimentalDrawing;
        ExperimentalRecognition = experimentalRecognition;
    }

    /// <summary>Gets whether experimental drawing behavior is enabled.</summary>
    public bool ExperimentalDrawing { get; }

    /// <summary>Gets whether experimental recognition behavior is enabled.</summary>
    public bool ExperimentalRecognition { get; }

    /// <summary>Returns one flag value by its stable name; unknown names are never treated as enabled.</summary>
    public bool IsEnabled(string name) => name switch
    {
        FeatureFlagNames.ExperimentalDrawing => ExperimentalDrawing,
        FeatureFlagNames.ExperimentalRecognition => ExperimentalRecognition,
        _ => false,
    };

    /// <summary>Returns deterministic public flag data for capability discovery without exposing file paths.</summary>
    public ImmutableDictionary<string, bool> ToDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [FeatureFlagNames.ExperimentalDrawing] = ExperimentalDrawing,
            [FeatureFlagNames.ExperimentalRecognition] = ExperimentalRecognition,
        }.ToImmutableDictionary(StringComparer.Ordinal);
}

/// <summary>Validated runtime settings shared by Server and future provider composition.</summary>
/// <remarks>
/// This type intentionally contains no credentials and no machine paths. Local paths stay in the loader input and are
/// represented only by source labels. 此类型刻意不包含凭据和机器绝对路径；本地路径只存在于 Loader 输入中，
/// 对外只用来源标签表示。
/// </remarks>
public sealed record SolidWorksMcpConfiguration
{
    /// <summary>Creates validated runtime settings.</summary>
    public SolidWorksMcpConfiguration(
        string schemaVersion = ConfigurationSchema.CurrentVersion,
        string providerMode = ProviderModes.Unavailable,
        FeatureFlagSet? features = null)
    {
        if (!string.Equals(schemaVersion, ConfigurationSchema.CurrentVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Configuration schema must be '{ConfigurationSchema.CurrentVersion}'.",
                nameof(schemaVersion));
        }

        SchemaVersion = schemaVersion;
        ProviderMode = ProviderModes.Normalize(providerMode);
        Features = features ?? new FeatureFlagSet();
    }

    /// <summary>Gets the validated configuration schema version.</summary>
    public string SchemaVersion { get; }

    /// <summary>Gets the allowlisted provider profile.</summary>
    public string ProviderMode { get; }

    /// <summary>Gets default-off experimental feature flags.</summary>
    public FeatureFlagSet Features { get; }
}

/// <summary>Records which configuration layer supplied each effective value.</summary>
/// <remarks>
/// Source labels are intentionally coarse and never contain the user-local file name or environment value.
/// 来源标签刻意保持粗粒度，不包含用户配置文件名或环境变量值，避免把本机信息泄露到诊断结果。
/// </remarks>
public sealed record ConfigurationLoadResult
{
    /// <summary>Creates an immutable load result.</summary>
    public ConfigurationLoadResult(
        SolidWorksMcpConfiguration configuration,
        bool userLocalFileLoaded,
        IReadOnlyDictionary<string, string> sources,
        IEnumerable<string>? warnings = null,
        CadPathAllowlist? pathAllowlist = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        UserLocalFileLoaded = userLocalFileLoaded;
        Sources = (sources ?? throw new ArgumentNullException(nameof(sources)))
            .ToImmutableDictionary(StringComparer.Ordinal);
        Warnings = [.. (warnings ?? [])];
        PathAllowlist = pathAllowlist ?? CadPathAllowlist.DenyAll;
    }

    /// <summary>Gets the validated effective configuration.</summary>
    public SolidWorksMcpConfiguration Configuration { get; }

    /// <summary>Gets whether a user-local JSON file contributed settings.</summary>
    public bool UserLocalFileLoaded { get; }

    /// <summary>Gets field-to-layer provenance without raw machine values.</summary>
    public ImmutableDictionary<string, string> Sources { get; }

    /// <summary>Gets safe non-secret warnings collected while applying optional layers.</summary>
    public ImmutableArray<string> Warnings { get; }

    /// <summary>Gets the private local path policy used by trusted provider composition.</summary>
    /// <remarks>
    /// This property is not included in <see cref="SolidWorksMcpConfiguration"/>, so MCP capability payloads cannot
    /// disclose machine paths. 此属性不放入 SolidWorksMcpConfiguration，因此 MCP capability payload 不会泄露机器路径。
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public CadPathAllowlist PathAllowlist { get; }
}

/// <summary>Loads the four explicit configuration layers in deterministic precedence order.</summary>
/// <remarks>
/// Precedence is defaults, user-local JSON, allowlisted environment variables, then allowlisted command-line options.
/// Unknown environment variables are ignored and unknown command-line options fail closed. 优先级固定为默认值、用户本地
/// JSON、白名单环境变量、白名单 CLI；未知环境变量忽略，未知 CLI 选项直接 fail-closed。
/// </remarks>
public static class SolidWorksMcpConfigurationLoader
{
    /// <summary>Environment variable selecting an explicit user-local configuration path.</summary>
    public const string ConfigPathEnvironmentVariable = "SOLIDWORKS_MCP_CONFIG";

    private const string ProviderModeEnvironmentVariable = "SOLIDWORKS_MCP_PROVIDER_MODE";
    private const string SchemaVersionEnvironmentVariable = "SOLIDWORKS_MCP_SCHEMA_VERSION";
    private const string ExperimentalDrawingEnvironmentVariable = "SOLIDWORKS_MCP_FEATURE_EXPERIMENTAL_DRAWING";
    private const string ExperimentalRecognitionEnvironmentVariable = "SOLIDWORKS_MCP_FEATURE_EXPERIMENTAL_RECOGNITION";
    private const string AllowedPathRootsEnvironmentVariable = "SOLIDWORKS_MCP_ALLOWED_PATH_ROOTS";

    /// <summary>Loads settings using the process environment and the standard local application path.</summary>
    public static ConfigurationLoadResult Load(IEnumerable<string>? commandLineArguments = null)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = entry.Value?.ToString();
        }

        return Load(commandLineArguments, environment);
    }

    /// <summary>Loads settings from injected layers, making precedence and error behavior unit-testable.</summary>
    /// <param name="commandLineArguments">Allowlisted CLI options, without the executable path.</param>
    /// <param name="environment">Environment map; callers may inject a deterministic map in tests.</param>
    /// <param name="defaultUserLocalPath">Optional test seam for the default user-local file path.</param>
    public static ConfigurationLoadResult Load(
        IEnumerable<string>? commandLineArguments,
        IReadOnlyDictionary<string, string?>? environment,
        string? defaultUserLocalPath = null)
    {
        string[] args = [.. commandLineArguments ?? []];
        Dictionary<string, string?> env = [];
        if (environment is not null)
        {
            foreach (KeyValuePair<string, string?> item in environment)
            {
                env[item.Key] = item.Value;
            }
        }
        string? cliConfigPath = ReadConfigPathFromArguments(args);
        string? envConfigPath = GetValue(env, ConfigPathEnvironmentVariable);
        string? userLocalPath = cliConfigPath ?? envConfigPath ?? defaultUserLocalPath ?? GetDefaultUserLocalPath();

        var state = new MutableConfigurationState();
        bool localLoaded = false;
        if (!string.IsNullOrWhiteSpace(userLocalPath))
        {
            string resolvedPath = Path.GetFullPath(userLocalPath);
            if (File.Exists(resolvedPath))
            {
                ApplyJsonFile(state, resolvedPath);
                localLoaded = true;
            }
            else if (cliConfigPath is not null || envConfigPath is not null)
            {
                // An explicitly selected file must not silently fall back to defaults after an operator typo.
                // 操作者显式选择的文件若不存在，不能因为路径错误而静默回退到默认配置。
                throw new ConfigurationException("The explicitly selected user-local configuration file was not found.");
            }
        }

        ApplyEnvironment(state, env);
        ApplyCommandLine(state, args);
        SolidWorksMcpConfiguration configuration = state.Build();
        return new ConfigurationLoadResult(configuration, localLoaded, state.Sources, state.Warnings, state.BuildPathAllowlist());
    }

    /// <summary>Returns the documented default configuration path under LocalApplicationData.</summary>
    public static string GetDefaultUserLocalPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolidWorksMcp", "config.json");

    private static string? ReadConfigPathFromArguments(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--config", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ConfigurationException("--config requires a non-empty user-local file path.");
            }

            return args[index + 1];
        }

        return null;
    }

    private static void ApplyJsonFile(MutableConfigurationState state, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationException("The user-local configuration root must be a JSON object.");
            }

            if (TryGetString(root, "schemaVersion", out string? schemaVersion))
            {
                state.SetSchemaVersion(schemaVersion!, "user-local");
            }

            if (TryGetString(root, "providerMode", out string? providerMode))
            {
                state.SetProviderMode(providerMode!, "user-local");
            }

            if (root.TryGetProperty("features", out JsonElement features))
            {
                if (features.ValueKind != JsonValueKind.Object)
                {
                    throw new ConfigurationException("The user-local 'features' value must be a JSON object.");
                }

                if (TryGetBoolean(features, "experimentalDrawing", out bool experimentalDrawing))
                {
                    state.SetExperimentalDrawing(experimentalDrawing, "user-local");
                }

                if (TryGetBoolean(features, "experimentalRecognition", out bool experimentalRecognition))
                {
                    state.SetExperimentalRecognition(experimentalRecognition, "user-local");
                }
            }

            if (root.TryGetProperty("pathAllowlist", out JsonElement pathAllowlist))
            {
                state.SetPathRoots(ReadPathRoots(pathAllowlist), "user-local");
            }
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException("The user-local configuration is not valid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new ConfigurationException("The user-local configuration could not be read.", exception);
        }
    }

    private static void ApplyEnvironment(
        MutableConfigurationState state,
        IReadOnlyDictionary<string, string?> environment)
    {
        ApplyOptionalString(environment, SchemaVersionEnvironmentVariable, state.SetSchemaVersion, "environment");
        ApplyOptionalString(environment, ProviderModeEnvironmentVariable, state.SetProviderMode, "environment");
        ApplyOptionalBoolean(environment, ExperimentalDrawingEnvironmentVariable, state.SetExperimentalDrawing, "environment");
        ApplyOptionalBoolean(environment, ExperimentalRecognitionEnvironmentVariable, state.SetExperimentalRecognition, "environment");
        string? roots = GetValue(environment, AllowedPathRootsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(roots))
        {
            state.SetPathRoots(roots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), "environment");
        }
    }

    private static void ApplyCommandLine(MutableConfigurationState state, string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--config":
                    index++;
                    break;
                case "--schema-version":
                    state.SetSchemaVersion(ReadRequiredValue(args, ref index, argument), "cli");
                    break;
                case "--provider-mode":
                    state.SetProviderMode(ReadRequiredValue(args, ref index, argument), "cli");
                    break;
                case "--feature":
                    ApplyFeatureArgument(state, ReadRequiredValue(args, ref index, argument));
                    break;
                case "--path-root":
                    state.SetPathRoots([ReadRequiredValue(args, ref index, argument)], "cli");
                    break;
                default:
                    throw new ConfigurationException($"Unknown configuration option '{argument}'.");
            }
        }
    }

    private static void ApplyFeatureArgument(MutableConfigurationState state, string value)
    {
        string[] parts = value.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !bool.TryParse(parts[1], out bool enabled))
        {
            throw new ConfigurationException("--feature must use name=true or name=false.");
        }

        switch (parts[0])
        {
            case FeatureFlagNames.ExperimentalDrawing:
                state.SetExperimentalDrawing(enabled, "cli");
                break;
            case FeatureFlagNames.ExperimentalRecognition:
                state.SetExperimentalRecognition(enabled, "cli");
                break;
            default:
                throw new ConfigurationException($"Unknown feature flag '{parts[0]}'.");
        }
    }

    private static string ReadRequiredValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ConfigurationException($"{option} requires a non-empty value.");
        }

        index++;
        return args[index];
    }

    private static void ApplyOptionalString(
        IReadOnlyDictionary<string, string?> environment,
        string key,
        Action<string, string> setter,
        string source)
    {
        string? value = GetValue(environment, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            setter(value, source);
        }
    }

    private static void ApplyOptionalBoolean(
        IReadOnlyDictionary<string, string?> environment,
        string key,
        Action<bool, string> setter,
        string source)
    {
        string? value = GetValue(environment, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!bool.TryParse(value, out bool parsed))
        {
            throw new ConfigurationException($"Environment variable '{key}' must be true or false.");
        }

        setter(parsed, source);
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> values, string key) =>
        values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool TryGetString(JsonElement objectElement, string propertyName, out string? value)
    {
        if (!objectElement.TryGetProperty(propertyName, out JsonElement property))
        {
            value = null;
            return false;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ConfigurationException($"Configuration property '{propertyName}' must be a non-empty string.");
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetBoolean(JsonElement objectElement, string propertyName, out bool value)
    {
        if (!objectElement.TryGetProperty(propertyName, out JsonElement property))
        {
            value = false;
            return false;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ConfigurationException($"Configuration property '{propertyName}' must be boolean.");
        }

        value = property.GetBoolean();
        return true;
    }

    private static ImmutableArray<string> ReadPathRoots(JsonElement pathAllowlist)
    {
        if (pathAllowlist.ValueKind != JsonValueKind.Object
            || !pathAllowlist.TryGetProperty("roots", out JsonElement roots)
            || roots.ValueKind != JsonValueKind.Array)
        {
            throw new ConfigurationException("Configuration property 'pathAllowlist.roots' must be an array.");
        }

        var values = new List<string>();
        foreach (JsonElement root in roots.EnumerateArray())
        {
            if (root.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(root.GetString()))
            {
                throw new ConfigurationException("Every pathAllowlist root must be a non-empty string.");
            }

            values.Add(root.GetString()!);
        }

        return [.. values];
    }

    private sealed class MutableConfigurationState
    {
        private string schemaVersion = ConfigurationSchema.CurrentVersion;
        private string providerMode = ProviderModes.Unavailable;
        private bool experimentalDrawing;
        private bool experimentalRecognition;
        private ImmutableArray<string> pathRoots = [];

        public Dictionary<string, string> Sources { get; } = [];

        public List<string> Warnings { get; } = [];

        public void SetSchemaVersion(string value, string source)
        {
            schemaVersion = value.Trim();
            Sources["schemaVersion"] = source;
        }

        public void SetProviderMode(string value, string source)
        {
            providerMode = value.Trim();
            Sources["providerMode"] = source;
        }

        public void SetExperimentalDrawing(bool value, string source)
        {
            experimentalDrawing = value;
            Sources[FeatureFlagNames.ExperimentalDrawing] = source;
        }

        public void SetExperimentalRecognition(bool value, string source)
        {
            experimentalRecognition = value;
            Sources[FeatureFlagNames.ExperimentalRecognition] = source;
        }

        public void SetPathRoots(IEnumerable<string> roots, string source)
        {
            pathRoots = [.. roots];
            Sources["pathAllowlist"] = source;
        }

        public CadPathAllowlist BuildPathAllowlist()
        {
            try
            {
                return new CadPathAllowlist(pathRoots);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigurationException("The configured CAD path allowlist is invalid.", exception);
            }
        }

        public SolidWorksMcpConfiguration Build() => new(
            schemaVersion,
            providerMode,
            new FeatureFlagSet(experimentalDrawing, experimentalRecognition));
    }
}

/// <summary>Stable exception for invalid or unsafe runtime configuration input.</summary>
public sealed class ConfigurationException(string message, Exception? innerException = null) : Exception(message, innerException)
{
    // The primary constructor keeps the exception immutable and preserves the original parse/read exception.
    // 主构造函数保持异常对象不可变，并保留底层解析/读取异常用于诊断。
}
