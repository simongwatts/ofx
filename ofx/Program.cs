using System.Collections.Generic;
using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Namotion.Reflection;
using OllamaSharp;
using OllamaSharp.Models;

namespace Ofx
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                // Core options
                var modelOption = new Option<string>(
                    new[] { "-m", "--model" },
                    () => "deepseek-r1:8b",
                    "Define the model to run on Ollama.");

                var endpointOption = new Option<Uri>(
                    new[] { "-e", "--endpoint" },
                    () => new Uri("http://localhost:11434"),
                    "Ollama API endpoint URL.");

                var systemPromptOption = new Option<string>(
                    new[] { "-s", "--system-prompt" },
                    () => "You are a command-line program that takes an input and provides an output ONLY. Give me only the output, without any additional labels (e.g., 'Output' or 'Result'). The output should be usable as input in another program that is not an LLM. Avoid unnecessary chat. No preamble, get straight to the point. Generate a text response suitable for downstream processing by another program. Do not change the content of the input unless specifically asked to. Do not repeat back the input.",
                    "System prompt for the LLM.");

                var verboseOption = new Option<bool>(
                    new[] { "-v", "--verbose" },
                    () => false,
                    "Show verbose output including configuration and traces");

                var promptArgument = new Argument<string>(
                    "prompt",
                    "The actual prompt for the LLM.");

                // Create dynamic options
                var dynamicOptions = RequestOptionsBuilder.CreateDynamicOptions().ToList();

                var rootCommand = new RootCommand("OFX - Ollama Function eXecutor (C# Version)")
                {
                    modelOption,
                    endpointOption,
                    systemPromptOption,
                    verboseOption,
                    promptArgument
                };

                // Add dynamic options
                foreach (var option in dynamicOptions)
                {
                    rootCommand.AddOption(option);
                }

                rootCommand.SetHandler(async (RequestParameters parameters) =>
                {
                    try
                    {
                        await RunOllamaQuery(parameters);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"Operation failed: {ex}");
                        Console.WriteLine($"Error: {ex.Message}");
                        Environment.Exit(1);
                    }
                }, new RequestParametersBinder(
                    modelOption,
                    endpointOption,
                    systemPromptOption,
                    verboseOption,
                    promptArgument,
                    dynamicOptions));

                return await rootCommand.InvokeAsync(args);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Fatal initialization error: {ex}");
                Console.WriteLine($"Fatal error: {ex.Message}");
                return 1;
            }
        }

        static async Task RunOllamaQuery(RequestParameters parameters)
        {
            try
            {
                var ollama = new OllamaApiClient(parameters.Endpoint)
                {
                    SelectedModel = parameters.Model
                };

                var request = new GenerateRequest
                {
                    Prompt = parameters.Prompt,
                    System = parameters.SystemPrompt,
                    Options = parameters.RequestOptions
                };

                await foreach (var response in ollama.GenerateAsync(request))
                {
                    Console.Write(response?.Response);
                    await Console.Out.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"API communication error: {ex}");
                throw;
            }
        }
    }

    #region Support Classes
    public static class AssemblyExtensions
    {
        public static string GetInformationalVersion(this Assembly assembly)
        {
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "Unknown";
        }

        public static string GetCopyright(this Assembly assembly)
        {
            return assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                ?? "Unknown";
        }

        public static IEnumerable<Assembly> GetNuGetPackageAssemblies(this Assembly assembly)
        {
            var frameworkAssemblies = new HashSet<string>
            {
                "System.", "Microsoft.", "netstandard", "mscorlib",
                "WindowsBase", "Presentation", "Accessibility"
            };

            return assembly.GetReferencedAssemblies()
                .Where(a => a.Name is not null)
                .Where(a => !frameworkAssemblies.Any(f =>
                    a.Name!.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                .Select(Assembly.Load)
                .Where(a => a.GetName().Name is not null)
                .Where(a => !string.Equals(
                    a.GetName().Name,
                    assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class RequestOptionsBuilder
    {
        private static readonly Dictionary<string, object> _optionDefaults = new()
        {
            ["num_ctx"] = 4096,
            ["temperature"] = 0.6f
        };

        public static IEnumerable<Option> CreateDynamicOptions()
        {
            var options = new List<Option>();
            var contextualType = typeof(RequestOptions).ToContextualType();

            foreach (var contextualProperty in contextualType.Properties)
            {
                var jsonAttr = contextualProperty.PropertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonAttr == null) continue;

                var (description, _) = ParseXmlDocs(contextualProperty);
                _optionDefaults.TryGetValue(jsonAttr.Name, out var defaultValue);

                var option = CreateOption(
                    contextualProperty,
                    jsonAttr.Name,
                    description,
                    defaultValue);

                if (option != null)
                {
                    options.Add(option);
                    AddValidators(option, contextualProperty);
                }
            }

            return options;
        }

        private static (string Description, object? DefaultValue) ParseXmlDocs(ContextualPropertyInfo property)
        {
            var xmlDoc = property.GetXmlDocsSummary();
            var cleaned = Regex.Replace(xmlDoc, @"<see.*?/>", "")
                .Replace("\n", " ")
                .Trim();

            var defaultMatch = Regex.Match(cleaned, @"\([Dd]efault:? (.*?)\)");
            if (!defaultMatch.Success) return (cleaned, null);

            var description = Regex.Replace(cleaned, @"\s*\([Dd]efault:.*?\)", "").Trim();
            return (description, null);
        }

        private static Option? CreateOption(
            ContextualPropertyInfo property,
            string jsonName,
            string description,
            object? defaultValue)
        {
            var optionType = GetOptionType(property.PropertyInfo.PropertyType);
            if (optionType == null) return null;

            var aliases = new[] { $"--{jsonName.ToKebabCase()}" };
            var option = (Option)Activator.CreateInstance(
                typeof(Option<>).MakeGenericType(optionType),
                new object?[] { aliases, description })!;

            if (defaultValue != null)
            {
                option.SetDefaultValue(defaultValue);
            }

            option.ArgumentHelpName = GetTypeDisplayName(property.PropertyInfo.PropertyType);
            return option;
        }

        private static void AddValidators(Option option, ContextualPropertyInfo property)
        {
            var range = property.PropertyInfo.GetCustomAttribute<RangeAttribute>();
            if (range == null) return;

            option.AddValidator(result =>
            {
                var value = result.GetValueForOption(option);
                if (value == null) return;

                try
                {
                    var converted = Convert.ToDouble(value);
                    if (converted < (double)range.Minimum || converted > (double)range.Maximum)
                    {
                        result.ErrorMessage = $"{option.Name} must be between {range.Minimum} and {range.Maximum}";
                    }
                }
                catch
                {
                    result.ErrorMessage = $"{option.Name} has invalid value";
                }
            });
        }

        private static Type? GetOptionType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType switch
            {
                _ when underlyingType == typeof(int) => typeof(int?),
                _ when underlyingType == typeof(float) => typeof(float?),
                _ when underlyingType == typeof(bool) => typeof(bool?),
                _ when underlyingType == typeof(string[]) => typeof(string[]),
                _ => null
            };
        }

        private static string GetTypeDisplayName(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.Name switch
            {
                nameof(Int32) => "INT",
                nameof(Single) => "FLOAT",
                nameof(Boolean) => "BOOL",
                _ => "TEXT"
            };
        }
    }

    public class RequestParameters
    {
        public string Model { get; set; } = default!;
        public Uri Endpoint { get; set; } = default!;
        public string SystemPrompt { get; set; } = default!;
        public bool Verbose { get; set; }
        public string Prompt { get; set; } = default!;
        public RequestOptions RequestOptions { get; set; } = new();
    }

    public class RequestParametersBinder(
        Option<string> modelOption,
        Option<Uri> endpointOption,
        Option<string> systemPromptOption,
        Option<bool> verboseOption,
        Argument<string> promptArgument,
        IEnumerable<Option> requestOptions) : BinderBase<RequestParameters>
    {
        private static readonly Dictionary<string, object> _optionDefaults = new()
        {
            ["num_ctx"] = 4096,
            ["temperature"] = 0.6f
        };

        protected override RequestParameters GetBoundValue(BindingContext context)
        {
            var parameters = new RequestParameters
            {
                Model = context.ParseResult.GetValueForOption(modelOption)!,
                Endpoint = context.ParseResult.GetValueForOption(endpointOption)!,
                SystemPrompt = context.ParseResult.GetValueForOption(systemPromptOption)!,
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                Prompt = context.ParseResult.GetValueForArgument(promptArgument)!
            };

            if (parameters.Verbose)
            {
                LogConfiguration(context);
            }

            parameters.RequestOptions = BindRequestOptions(context);
            return parameters;
        }

        private void LogConfiguration(BindingContext context)
        {
            var assembly = Assembly.GetEntryAssembly()!;

            Trace.Listeners.Add(new ConsoleTraceListener());

            Trace.TraceInformation($"OFX Version: {assembly.GetInformationalVersion()}");
            Trace.TraceInformation($"Copyright: {assembly.GetCopyright()}");

            Trace.TraceInformation("\nConfiguration Options:");
            var options = new List<Option>()
                .Concat(new Option[] {
                    modelOption,
                    endpointOption,
                    systemPromptOption,
                    verboseOption
                })
                .Concat(requestOptions);

            foreach (var option in options)
            {
                var value = context.ParseResult.GetValueForOption(option);
                var defaultValue = GetOptionDefaultValue(option);

                if (value != null)
                {
                    Trace.TraceInformation($"  {string.Join(", ", option.Aliases),-20} " +
                        $"Value: {FormatValue(value)} " +
                        $"(Default: {FormatValue(defaultValue)})");
                }
            }

            Trace.TraceInformation($"\n  {"[prompt]",-20} Value: {context.ParseResult.GetValueForArgument(promptArgument)}");

            Trace.TraceInformation("\nNuGet Dependencies:");
            foreach (var packageAssembly in assembly.GetNuGetPackageAssemblies())
            {
                var name = packageAssembly.GetName();
                Trace.TraceInformation($"  {name.Name,-25} v{name.Version}");
            }

            Trace.TraceInformation(new string('=', 60));
        }

        private static object? GetOptionDefaultValue(Option option)
        {
            try
            {
                if (option is IValueDescriptor valueDescriptor)
                {
                    if (valueDescriptor.ValueType == typeof(bool))
                    {
                        return false;
                    }
                    return valueDescriptor.GetDefaultValue();
                }
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null => "(null)",
                Uri uri => uri.ToString(),
                bool b => b.ToString().ToLower(),
                string s => s,
                _ => value.ToString() ?? "(null)"
            };
        }

        private RequestOptions BindRequestOptions(BindingContext context)
        {
            var options = new RequestOptions();
            var type = typeof(RequestOptions);

            foreach (var property in type.GetProperties())
            {
                var jsonAttr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonAttr == null) continue;

                var optionName = jsonAttr.Name.ToKebabCase();
                var option = requestOptions.FirstOrDefault(o =>
                    o.Aliases.Any(a => a.Equals($"--{optionName}", StringComparison.OrdinalIgnoreCase)));

                if (option != null)
                {
                    object? value = null;

                    if (context.ParseResult.HasOption(option))
                    {
                        value = context.ParseResult.GetValueForOption(option);
                    }
                    else if (_optionDefaults.TryGetValue(jsonAttr.Name, out var defaultValue))
                    {
                        value = defaultValue;
                    }

                    if (value != null)
                    {
                        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        var convertedValue = Convert.ChangeType(value, targetType);

                        if (property.PropertyType.IsGenericType &&
                            property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            convertedValue = Activator.CreateInstance(property.PropertyType, convertedValue);
                        }

                        property.SetValue(options, convertedValue);
                    }
                }
            }

            return options;
        }
    }

    public static class StringExtensions
    {
        public static string ToKebabCase(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return Regex.Replace(
                Regex.Replace(
                    Regex.Replace(input, @"([A-Z]+)([A-Z][a-z])", "$1-$2"),
                    @"([a-z\d])([A-Z])",
                    "$1-$2"),
                @"[_\s]+",
                "-")
                .ToLower();
        }
    }
    #endregion
}