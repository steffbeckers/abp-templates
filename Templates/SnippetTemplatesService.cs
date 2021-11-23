﻿using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SteffBeckers.Abp.Generator.Helpers;
using SteffBeckers.Abp.Generator.Realtime;
using SteffBeckers.Abp.Generator.Settings;
using System.Diagnostics;

namespace SteffBeckers.Abp.Generator.Templates;

public class SnippetTemplatesService
{
    public List<SnippetTemplate> Templates = new List<SnippetTemplate>();

    private readonly IHandlebars? _handlebarsContext = Handlebars.Create();
    private readonly IHubContext<RealtimeHub> _realtimeHub;
    private readonly SettingsService _settingsService;
    private readonly string _templateConfigDelimiter = "#-#-#";

    public SnippetTemplatesService(
        IHubContext<RealtimeHub> realtimeHub,
        SettingsService settingsService)
    {
        _realtimeHub = realtimeHub;
        _settingsService = settingsService;

        HandlebarsHelpers.Register(_handlebarsContext);
    }

    public Task GenerateAsync(GenerateSnippetTemplates input)
    {
        return Parallel.ForEachAsync(input.OutputPaths, async (outputPath, cancellationToken) =>
        {
            SnippetTemplate? snippetTemplate = Templates.FirstOrDefault(x => x.OutputPath == outputPath);
            if (snippetTemplate?.OutputPath == null) return;

            string fullOutputPath = Path.Combine(_settingsService.Settings.ProjectPath, snippetTemplate.OutputPath);
            if (fullOutputPath == null) return;

            string? fullOutputDirectoryPath = Path.GetDirectoryName(fullOutputPath);
            if (fullOutputDirectoryPath == null) return;

            if (!Directory.Exists(fullOutputPath))
            {
                Directory.CreateDirectory(fullOutputDirectoryPath);
            }

            await File.WriteAllTextAsync(fullOutputPath, snippetTemplate.Output, cancellationToken);
        });
    }

    public Task<List<SnippetTemplate>> GetListAsync()
    {
        return Task.FromResult(Templates);
    }

    public async Task InitializeAsync()
    {
        // Copy snippet templates from source to user based snippet templates folder.
        if (!Directory.Exists(FileHelpers.UserBasedSnippetTemplatesPath))
        {
            Directory.CreateDirectory(FileHelpers.UserBasedSnippetTemplatesPath);
            FileHelpers.CopyFilesRecursively(FileHelpers.SnippetTemplatesPath, FileHelpers.UserBasedSnippetTemplatesPath);
        }

        // Load all snippet templates on startup.
        await LoadTemplatesAsync();

        // Reload all snippet templates when the settings change.
        _settingsService.Monitor.OnChange(async (settings) =>
        {
            await LoadTemplatesAsync();

            await _realtimeHub.Clients.All.SendAsync("SnippetTemplatesReloaded", Templates);
        });

        // Watch all template files for changes
        FileSystemWatcher? watcher = new FileSystemWatcher(FileHelpers.UserBasedSnippetTemplatesPath, "*.hbs")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Changed += async (watch, eventArgs) =>
        {
            await LoadTemplateAsync(eventArgs.FullPath);

            foreach (SnippetTemplate snippetTemplate in Templates.Where(x => x.FullPath == eventArgs.FullPath).ToList())
            {
                await _realtimeHub.Clients.All.SendAsync("SnippetTemplateUpdated", snippetTemplate);
            }
        };

        watcher.Renamed += async (watch, eventArgs) =>
        {
            Templates.RemoveAll(x => x.FullPath == eventArgs.OldFullPath);

            await _realtimeHub.Clients.All.SendAsync("SnippetTemplateDeleted", eventArgs.OldFullPath);
        };

        watcher.Deleted += async (watch, eventArgs) =>
        {
            Templates.RemoveAll(x => x.FullPath == eventArgs.FullPath);

            await _realtimeHub.Clients.All.SendAsync("SnippetTemplateDeleted", eventArgs.FullPath);
        };
    }

