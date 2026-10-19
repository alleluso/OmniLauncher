using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OmniLauncher.Models;
namespace OmniLauncher.Services;

public record RegisteredApplication(ProjectType Type, string Path, string Version);

public static class ApplicationPaths
{
    public static string InferVersion(ProjectType type, string path)
    {
        var inferred = ApplicationRegistry.Get(type).InferVersion(path);
        if (!string.IsNullOrWhiteSpace(inferred)) 
            return inferred;
        
        try
        {
            var productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
            return Regex.Match(productVersion, @"^\d+(?:\.\d+)+(?:[abfp]\d+)?").Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    public static string Validate(ProjectType type, string path) => ApplicationRegistry.Get(type).ValidateExecutable(path);

    public static RegisteredApplication DetectApplication(string path, ProjectType? expectedType = null)
    {
        var definition = expectedType.HasValue ? ApplicationRegistry.Get(expectedType.Value)
            : ApplicationRegistry.All.FirstOrDefault(application => application.ExecutableNames.Contains(
                Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException("This executable is not a supported application. Choose a registered application's executable.");
        path = definition.ValidateExecutable(path);
        return new(definition.Type, path, InferVersion(definition.Type, path));
    }
}
