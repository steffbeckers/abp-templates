<!-- <svelte:head>
    <link rel="stylesheet" href="//cdnjs.cloudflare.com/ajax/libs/highlight.js/11.3.1/styles/github.min.css">
</svelte:head> -->

<script>
    // TODO
    // import LibLoader from './LibLoader.svelte';

    import { onMount } from "svelte";
    import * as signalR from "@microsoft/signalr";
    
    let version;
    let settings;

    let realtimeConnection;
    let connected = true;

    let snippetTemplates = [];
    let selectedSnippetTemplateOutputPaths = [];
    let snippetTemplate;

    let projectTemplates = [];
    let selectedProjectTemplateName;

    onMount(async () => {
        fetch("/api/version")
            .then((response) => response.text())
            .then((data) => {
                version = data;
            });

        fetch("/api/settings")
            .then((response) => response.json())
            .then((data) => {
                settings = data;
            });

        fetch("/api/templates/snippets")
            .then((response) => response.json())
            .then((data) => {
                snippetTemplates = data;
                selectedSnippetTemplateOutputPaths = [snippetTemplates[0].outputPath]
                setSnippetTemplate();
            });

            
        fetch("/api/templates/projects")
            .then((response) => response.json())
            .then((data) => {
                projectTemplates = data;
                selectedProjectTemplateName = projectTemplates[0].name
            });

        realtimeConnection = new signalR.HubConnectionBuilder()
            .withUrl("/signalr-hubs/realtime")
            .withAutomaticReconnect()
            .build();
        
        realtimeConnection.on("SettingsUpdated", (updatedSettings) => {
            settings = updatedSettings;
        });

        realtimeConnection.on("SnippetTemplateUpdated", (updatedSnippetTemplate) => {
            let updatedSnippetTemplateIndex = snippetTemplates.map(x => x.outputPath).indexOf(updatedSnippetTemplate.outputPath);

            if (updatedSnippetTemplateIndex > -1) {
                snippetTemplates[updatedSnippetTemplateIndex] = updatedSnippetTemplate;
            } else {
                snippetTemplates.push(updatedSnippetTemplate);
            }

            snippetTemplates = snippetTemplates.sort((x, y) => (x.outputPath > y.outputPath) ? 1 : ((y.outputPath > x.outputPath) ? -1 : 0))

            selectedSnippetTemplateOutputPaths = [updatedSnippetTemplate.outputPath];
            setSnippetTemplate();
        });

        realtimeConnection.on("SnippetTemplateDeleted", (deletedSnippetTemplateFullPath) => {
            snippetTemplates = snippetTemplates.filter(x => x.fullPath != deletedSnippetTemplateFullPath);

            selectedSnippetTemplateOutputPaths = [snippetTemplates[0].outputPath];
            setSnippetTemplate();
        });

        realtimeConnection.on("SnippetTemplatesReloaded", (reloadedSnippetTemplates) => {
            snippetTemplates = reloadedSnippetTemplates;

            setSnippetTemplate();
        });

        realtimeConnection.onclose(() => {
            connected = false;
        });

        realtimeConnection.onreconnecting(() => {
            connected = false;
        })

        realtimeConnection.onreconnected(() => {
            connected = true;
        })

        await realtimeConnection.start();
    })

    // TODO
    // function highlightJsLoaded() {
    //     hljs.highlightAll();
    // }
    
    async function openSettingsJson() {
        await fetch("/api/settings/open-json");
    }

    async function updateSettings() {
        await fetch("/api/settings", {
            method: "PUT",
            body: JSON.stringify(settings),
            headers: {
                "Content-Type": "application/json"
            }
        });
    }
    
    function aggregateRootNameChanged() {
        settings.context.aggregateRoot.namePlural = settings.context.aggregateRoot.name + 's';
    }

    async function openProjectPathFolder() {
        await fetch("/api/settings/open-project-folder");
    }

    async function openSnippetTemplatesFolder() {
        await fetch("/api/templates/snippets/open-folder");
    }

    async function editSelectedSnippetTemplates() {
        await fetch("/api/templates/snippets/edit", {
            method: "POST",
            body: JSON.stringify({
                outputPaths: selectedSnippetTemplateOutputPaths
            }),
            headers: {
                "Content-Type": "application/json"
            }
        });
    }

    async function generateSelectedSnippetTemplates() {
        await fetch("/api/templates/snippets/generate", {
            method: "POST",
            body: JSON.stringify({
                outputPaths: selectedSnippetTemplateOutputPaths
            }),
            headers: {
                "Content-Type": "application/json"
            }
        });
    }

    function setSnippetTemplate() {
        if (selectedSnippetTemplateOutputPaths && selectedSnippetTemplateOutputPaths.length != 1) {
            snippetTemplate = null;
            return;
        }

        let selectedSnippetTemplateIndex = snippetTemplates.map(x => x.outputPath).indexOf(selectedSnippetTemplateOutputPaths[0]);
        if (selectedSnippetTemplateIndex > -1) {
            snippetTemplate = snippetTemplates[selectedSnippetTemplateIndex];

            // TODO
            // hljs.initHighlighting.called = false;
            // hljs.initHighlighting();
        }
    }

    async function openProjectTemplatesFolder() {
        await fetch("/api/templates/projects/open-folder");
    }

    async function generateSelectedProjectTemplate() {
        await fetch("/api/templates/projects/generate", {
            method: "POST",
            body: JSON.stringify({
                templateName: selectedProjectTemplateName
            }),
            headers: {
                "Content-Type": "application/json"
            }
        });
    }