    public async Task LoadTemplateAsync(string fullPath)
    {
        try
        {
            if (_handlebarsContext == null)
            {
                throw new Exception("Handlebars templating context could not be loaded.");
            }

            Templates.RemoveAll(x => x.FullPath == fullPath);

            string outputPath = fullPath
                .Replace(FileHelpers.UserBasedSnippetTemplatesPath + Path.DirectorySeparatorChar, "")
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(".hbs", "");

            string templateText;
            SnippetTemplateContext templateContext = new SnippetTemplateContext();
            string templateOutput = string.Empty;

            using (StreamReader? templateTextStreamReader = new StreamReader(File.Open(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite)))
            {
                templateText = await templateTextStreamReader.ReadToEndAsync();
            }

            string templateSource = templateText;

            int templateConfigDelimiterIndex = templateText.IndexOf(_templateConfigDelimiter);
            if (templateConfigDelimiterIndex > -1)
            {
                templateContext = JsonConvert.DeserializeObject<SnippetTemplateContext>(templateText.Substring(0, templateConfigDelimiterIndex)) ?? templateContext;
                templateSource = templateText.Substring(templateConfigDelimiterIndex + _templateConfigDelimiter.Length + Environment.NewLine.Length);
            }

            GeneratorContext generatorContext = _settingsService.Settings.Context;

            generatorContext.AggregateRoot.Properties = generatorContext.AggregateRoot.Properties.OrderBy(x => x.Name).ToList();

            if (templateContext.RunForEachEntity)
            {
                foreach (Entity entity in _settingsService.Settings.Context.AggregateRoot.Entities)
                {
                    entity.Properties = entity.Properties.OrderBy(x => x.Name).ToList();

                    generatorContext.Entity = entity;

                    string entityOutputPath = outputPath;

                    HandlebarsTemplate<object, object>? handlebarsOutputPath = _handlebarsContext.Compile(entityOutputPath);
                    entityOutputPath = handlebarsOutputPath(generatorContext);

                    HandlebarsTemplate<object, object>? handlebarsTemplate = _handlebarsContext.Compile(templateSource);
                    templateOutput = handlebarsTemplate(generatorContext);

                    Templates.Add(new SnippetTemplate()
                    {
                        FullPath = fullPath,
                        OutputPath = entityOutputPath,
                        Context = templateContext,
                        Output = templateOutput
                    });
                }
            }
            else
            {
                HandlebarsTemplate<object, object>? handlebarsOutputPath = _handlebarsContext.Compile(outputPath);
                outputPath = handlebarsOutputPath(generatorContext);

                HandlebarsTemplate<object, object>? handlebarsTemplate = _handlebarsContext.Compile(templateSource);
                templateOutput = handlebarsTemplate(generatorContext);

                Templates.Add(new SnippetTemplate()
                {
                    FullPath = fullPath,
                    OutputPath = outputPath,
                    Context = templateContext,
                    Output = templateOutput
                });
            }

            Templates = Templates.OrderBy(x => x.OutputPath).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

    public async Task LoadTemplatesAsync()
    {
        string? templateFilesDirectory = Path.GetDirectoryName(FileHelpers.UserBasedSnippetTemplatesPath);

        if (string.IsNullOrEmpty(templateFilesDirectory))
        {
            throw new Exception($"Template files directory not found: {FileHelpers.UserBasedSnippetTemplatesPath}");
        }

        List<string> templateFilePaths = Directory
            .GetFiles(
                path: templateFilesDirectory,
                searchPattern: "*.hbs",
                searchOption: SearchOption.AllDirectories)
            .ToList();

        Templates.Clear();

        foreach (string templateFilePath in templateFilePaths)
        {
            await LoadTemplateAsync(templateFilePath);
        }
    }

    public Task OpenFolderAsync()
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = FileHelpers.UserBasedSnippetTemplatesPath,
            UseShellExecute = true,
            Verb = "open"
        });

        return Task.CompletedTask;
    }
}