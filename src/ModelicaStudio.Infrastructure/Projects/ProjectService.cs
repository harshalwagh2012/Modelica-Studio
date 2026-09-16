using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelicaStudio.Application.Files;
using ModelicaStudio.Application.Projects;
using ModelicaStudio.Domain;
using ModelicaStudio.Domain.Projects;

namespace ModelicaStudio.Infrastructure.Projects;

public sealed class ProjectService(IAtomicFileWriter atomicFileWriter) : IProjectService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private static readonly Regex QualifiedIdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ModelicaProjectDocument> CreateAsync(
        string projectFilePath,
        string name,
        string? rootPackage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fullPath = NormalizeProjectPath(projectFilePath);
        var projectDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The project path must have a parent directory.", nameof(projectFilePath));
        var effectiveRootPackage = string.IsNullOrWhiteSpace(rootPackage) ? CreateRootPackageName(name) : rootPackage;
        ValidateRootPackage(effectiveRootPackage);

        Directory.CreateDirectory(Path.Combine(projectDirectory, "Models"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "Libraries"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "Results"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, ".modelicastudio"));

        var document = new ModelicaProjectDocument(
            fullPath,
            new ModelicaProject
            {
                Name = name.Trim(),
                RootPackage = effectiveRootPackage,
            });
        await SaveAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task<ModelicaProjectDocument> LoadAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Modelica Studio project file was not found.", fullPath);
        }

        try
        {
            var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var project = JsonSerializer.Deserialize<ModelicaProject>(json, SerializerOptions)
                ?? throw new InvalidModelicaProjectException("The project file is empty or does not contain project metadata.");
            ValidateProject(project, fullPath);
            return new ModelicaProjectDocument(fullPath, project);
        }
        catch (JsonException exception)
        {
            throw new InvalidModelicaProjectException("The project file contains invalid JSON metadata.", exception);
        }
    }

    public async Task SaveAsync(ModelicaProjectDocument project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var fullPath = NormalizeProjectPath(project.ProjectFilePath);
        ValidateProject(project.Project, fullPath);
        var json = JsonSerializer.Serialize(project.Project, SerializerOptions) + Environment.NewLine;
        await atomicFileWriter.WriteTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeProjectPath(string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        return fullPath.EndsWith(ProductInfo.ProjectExtension, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath + ProductInfo.ProjectExtension;
    }

    private static void ValidateProject(ModelicaProject project, string projectFilePath)
    {
        if (project.FormatVersion <= 0 || project.FormatVersion > ModelicaProject.CurrentFormatVersion)
        {
            throw new InvalidModelicaProjectException(
                $"Project format version {project.FormatVersion} is not supported by this version of Modelica Studio.");
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            throw new InvalidModelicaProjectException("The project name is required.");
        }

        if (!string.IsNullOrWhiteSpace(project.RootPackage))
        {
            ValidateRootPackage(project.RootPackage);
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidModelicaProjectException("The project file must have a parent directory.");
        var prefix = projectDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? projectDirectory
            : projectDirectory + Path.DirectorySeparatorChar;
        foreach (var modelFile in project.ModelFiles)
        {
            if (string.IsNullOrWhiteSpace(modelFile) || Path.IsPathRooted(modelFile))
            {
                throw new InvalidModelicaProjectException("Model file entries must be non-empty paths relative to the project directory.");
            }

            var resolved = Path.GetFullPath(Path.Combine(projectDirectory, modelFile));
            if (!resolved.StartsWith(prefix, PathComparison))
            {
                throw new InvalidModelicaProjectException($"Model file '{modelFile}' resolves outside the project directory.");
            }
        }
    }

    private static void ValidateRootPackage(string rootPackage)
    {
        if (!QualifiedIdentifierPattern.IsMatch(rootPackage))
        {
            throw new InvalidModelicaProjectException($"'{rootPackage}' is not a valid qualified Modelica root package name.");
        }
    }

    private static string CreateRootPackageName(string projectName)
    {
        var characters = projectName.Where(static character => char.IsLetterOrDigit(character) || character == '_').ToArray();
        var value = new string(characters);
        if (value.Length == 0)
        {
            return "ModelicaProject";
        }

        return char.IsLetter(value[0]) || value[0] == '_' ? value : "Project" + value;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