</script>

<!-- <LibLoader url="//cdnjs.cloudflare.com/ajax/libs/highlight.js/11.3.1/highlight.min.js"
on:loaded="{highlightJsLoaded}" /> -->

<div class="container">
    <h1>ABP.io Generator</h1>
    {#if version}<div>Version: {version}</div>{/if}
    {#if !connected}
    <h2 style="color: red; font-weigth: bold">
        CLI connection lost!
    </h2>
    {:else}
    <div>
        <h2>Settings</h2>
        <div>
            <button on:click={openSettingsJson} type="button">Open generatorsettings.json</button>
        </div>
        {#if settings}
        <!-- To view the settings as JSON -->
        <!-- <div style="white-space: pre">
            {JSON.stringify(settings, null, 2)}
        </div> -->
        <div style="display: flex; flex-direction: column">
            <div style="flex: 1 1">
                <label for="projectPathSetting">Project path</label>
                <div style="display: flex; gap: 12px">
                    <input style="flex: 3 1" bind:value={settings.projectPath} type="text" id="projectPathSetting" />
                    <button style="flex: 1 1" on:click={openProjectPathFolder} type="button">Open folder</button>
                </div>
            </div>
            <div style="display: flex; gap: 12px">
                <div style="flex: 1 1">
                    <label for="projectNameSetting">Project.Name</label>
                    <input bind:value={settings.context.project.name} type="text" id="projectNameSetting" />
                </div>
                <div style="flex: 0 0">
                    <label for="isModuleSetting">Project.IsModule</label>
                    <input bind:checked={settings.context.project.isModule} type="checkbox" id="isModuleSetting" />
                </div>
                <div style="flex: 1 1">
                    <label for="companyNameSetting">Project.CompanyName</label>
                    <input bind:value={settings.context.project.companyName} type="text" id="companyNameSetting" disabled />
                </div>
                <div style="flex: 1 1">
                    <label for="productNameSetting">Project.ProductName</label>
                    <input bind:value={settings.context.project.productName} type="text" id="productNameSetting" disabled />
                </div>
            </div>
            <div style="display: flex; gap: 12px">
                <div style="flex: 1 1">
                    <label for="aggregateRootNameSetting">AggregateRoot.Name</label>
                    <input bind:value={settings.context.aggregateRoot.name} on:blur={aggregateRootNameChanged} type="text" id="aggregateRootNameSetting" />
                </div>
                <div style="flex: 1 1">
                    <label for="aggregateRootNamePluralSetting">AggregateRoot.NamePlural</label>
                    <input bind:value={settings.context.aggregateRoot.namePlural} type="text" id="aggregateRootNamePluralSetting" />
                </div>
            </div>
            <div>TODO: Add other context based settings like list of entities.</div>
        </div>
        <div>
            <button on:click={updateSettings} type="button">Save</button>
        </div>
        {/if}
    </div>
    <div>
        <h2>Snippet templates</h2>
        <div>
            <button on:click={openSnippetTemplatesFolder} type="button">Open folder</button>
        </div>
        {#if snippetTemplates}
        <div style="display: flex; flex-direction: column">
            <select bind:value={selectedSnippetTemplateOutputPaths} on:change={setSnippetTemplate} style="flex: 1 1 200px" multiple>
                {#each snippetTemplates as snippetTemplate}
                <option value={snippetTemplate.outputPath}>{snippetTemplate.outputPath}</option>
                {/each}
            </select>
            {#if selectedSnippetTemplateOutputPaths.length > 0}
            <div style="display: flex; gap: 12px">
                <button style="flex: 1 1" on:click={editSelectedSnippetTemplates} type="button">Edit template{#if selectedSnippetTemplateOutputPaths.length > 1}s{/if}</button>
                <button style="flex: 1 1" on:click={generateSelectedSnippetTemplates} type="button">Generate snippet{#if selectedSnippetTemplateOutputPaths.length > 1}s{/if}</button>
            </div>
            {/if}
        </div>
        {/if}
        {#if snippetTemplate && snippetTemplate.output}
        <div>
            <h3>Preview</h3>
            <div style="height: 450px; overflow-y: scroll; border: 1px solid #000000; padding: 8px 12px">
                <div style="margin-bottom: 12px; font-size: 12px">// {snippetTemplate.fullPath}</div>
                <pre>
                    <code class="language-csharp" style="font-size: 14px">
                        {snippetTemplate.output}
                    </code>
                </pre>
            </div>
        </div>
        {/if}
    </div>
    <div>
        <h2>Project templates</h2>
        <div>
            <button on:click={openProjectTemplatesFolder} type="button">Open folder</button>
        </div>
        {#if projectTemplates}
        <div style="display: flex; flex-direction: column">
            <select bind:value={selectedProjectTemplateName}>
                {#each projectTemplates as projectTemplate}
                <option value={projectTemplate.name}>{projectTemplate.name} - {projectTemplate.description}</option>
                {/each}
            </select>
            <div style="display: flex; gap: 12px">
                <button style="flex: 1 1" on:click={generateSelectedProjectTemplate} type="button">Generate project</button>
            </div>
        </div>
        {/if}
    </div>
    {/if}
</div>