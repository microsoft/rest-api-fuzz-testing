﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrchestratorLogic

open System
open Raft.Message
open Microsoft.Azure
open Microsoft.Azure.Management.ContainerInstance.Fluent
open Microsoft.Azure.Management.ResourceManager.Fluent
open Microsoft.Azure.Management.Fluent
open Microsoft.Extensions.Logging
open Microsoft.Azure.Cosmos.Table
open Raft.JobEvents
open Raft.StorageEntities
open Raft.Webhooks
open Raft.Telemetry
open Raft.Job
open System.Net
open System.Collections.Generic

module ContainerInstances =
    module Tags =
        let [<Literal>] Duration = "duration"
        let [<Literal>] StartTimeUtc = "startTimeUTC"

        // if set to true, then this container group is used for re-running
        // multiple runs of commands
        let [<Literal>] IsIdling = "isIdling"

        //wait for some extra timeout to allow container perform
        //cleanup actions (such as copying logs to common share, closing connections, etc)
        let [<Literal>] PostRunExpectedRunDuration = "postRunExpectedRunDuration"

        //This is set when the post run command is started. Calculation for this: nowUTC + PostRunExpectedRunDuration
        //If this value is set, then garbage collection will use that for figuring out when to delete the job
        let [<Literal>] PostRunTimeoutUtc = "postRunTimeoutUTC"


        let getFromTags (tryParse: string -> bool * 'a) (tags: IReadOnlyDictionary<string, string>) (tag: string) =
            match tags.TryGetValue tag with
            | true, v -> 
                match tryParse v with
                | true, b -> Some b
                | false, _ -> None
            | false, _ -> None

        let inline getBoolFromTags tags tag = getFromTags Boolean.TryParse tags tag
        let inline getDateTimeFromTags tags tag = getFromTags DateTime.TryParse tags tag
        let inline getTimeSpanFromTags tags tag = getFromTags TimeSpan.TryParse tags tag

        let isIdling (tags: Collections.Generic.IReadOnlyDictionary<string, string>) =
            match getBoolFromTags tags IsIdling with
            | Some true -> true
            | None | Some false -> false

        let getStartTime (tags: Collections.Generic.IReadOnlyDictionary<string, string>) =
            getDateTimeFromTags tags StartTimeUtc

        let getDuration (tags: Collections.Generic.IReadOnlyDictionary<string, string>) =
            getTimeSpanFromTags tags Duration

        let getPostRunExpectedRunDuration tags = 
            getTimeSpanFromTags tags PostRunExpectedRunDuration
        
        let getPostRunTimeoutUTC tags =
            getDateTimeFromTags tags PostRunTimeoutUtc

    let [<Literal>] TestTarget = "test-target"

    type System.Threading.Tasks.Task with
        member x.ToAsync = Async.AwaitTask(x)

    type System.Threading.Tasks.Task<'T> with
        member x.ToAsync = Async.AwaitTask(x)

    module Exceptions =
        let exnOfType< 't when 't :> exn > (f: 't -> bool) (e : exn) =
            match e with
            | :? 't as ex when f ex -> Some ex
            | :? System.AggregateException as ex when 
                (ex.InnerException :? 't && f (ex.InnerException :?> 't)) ->
                Some (ex.InnerException :?> 't)
            | _ -> None

        let (|AlreadyExists|_|) (e : exn) = 
            exnOfType<Azure.RequestFailedException> (fun ex -> ex.Status = int Net.HttpStatusCode.Conflict) e


    type AgentConfig =
        {
            ResourceGroup: string
            StorageAccount: string
            StorageAccountKey: string
            KeyVault: string
            AppInsightsKey: string
            OutputSas: string
            StorageTableConnectionString: string
            
            SiteHash: string
            EventGridEndpoint: string
            EventGridKey: string
            UtilsStorageAccount: string
            UtilsStorageAccountKey: string
            UtilsFileShare: string
            
            ResultsStorageAccount : string
            ResultsStorageAccountKey: string
        }

    type DockerConfig =
        {
            Registry: string
            User: string
            Password: string
        }

    type CommunicationClients =
        {
            JobEventsSender: ServiceBus.Core.MessageSender
            JobCreationSender : ServiceBus.Core.MessageSender
            WebhookSender : System.Net.Http.HttpClient
        }

    let containerGroupName (jobId: string) = jobId

    let createJobStatus (jobId: string) (state: JobState) (resultsUrl : string option) (details: Map<string, string> option) =
        let message: JobStatus =
            {
                AgentName = jobId.ToString()
                Tool = ""
                JobId = jobId
                State = state
                Metrics = None
                UtcEventTime = System.DateTime.UtcNow
                Details = details
                Metadata = None
                ResultsUrl = resultsUrl
            }
        Raft.Message.RaftEvent.createJobEvent message

    let postStatus (jobStatusSender: ServiceBus.Core.MessageSender) (jobId: string) (state: JobState) (resultsUrl : string option) (details: Map<string, string> option) =
        async {
            let jobStatus = createJobStatus jobId state resultsUrl details
            do! jobStatusSender.SendAsync( 
                    ServiceBus.Message ( RaftEvent.serializeToBytes jobStatus )
                ).ToAsync
        }

    let rePostJobCreate (jobCreateSender: ServiceBus.Core.MessageSender) (jobCreateMessage : RaftCommand.RaftCommand<Raft.Job.CreateJobRequest>) =
        async {
            let rePostDelay = TimeSpan.FromSeconds(10.0)
            let message = ServiceBus.Message (RaftCommand.serializeToBytes {jobCreateMessage with MessagePostCount = jobCreateMessage.MessagePostCount + 1})
            message.SessionId <- jobCreateMessage.Message.JobId.ToString()
            let! _ = jobCreateSender.ScheduleMessageAsync(
                        message, (DateTimeOffset.UtcNow + rePostDelay)).ToAsync
            ()
        }

    type ContainerConfiguration =
        {
            Tool: string
            Container: string
            Ports : int array option
            
            IsIdling : bool

            Run : Command option
            Idle : Command option
            PostRun : Command option
            Shell : string option
            
            Secrets : string array option
            UserDefinedEnvironmentVariables : Map<string, string> option
        }

    type ContainerToolRun =
        {
            ContainerName : string
            RunDirectory : string option
            WorkDirectory : string option
            ContainerConfiguration: ContainerConfiguration
        }

    type ToolConfig =
        {
            Container : string

            Run : Command
            Idle : Command

            Shell : string option

            EnvironmentVariables : Map<string, string> option
        }

    let getStorageKey (azure: IAzure) (resourceGroup: string, storageAccount: string) =
        async {
            let! storage = azure.StorageAccounts.GetByResourceGroupAsync(resourceGroup, storageAccount).ToAsync
            let! keys = storage.GetKeysAsync().ToAsync
            let storageKey = keys.[0].Value
            return storageKey
        }

    let getStorageKeyTask (azure: IAzure) (resourceGroup: string, storageAccount: string) =
        getStorageKey azure (resourceGroup, storageAccount) |> Async.StartAsTask

    let initializeTools (agentConfig: AgentConfig) =
        async {
            let auth = Microsoft.Azure.Storage.Auth.StorageCredentials(agentConfig.UtilsStorageAccount, agentConfig.UtilsStorageAccountKey;)
            let account = Microsoft.Azure.Storage.CloudStorageAccount(auth, true)
            let sasUrl = account.ToString(true)

            let directoryClient = Azure.Storage.Files.Shares.ShareDirectoryClient(sasUrl, agentConfig.UtilsFileShare, "tools")
            
            let asyncEnum = directoryClient.GetFilesAndDirectoriesAsync().GetAsyncEnumerator()

            let rec loadAllConfigs(allConfigs) =
                async {
                    let! next = asyncEnum.MoveNextAsync().AsTask().ToAsync
                    if next then
                        if asyncEnum.Current.IsDirectory then
                            try
                                let fileClient = directoryClient.GetSubdirectoryClient(asyncEnum.Current.Name).GetFileClient("config.json")
                                let! file = fileClient.DownloadAsync().ToAsync
                                let toolConfig : ToolConfig = Microsoft.FSharpLu.Json.Compact.deserializeStream(file.Value.Content)
                                return! loadAllConfigs ((asyncEnum.Current.Name, Result.Ok((sprintf "/raft-tools/tools/%s" asyncEnum.Current.Name), toolConfig)) :: allConfigs)
                            with ex ->
                                return! loadAllConfigs ((asyncEnum.Current.Name, Result.Error(ex.Message)) :: allConfigs)
                        else
                            return! loadAllConfigs allConfigs
                    else
                        return allConfigs
                }

            let! configs = loadAllConfigs []
            return dict configs
        } |> Async.StartAsTask


    let initializeSecretsFromKeyvault (azure: IAzure) (agentConfig: AgentConfig) =
        async {
            let! keyVault = azure.Vaults.GetByResourceGroupAsync(agentConfig.ResourceGroup, agentConfig.KeyVault).ToAsync
            let! secrets = keyVault.Secrets.ListAsync(true) |> Async.AwaitTask
            let secretNames =
                let e = secrets.GetEnumerator()
                [
                    while e.MoveNext() do
                        yield e.Current.Name.ToLower()
                ]

            let! secretTuples =
                secretNames
                |> List.map (fun name ->
                    async {
                        let! s= keyVault.Secrets.GetByNameAsync(name) |> Async.AwaitTask
                        return name, s.Value
                    }
                )
                |> Async.Sequential
            
            let secrets = secretTuples |> dict

            let privateRegistries =
                secrets 
                |> Seq.filter(fun (KeyValue(k, _)) ->
                    k.ToLower().StartsWith("privateregistry")
                )
                |> Seq.map (fun (KeyValue(k, v)) ->
                    (k.ToLower()), ((v |> Microsoft.FSharpLu.Json.Compact.deserialize) : DockerConfig)
                )
            return secrets, privateRegistries
        } |> Async.StartAsTask

    let getToolConfiguration 
        (dockerConfigs: (string*DockerConfig) seq)
        (toolsConfigs : IDictionary<string, Result<string * ToolConfig, string>>)
        (task: RaftTask) =
            async {
                match toolsConfigs.TryGetValue(task.ToolName) with
                | true, Result.Ok(runDirectory, c) ->
                    let container =
                        dockerConfigs
                        |> Seq.tryPick( fun (k, v) ->
                            let t = sprintf"{%s}" k
                            if c.Container.Contains(t, StringComparison.OrdinalIgnoreCase) then
                                Some (c.Container.Replace(t, v.Registry, StringComparison.OrdinalIgnoreCase))
                            else
                                None
                        )
                        |> Option.defaultValue c.Container

                    return runDirectory,
                        {
                            Tool = task.ToolName
                            Ports = None
                            Container = container

                            Secrets = task.KeyVaultSecrets
                            IsIdling = task.IsIdling

                            Run = Some {
                                ExpectedRunDuration = None
                                ShellArguments = c.Run.ShellArguments
                            }

                            Idle = Some {
                                ExpectedRunDuration = None
                                ShellArguments = c.Idle.ShellArguments
                            }
                            Shell = c.Shell
                            PostRun = None

                            UserDefinedEnvironmentVariables = c.EnvironmentVariables
                        }
                | true, Result.Error(err) ->
                    return failwithf "Cannot get %s tool configuration: %s" task.ToolName err

                | false, _ -> 
                    return failwithf "Failed to get configuration for unsupported tool: %A" task.ToolName
            }

    let jobResultsUrl subscription resourceGroup storageAccountName containerGroupName rootFileShare =
        "https://ms.portal.azure.com/#blade/Microsoft_Azure_FileStorage/"
                    + "FileShareMenuBlade/overview/storageAccountId/"
                    + "%2Fsubscriptions%2F" + subscription
                    + "%2FresourceGroups%2F" + resourceGroup
                    + "%2Fproviders%2FMicrosoft.Storage%2FstorageAccounts%2F"
                    + (sprintf "%s/" storageAccountName)
                    + (sprintf "path/%s/protocol/" (Option.defaultValue containerGroupName rootFileShare))

    let createJobShareAndFolders (logger: ILogger) (containerGroupName: string) (sasUrl: string) (jobCreateRequest: CreateJobRequest) =
        async {
            let shareName, createSubDirectory, shareQuota =
                match jobCreateRequest.JobDefinition.RootFileShare with
                | None ->
                    let shareQuota = 1
                    containerGroupName, (fun _ -> async {return ""}), shareQuota
                | Some rootFileShare ->
                    let shareQuota = 1000
                    rootFileShare, 
                    (fun (shareClient:Azure.Storage.Files.Shares.ShareClient) ->
                        async {
                            let d = sprintf "%s/" containerGroupName
                            let directoryClient = shareClient.GetDirectoryClient(d)
                            let! _  = directoryClient.CreateIfNotExistsAsync().ToAsync
                            return d
                    }), 
                    shareQuota

            let logInfo format = Printf.kprintf logger.LogInformation format
            logInfo "Creating config fileshare: %s" shareName

            let saveString(fileName: string) (data: string) =
                async {
                    let file = Azure.Storage.Files.Shares.ShareFileClient(sasUrl, shareName, fileName)
                    let! _ = file.DeleteIfExistsAsync().ToAsync

                    let! _ = file.CreateAsync(int64 data.Length).ToAsync

                    use fileWriter = new IO.MemoryStream(Text.Encoding.UTF8.GetBytes(data))
                    let! _ = file.UploadAsync(fileWriter).ToAsync
                    let! _ = fileWriter.FlushAsync().ToAsync
                    return ()
                }

            let shareClient = Azure.Storage.Files.Shares.ShareClient(sasUrl, shareName)
            let! _ = shareClient.CreateIfNotExistsAsync(dict[], Nullable(shareQuota)).ToAsync

            let! subDirectory = createSubDirectory(shareClient)

            do! saveString (sprintf "%sjob-config.json" subDirectory) (Microsoft.FSharpLu.Json.Compact.Strict.serialize jobCreateRequest)

            for task in jobCreateRequest.JobDefinition.Tasks do
                let taskDirectory = sprintf "%s%s" subDirectory task.OutputFolder
                let directoryClient = shareClient.GetDirectoryClient(taskDirectory)
                let! _  = directoryClient.CreateIfNotExistsAsync().ToAsync
                do! saveString (sprintf "%s/%s" taskDirectory "task-config.json") (Microsoft.FSharpLu.Json.Compact.Strict.serialize task)

            match jobCreateRequest.JobDefinition.TestTargets with
            | Some tt ->
                for target in tt.Targets do
                    match target.OutputFolder with
                    | Some outputFolder ->
                        let taskDirectory = sprintf "%s%s" subDirectory outputFolder
                        let directoryClient = shareClient.GetDirectoryClient(taskDirectory)
                        let! _  = directoryClient.CreateIfNotExistsAsync().ToAsync
                        ()
                    | None -> ()
            | None -> ()

            return shareName
        }

    module RaftContainerGroup =

        let mountShare (cg: ContainerGroup.Definition.IWithVolume) (storageAccountName: string, storageAccountKey: string) (mounts: FileShareMount array option, isReadOnly: bool) =
            match mounts with
            | Some shares ->
                (cg, shares)
                ||> Array.fold(fun a fs ->
                    let withShares =
                        let v = a.DefineVolume(fs.FileShareName)
                        if isReadOnly then
                            v.WithExistingReadOnlyAzureFileShare(fs.FileShareName)
                        else
                            v.WithExistingReadWriteAzureFileShare(fs.FileShareName)
                    withShares
                        .WithStorageAccountName(storageAccountName)
                        .WithStorageAccountKey(storageAccountKey)
                        .Attach()
                )
            | None -> cg

        
        let configureTasksContainerInstances
                (cg: Choice<ContainerGroup.Definition.IWithVolume, ContainerGroup.Definition.IWithNextContainerInstance>) (resources: Resources)
                (toolContainerRunsWithSecrets: (ContainerToolRun * IDictionary<string, string> * IDictionary<string, string>) array)
                (workDirectory, workVolume) (readOnlyShares, readWriteShares) =

            let numberOfTasks = Array.length toolContainerRunsWithSecrets
            let cores = float resources.Cores
            let mem = float resources.MemoryGBs

            let cpu = System.Math.Round(cores / float numberOfTasks, 2, MidpointRounding.ToZero)
            let ram = System.Math.Round(mem / float numberOfTasks, 1, MidpointRounding.ToZero)

            let r, isIdling, _ =
                ((cg, false, (cores, mem)), toolContainerRunsWithSecrets)
                ||> Array.fold (fun (a, isIdling, (remainingCpu, remainingRam)) (toolContainerRun, secrets, environmentVariables) ->
                    let cpu =
                        if remainingCpu >= 0.01 && remainingCpu < cpu then
                            System.Math.Round(remainingCpu, 2, MidpointRounding.ToZero)
                        else
                            cpu

                    let ram =
                        if remainingRam >= 0.1 && remainingRam < ram then
                            System.Math.Round(remainingRam, 1, MidpointRounding.ToZero)
                        else
                            ram

                    let c =
                        let b =
                            match a with
                            | Choice1Of2 b ->
                                b.DefineContainerInstance toolContainerRun.ContainerName
                            | Choice2Of2 (b: ContainerGroup.Definition.IWithNextContainerInstance) ->
                                b.DefineContainerInstance toolContainerRun.ContainerName

                        let b1 = 
                            b.WithImage(toolContainerRun.ContainerConfiguration.Container)

                        let b2 =
                            match toolContainerRun.ContainerConfiguration.Ports with
                            | None ->
                                b1.WithoutPorts().WithCpuCoreCount(cpu)
                            | Some p ->
                                (b1.WithInternalTcpPorts p).WithCpuCoreCount(cpu)
                        b2
                            .WithMemorySizeInGB(ram)
                            .WithVolumeMountSetting(workVolume, workDirectory)

                    let d =
                        match readOnlyShares with
                        | Some shares ->
                            (c, shares)
                            ||> Array.fold (fun s fs ->
                                    s.WithReadOnlyVolumeMountSetting(fs.FileShareName, fs.MountPath)
                                )
                        | None -> c

                    let e =
                        match readWriteShares with
                        | Some shares ->
                            (d, shares)
                            ||> Array.fold (fun s fs ->
                                s.WithVolumeMountSetting(fs.FileShareName, fs.MountPath)
                            )
                        | None -> d

                    let f = e.WithEnvironmentVariablesWithSecuredValue secrets
                    let g = f.WithEnvironmentVariables environmentVariables

                    let command =
                        match toolContainerRun.ContainerConfiguration.Shell with
                        | Some sh ->
                            if toolContainerRun.ContainerConfiguration.IsIdling then
                                match toolContainerRun.ContainerConfiguration.Idle with
                                | Some c -> Some(sh, match c.ShellArguments with Some args -> args | None -> Array.empty)
                                | None -> failwith "No idle command is set"
                            else
                                match toolContainerRun.ContainerConfiguration.Run with
                                | Some r -> Some(sh, match r.ShellArguments with Some args -> args | None -> Array.empty)
                                | None -> None
                        | None ->
                            if toolContainerRun.ContainerConfiguration.IsIdling then
                                match toolContainerRun.ContainerConfiguration.Idle with
                                | Some _ -> failwith "Cannot exectue Idle command since shell is not set"
                                | None -> ()
                            else
                                match toolContainerRun.ContainerConfiguration.Run with
                                | Some _ -> failwith "Cannot execute Run command since shell is not set"
                                | None -> ()
                            None

                    let cg =
                        match command with
                        | Some(shell, args) ->
                            Choice2Of2(g.WithStartingCommandLine(shell, args).Attach())
                        | None ->
                            Choice2Of2(g.Attach())
                    cg,(isIdling || toolContainerRun.ContainerConfiguration.IsIdling), (remainingCpu - cpu, remainingRam - ram)
                )
            r, isIdling

    let getTaskWorkDirectoryPath (containerGroupName : string) (rootFileShare: string option) (workDirectory : string) (taskOutputFolder : string) =
        match rootFileShare with
        | None -> sprintf "%s/%s" workDirectory taskOutputFolder
        | Some _ -> sprintf "%s/%s/%s" workDirectory containerGroupName taskOutputFolder

    let getContainerGroupInstanceConfiguration
            (containerGroupName: string)
            (logger:ILogger)
            (agentConfig: AgentConfig)
            (dockerConfigs: (string * DockerConfig) seq)
            (toolsConfigs : IDictionary<string, Result<string * ToolConfig, string>>)
            (jobCreateRequest: Raft.Job.CreateJobRequest) =
        async {
            let workVolume = jobCreateRequest.JobId
            let workDirectory = sprintf "/work-directory-%s" jobCreateRequest.JobId

            let auth = Microsoft.Azure.Storage.Auth.StorageCredentials(agentConfig.ResultsStorageAccount, agentConfig.ResultsStorageAccountKey)
            let account = Microsoft.Azure.Storage.CloudStorageAccount(auth, true)
            let sasUrl = account.ToString(true)

            let makeToolConfig payload =
                getToolConfiguration dockerConfigs toolsConfigs payload

            let! shareName = createJobShareAndFolders logger containerGroupName sasUrl jobCreateRequest

            jobCreateRequest.JobDefinition.Tasks
            |> Array.countBy(fun task -> task.ToolName)
            |> (fun tasks ->
                Central.Telemetry.TrackMetric(TelemetryValues.Tasks(tasks, jobCreateRequest.JobDefinition.Tasks.Length), "N")
            )

            let! containerToolRuns = 
                jobCreateRequest.JobDefinition.Tasks 
                |> Array.mapi (fun i task ->
                                async {
                                    let! (runDirectory, toolConfig) = makeToolConfig task
                                    return
                                        {
                                            RunDirectory = Some runDirectory
                                            WorkDirectory = Some(getTaskWorkDirectoryPath containerGroupName jobCreateRequest.JobDefinition.RootFileShare workDirectory task.OutputFolder)
                                            ContainerName = (sprintf "%d-%s" i task.OutputFolder).ToLowerInvariant()
                                            ContainerConfiguration = toolConfig
                                        }
                                }
                              )
                |> Async.Sequential
            return containerToolRuns, shareName, workVolume, workDirectory
        }

    let getContainerRunCommandString command (commandArguments: string array option) =
        let commandArgumentsString =
            match commandArguments with
            | None -> ""
            | Some args ->
                args
                |> Array.map(fun s -> sprintf "\"%s\"" (s.Replace("\"", "\\\"")))
                |> (fun args -> String.Join(" ", args))

        sprintf "%s %s" command commandArgumentsString

    let runWebsocketCmd (logger:ILogger) (existingContainerGroup : IContainerGroup, containerName : string) (shell: string, cmd : string) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let! runCmd = existingContainerGroup.ExecuteCommandAsync(containerName, shell, 80, 40).ToAsync

            use websocketsClient = new System.Net.WebSockets.ClientWebSocket()
            do! websocketsClient.ConnectAsync(System.Uri(runCmd.WebSocketUri), Async.DefaultCancellationToken).ToAsync
            
            let send (s: string) =
                websocketsClient.SendAsync( ArraySegment(System.Text.Encoding.UTF8.GetBytes(s)),
                                            System.Net.WebSockets.WebSocketMessageType.Text,
                                            true, 
                                            Async.DefaultCancellationToken).ToAsync

            let sendCommand (s: string) = send (s + "\r\n")

            let ar = Array.zeroCreate 1024
            let seg = ArraySegment<byte>(ar)

            let rec receive () = async {
                let! resp = websocketsClient.ReceiveAsync(seg, Async.DefaultCancellationToken).ToAsync
                if resp.EndOfMessage || resp.Count = Array.length ar then
                    let ch = System.Text.Encoding.UTF8.GetChars(ar, 0, resp.Count)
                    let str = String(ch)
                    printfn "%s" str
                    return str
                else
                    return! receive ()
            }
            do! send runCmd.Password
            let! _ = receive()
            do! sendCommand cmd

            try
                let! _ = receive()
                let! rsp = receive()
                try
                    do! sendCommand "exit"
                    do! websocketsClient.CloseAsync(Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Session completed", Async.DefaultCancellationToken).ToAsync
                with
                | ex ->
                    logInfo "Ignoring websocket exception, since execution is complete %A" ex

                return rsp
            with
            | ex -> 
                logInfo "Failed to get response from web-socket due to: %A" ex
                return ""
        }


    let runDebugContainers
        (existingContainerGroup : IContainerGroup)
        (logger:ILogger)
        (agentConfig: AgentConfig)
        (dockerConfigs: (string * DockerConfig) seq)
        (toolsConfigs : IDictionary<string, Result<string * ToolConfig, string>>)
        (jobCreateRequest: Raft.Job.CreateJobRequest) =
            async {
                let logInfo format = Printf.kprintf logger.LogInformation format
                let! containerToolRunsConfigurations, _, _, _ = getContainerGroupInstanceConfiguration existingContainerGroup.Name logger agentConfig dockerConfigs toolsConfigs jobCreateRequest
                let! _ =
                    containerToolRunsConfigurations
                    |> Array.filter (fun toolRunConfig -> toolRunConfig.ContainerConfiguration.IsIdling)
                    |> Array.map (fun toolRunConfig ->
                        async {
                            match toolRunConfig.ContainerConfiguration.Shell, toolRunConfig.ContainerConfiguration.Run with
                            | Some sh, Some r ->
                                let cmd = getContainerRunCommandString sh r.ShellArguments
                                logInfo "Since isIdling is set: on %s in %s running %s" toolRunConfig.ContainerName existingContainerGroup.Name cmd
                                let! _ = runWebsocketCmd logger (existingContainerGroup, toolRunConfig.ContainerName) (sh, cmd)
                                return ()
                            | Some _, None | None, None -> return failwithf "Cannot execute websocket command, since run command is not set"
                            | None, Some _ -> return failwithf "Cannot execute websocket command, since shell is not set."
                    }
                    ) |> Async.Sequential
                ()
            }


    let createContainerGroupInstance
            (containerGroupName: string)
            (logger:ILogger)
            (azure: IAzure)
            (secrets : IDictionary<string, string>)
            (agentConfig: AgentConfig)
            (dockerConfigs: (string * DockerConfig) seq, toolsConfigs : IDictionary<string, Result<string * ToolConfig, string>>)
            (jobCreateRequest: Raft.Job.CreateJobRequest)
            (reportDeploymentError : exn -> Async<unit>)
            =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            try
                if Array.isEmpty jobCreateRequest.JobDefinition.Tasks then
                    return failwithf "No tasks defined for the job: %A" jobCreateRequest.JobId
                else
                    logInfo "Creating container group for job: %A" jobCreateRequest.JobId
                    let resourceGroup = azure.ResourceGroups.GetByName(agentConfig.ResourceGroup)
                    let! containerToolRunsConfigurations, shareName, workVolume, workDirectory = 
                            getContainerGroupInstanceConfiguration containerGroupName logger agentConfig dockerConfigs toolsConfigs jobCreateRequest

                    logInfo "Deploying container group: %s for job : %A" containerGroupName jobCreateRequest.JobId

                    let region = resourceGroup.Region
                    logInfo "Container group does not exist %s. Deploying..." containerGroupName

                    let _1_1 =
                        azure
                            .ContainerGroups
                            .Define(containerGroupName)
                            .WithRegion(match jobCreateRequest.Region with None -> region | Some r -> Core.Region.Create(r))
                            .WithExistingResourceGroup(agentConfig.ResourceGroup)
                            .WithLinux()

                    let _1_2 =
                        let dockerConfigs =
                            dockerConfigs
                            |> Seq.distinctBy(fun (_, dockerConfig) -> dockerConfig.Registry)

                        if Seq.isEmpty dockerConfigs then
                            _1_1.WithPublicImageRegistryOnly()
                        else
                            let h = snd (Seq.head dockerConfigs)
                            (_1_1.WithPrivateImageRegistry(h.Registry, h.User, h.Password), Seq.tail dockerConfigs)
                            ||> Seq.fold(fun a (_, v) ->
                                a.WithPrivateImageRegistry(v.Registry, v.User, v.Password)
                            )

                    let _1 =
                        _1_2.DefineVolume(workVolume)
                            .WithExistingReadWriteAzureFileShare(shareName)
                            .WithStorageAccountName(agentConfig.ResultsStorageAccount)
                            .WithStorageAccountKey(agentConfig.ResultsStorageAccountKey)
                            .Attach()

                    logInfo "Mounting read-only fileshares %s : %A" containerGroupName jobCreateRequest.JobDefinition.ReadOnlyFileShareMounts
                    let _2_1 = RaftContainerGroup.mountShare _1 
                                (agentConfig.ResultsStorageAccount, agentConfig.ResultsStorageAccountKey)
                                (jobCreateRequest.JobDefinition.ReadOnlyFileShareMounts, true)

                    let utilsFileShares = [| { FileShareName = agentConfig.UtilsFileShare; MountPath = "/raft-tools" } |]
                    let _2 = RaftContainerGroup.mountShare _2_1 
                                (agentConfig.UtilsStorageAccount, agentConfig.UtilsStorageAccountKey) 
                                ((Some utilsFileShares), true)

                    logInfo "Mounting read-write fileshares %s : %A" containerGroupName jobCreateRequest.JobDefinition.ReadWriteFileShareMounts
                    let _3 = RaftContainerGroup.mountShare _2 
                                (agentConfig.ResultsStorageAccount, agentConfig.ResultsStorageAccountKey)
                                (jobCreateRequest.JobDefinition.ReadWriteFileShareMounts, false)

                    let getSecret (name: string) = 
                        match secrets.TryGetValue(name.ToLower()) with
                        | true, v -> v
                        | false, _ -> 
                            failwithf "Secret with name %s does not exist" name
                    
                    let startupDelay = 
                        match jobCreateRequest.JobDefinition.TestTargets with
                        | None -> TimeSpan.Zero
                        | Some ts ->
                            if Array.isEmpty ts.Targets then
                                TimeSpan.Zero
                            else
                                (ts.Targets |> Array.maxBy (fun t -> t.ExpectedDurationUntilReady)).ExpectedDurationUntilReady

                    let setupContainerEnvironment (i : int) (config: ContainerToolRun) =
                        let secrets =
                            match config.ContainerConfiguration.Secrets with
                            | Some toolSecrets ->
                                toolSecrets
                                |> Array.map(fun secretName ->
                                    let secret = getSecret secretName
                                    //add prefix in order to avoid overriding existing
                                    //environment variables on the image
                                    sprintf "RAFT_%s" secretName, secret
                                )
                            | None -> [||]

                        let getShell() =
                            match config.ContainerConfiguration.Shell with
                            | Some sh -> sh
                            | None -> failwith "Shell is not set"
    
                        let predefinedEnvironmentVariablesDict =
                            dict ([
                                "RAFT_STARTUP_DELAY", sprintf "%d" (int startupDelay.TotalSeconds)
                                "RAFT_JOB_ID", jobCreateRequest.JobId
                                "RAFT_TASK_INDEX", sprintf "%d" i
                                "RAFT_CONTAINER_GROUP_NAME", containerGroupName
                                "RAFT_CONTAINER_NAME", config.ContainerName
                                "RAFT_APP_INSIGHTS_KEY", agentConfig.AppInsightsKey
                                "RAFT_SITE_HASH", agentConfig.SiteHash
                            ]
                            @ (match config.ContainerConfiguration.Run with Some r -> ["RAFT_RUN_CMD", getContainerRunCommandString (getShell()) r.ShellArguments ] | None -> [])
                            @ (match config.WorkDirectory with Some wd -> ["RAFT_WORK_DIRECTORY", wd] | None -> [])
                            @ (match config.RunDirectory with Some rd -> ["RAFT_TOOL_RUN_DIRECTORY", rd] | None -> [])
                            @ (match config.ContainerConfiguration.PostRun with Some pr -> ["RAFT_POST_RUN_COMMAND", getContainerRunCommandString (getShell()) pr.ShellArguments] | None -> [])
                            @ (match config.ContainerConfiguration.Shell with Some pr -> ["RAFT_CONTAINER_SHELL", pr] | None -> [])
                            @ (Map.toList (Option.defaultValue Map.empty config.ContainerConfiguration.UserDefinedEnvironmentVariables)))

                        let secretsDict = dict (Array.append secrets [|"RAFT_SB_OUT_SAS", agentConfig.OutputSas|])
                        config, secretsDict, predefinedEnvironmentVariablesDict

                    let containerToolRunsConfigurationsWithSecrets =
                        containerToolRunsConfigurations
                        |> Array.mapi setupContainerEnvironment

                    let readOnlyFileShares =
                        Some(
                            Array.append
                                (Option.defaultWith (fun () -> Array.empty) jobCreateRequest.JobDefinition.ReadOnlyFileShareMounts)
                                utilsFileShares
                        )

                    let taskResources =
                        match jobCreateRequest.JobDefinition.TestTargets with
                        | None -> jobCreateRequest.JobDefinition.Resources
                        | Some t ->
                            {
                                jobCreateRequest.JobDefinition.Resources with
                                    Cores = jobCreateRequest.JobDefinition.Resources.Cores - t.Resources.Cores
                                    MemoryGBs = jobCreateRequest.JobDefinition.Resources.MemoryGBs - t.Resources.MemoryGBs
                            }

                    let _4 = RaftContainerGroup.configureTasksContainerInstances
                                (Choice1Of2 _3) taskResources
                                containerToolRunsConfigurationsWithSecrets
                                (workDirectory, workVolume)
                                (readOnlyFileShares, jobCreateRequest.JobDefinition.ReadWriteFileShareMounts)

                    match _4 with
                    | Choice1Of2 (_), _ ->
                        return failwithf "No container instances configured for container group: %s" containerGroupName
                    
                    | Choice2Of2(cg), isIdling ->
                        let w, isIdling =
                            match jobCreateRequest.JobDefinition.TestTargets with
                            | Some t ->
                                let targetRuns =
                                    t.Targets
                                    |> Array.mapi (fun i target ->
                                        {
                                            ContainerName = sprintf "%s-%d" TestTarget i
                                            RunDirectory = None
                                            WorkDirectory =
                                                match target.OutputFolder with
                                                | Some x -> Some(getTaskWorkDirectoryPath containerGroupName jobCreateRequest.JobDefinition.RootFileShare workDirectory x)
                                                | None -> None

                                            ContainerConfiguration = {
                                                Tool = target.Container
                                                Container = target.Container
                                                Ports = target.Ports

                                                IsIdling = match target.IsIdling with None -> false | Some v -> v

                                                Run = target.Run
                                                Idle = target.Idle
                                                PostRun = target.PostRun
                                                Shell = target.Shell

                                                Secrets = target.KeyVaultSecrets
                                                UserDefinedEnvironmentVariables = target.EnvironmentVariables
                                            }
                                        } |> setupContainerEnvironment i
                                    )

                                match RaftContainerGroup.configureTasksContainerInstances
                                        (Choice2Of2 cg)
                                        t.Resources targetRuns
                                        (workDirectory, workVolume)
                                        (readOnlyFileShares, jobCreateRequest.JobDefinition.ReadWriteFileShareMounts) with
                                | Choice1Of2 _, _ -> failwith "Not possible"
                                | Choice2Of2 cg, idling -> cg, (isIdling || idling)
                            | None -> cg, isIdling

                        let postRunExpectedRunSeconds =
                            match jobCreateRequest.JobDefinition.TestTargets with
                            | None -> None
                            | Some tt ->
                                let timeOut =
                                    tt.Targets
                                    |> Array.map (fun t ->
                                        match t.PostRun with
                                        | None -> None
                                        | Some pr -> pr.ExpectedRunDuration
                                    )
                                    |> Array.max
                                timeOut

                        logInfo "Finishing deployment %s: (isIdling: %A)" containerGroupName isIdling
                        let newTags =
                            let tags =
                                let t1 =
                                    // if job is created for debugging, then it has no duration and does not expire
                                    match jobCreateRequest.JobDefinition.Duration, isIdling with
                                    | Some d, false -> Map.empty.Add(Tags.Duration, sprintf "%A" d)
                                    | None, (true | false) | Some _, true -> Map.empty
                                let t2 =
                                    match postRunExpectedRunSeconds with
                                    | None -> t1
                                    | Some pt -> t1.Add(Tags.PostRunExpectedRunDuration, sprintf "%A" pt)
                                t2

                            tags
                                .Add(Tags.StartTimeUtc, sprintf "%A" System.DateTime.UtcNow)
                                .Add(Tags.IsIdling, sprintf "%A" isIdling)

                        async {
                            logInfo "Starting background creation of :%s with tags: %A" containerGroupName newTags
                            try
                                let! _ =
                                    w
                                        .WithTags(newTags)
                                        .WithRestartPolicy(Models.ContainerGroupRestartPolicy.Never)
                                        .CreateAsync()
                                        .ToAsync
                                ()
                            with
                            | ex ->
                                do! reportDeploymentError ex
                        }
                        |> Async.Start

                    logInfo "Started deployment of %s" containerGroupName
                    return Result.Ok ()
            with
            | ex ->
                return Result.Error (ex)
        }

    let getJobStatusTable connectionString = 
        async {
            let tableName = Raft.StorageEntities.JobStatusTableName
            let storageAccount = CloudStorageAccount.Parse(connectionString)
            let tableClient = storageAccount.CreateCloudTableClient()
            let table = tableClient.GetTableReference(tableName)
            return table
        }

    let getWebHookTable connectionString = 
        async {
            let tableName = Raft.StorageEntities.JobWebHookTableName
            let storageAccount = CloudStorageAccount.Parse(connectionString)
            let tableClient = storageAccount.CreateCloudTableClient()
            let table = tableClient.GetTableReference(tableName)
            return table
        }

    let getJobTable connectionString = 
        async {
            let tableName = Raft.StorageEntities.JobTableName
            let storageAccount = CloudStorageAccount.Parse(connectionString)
            let tableClient = storageAccount.CreateCloudTableClient()
            let table = tableClient.GetTableReference(tableName)
            return table
        }

    module ContainerGroupStates =
        let [<Literal>] Stopped = "Stopped"
        let [<Literal>] Running = "Running"
        let [<Literal>] Pending = "Pending"
        let [<Literal>] Succeeded = "Succeeded"
        let [<Literal>] Failed = "Failed"
        let [<Literal>] Repairing = "Repairing"

    let createJob
            (logger:ILogger)
            (secrets : IDictionary<string, string>)
            (dockerConfigs: (string * DockerConfig) seq, toolsConfigs: IDictionary<string, Result<string * ToolConfig, string>>,
                azure: IAzure, agentConfig: AgentConfig, communicationClients: CommunicationClients)
            (message: string) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let logError format = Printf.kprintf logger.LogError format

            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()
            
            logInfo "Got queue message: %s" message
            let decodedMessage: RaftCommand.RaftCommand<Raft.Job.CreateJobRequest> =
                match RaftCommand.tryDeserializeCommand message with
                | Choice1Of2 j -> j
                | Choice2Of2 err -> 
                    logError "Failed to deserialize JSON due to %s. JSON: %s" err message
                    failwithf "Failed to deserialize JSON due to %s" err
            
            let postStatus = postStatus communicationClients.JobEventsSender decodedMessage.Message.JobId

            let containerGroupName = containerGroupName decodedMessage.Message.JobId

            let! existingContainerGroupOpt = 
                async {
                    let! cg = azure.ContainerGroups.GetByResourceGroupAsync(agentConfig.ResourceGroup, containerGroupName).ToAsync
                    return Option.ofObj cg
                }

            let reportDeploymentError (ex: Exception) =
                async {
                    match ex with
                    | :? System.AggregateException as ag ->
                        match ag.InnerException with
                        | :? Microsoft.Rest.Azure.CloudException as ce 
                                when (ce.Response.StatusCode = Net.HttpStatusCode.NotFound || ce.Response.StatusCode = Net.HttpStatusCode.OK ||
                                        ce.Response.StatusCode = Net.HttpStatusCode.TooManyRequests)->
                            // ignore this exception. This is caused due to azure SDK waiting for azure success return after container 
                            // creation finished. Meanwhile container group execution terminated already and garbage collection deleted the container group.
                            // azure SDK throws this failure.
                            ()
                        | :? Microsoft.Rest.Azure.CloudException as ce ->
                            // it looks like the error when container group is transitioning states is OK to ignore. Need to get more info on that.
                            logError "Failed to deploy container group %s due to %A (status code : %A)" containerGroupName ex ce.Response.StatusCode
                            do! postStatus JobState.Error None (Some (Map.empty.Add("Error", ex.Message)))

                        | _ ->
                            logError "Failed to deploy container group %s due to %A" containerGroupName ex
                            do! postStatus JobState.Error None (Some (Map.empty.Add ("Error", ex.Message)))
                    | _ ->
                        logError "Failed to deploy container group %s due to %A" containerGroupName ex
                        do! postStatus JobState.Error None (Some (Map.empty.Add("Error", ex.Message)))
                }

            match existingContainerGroupOpt with
            | None ->
                let! table = getJobStatusTable agentConfig.StorageTableConnectionString
                let retrieve = TableOperation.Retrieve<JobStatusEntity>(decodedMessage.Message.JobId.ToString(), decodedMessage.Message.JobId.ToString())
                let! retrieveResult = table.ExecuteAsync(retrieve).ToAsync

                let isError =
                    if retrieveResult.HttpStatusCode = int HttpStatusCode.OK then
                        let r = retrieveResult.Result :?> JobStatusEntity
                        let currentRow: RaftEvent.RaftJobEvent<JobStatus> = RaftEvent.deserializeEvent r.JobStatus
                        currentRow.Message.State = JobState.Error
                    else
                        false

                if decodedMessage.MessagePostCount > 0 && isError then
                    logInfo "Message for job %A will not be reposted initial container group creation did not succeed" decodedMessage.Message.JobId
                else
                    do! postStatus JobState.Creating None None
                    match! createContainerGroupInstance containerGroupName logger azure secrets agentConfig (dockerConfigs, toolsConfigs) decodedMessage.Message reportDeploymentError with
                    | Result.Ok () ->
                        //this is newly created container. Poll until it is fully running and then update job status
                        do! rePostJobCreate communicationClients.JobCreationSender decodedMessage

                        stopWatch.Stop()
                        logInfo "Time took to start job deployment: %s total seconds %f" containerGroupName stopWatch.Elapsed.TotalSeconds

                    | Result.Error (ex) ->
                        stopWatch.Stop()
                        logError "Failed to create container group for job : %A due to %A (Time it took: %f total seconds)" decodedMessage.Message.JobId ex stopWatch.Elapsed.TotalSeconds
                        Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                        do! postStatus JobState.Error None (Some (Map.empty.Add("Error", ex.Message)))

            | Some existingContainerGroup ->
                match Option.ofObj existingContainerGroup.State with
                | Some state ->
                    match state with
                    | ContainerGroupStates.Running | ContainerGroupStates.Succeeded | ContainerGroupStates.Stopped | ContainerGroupStates.Failed ->
                        stopWatch.Stop()
                        logInfo "Time took to deploy job: %s total seconds %f. State: %s; Provisioning State : %s" 
                            containerGroupName stopWatch.Elapsed.TotalSeconds state existingContainerGroup.ProvisioningState

                        let resultsUrl = jobResultsUrl azure.SubscriptionId agentConfig.ResourceGroup agentConfig.StorageAccount containerGroupName decodedMessage.Message.JobDefinition.RootFileShare
                        do! postStatus JobState.Created (Some resultsUrl) None

                        if decodedMessage.Message.IsIdlingRun then
                            do! runDebugContainers existingContainerGroup logger agentConfig dockerConfigs toolsConfigs decodedMessage.Message
                        else
                            ()

                    | ContainerGroupStates.Pending | ContainerGroupStates.Repairing | null ->
                        do! rePostJobCreate communicationClients.JobCreationSender decodedMessage
                
                    | state ->
                        logError "Unhandled container instance %s state: %s" containerGroupName state
                        do! rePostJobCreate communicationClients.JobCreationSender decodedMessage
                | None ->
                    do! rePostJobCreate communicationClients.JobCreationSender decodedMessage

            return ()
        } |> Async.StartAsTask

    /// Job is manually stopped if Container Group is Stopped explicitly (from portal, or by calling delete)
    let isJobManuallyStopped (g: IContainerGroup) =
        g.State = ContainerGroupStates.Stopped

    let isJobProvisioningFailed (g:IContainerGroup) =
        g.State = ContainerGroupStates.Failed || g.ProvisioningState = ContainerGroupStates.Failed

    let stopJob (containerGroup : IContainerGroup) =
        async {
            if not (isJobManuallyStopped containerGroup || isJobProvisioningFailed containerGroup) then
                do! containerGroup.StopAsync().ToAsync
        }

    //TODO: need to add one more form of deletion where we execute PostRun command on the test target containers
    let delete (logger:ILogger) (azure: IAzure, agentConfig: AgentConfig, communicationClients: CommunicationClients) (message: string) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let logError format = Printf.kprintf logger.LogError format

            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let decodedMessage: RaftCommand.RaftCommand<Raft.Job.DeleteJobRequest> = 
                match RaftCommand.tryDeserializeCommand message with
                | Choice1Of2 j -> j
                | Choice2Of2 err ->
                    logError "Failed to deserialize deletion message due to %s. JSON: %s" err message
                    failwithf "Failed to deserialize deletion message due to %s" err
            logInfo "Got delete queue message: %A" decodedMessage

            let containerGroupName = containerGroupName decodedMessage.Message.JobId
            let! containerGroup = azure.ContainerGroups.GetByResourceGroupAsync(agentConfig.ResourceGroup, containerGroupName).ToAsync
            match (containerGroup |> Option.ofObj) with
            | Some cg ->
                do! stopJob cg
            | None -> ()

            stopWatch.Stop()
            logInfo "Time took to stop job: %s total seconds %f" containerGroupName stopWatch.Elapsed.TotalSeconds

            return ()
        } |> Async.StartAsTask


    module JobStatus =
        let getRow (agentConfig: AgentConfig) (jobId: string, agentName: string) =
            async {
                let! table = getJobStatusTable agentConfig.StorageTableConnectionString
                let retrieve = TableOperation.Retrieve<JobStatusEntity>(jobId, agentName)
                let! retrieveResult = table.ExecuteAsync(retrieve).ToAsync

                if retrieveResult.HttpStatusCode = int HttpStatusCode.OK then
                    let r = retrieveResult.Result :?> JobStatusEntity
                    return Result.Ok(Some r)
                else if retrieveResult.HttpStatusCode = int HttpStatusCode.NotFound then
                    return Result.Ok(None)
                else
                    return Result.Error()
        
            }

        let setRow (agentConfig : AgentConfig) (jobId: string, agentName : string) (message: string, state: Raft.JobEvents.JobState, utcEventTime : DateTime, resultsUrl : string) (etag: string) =
            async {
                let! table = getJobStatusTable agentConfig.StorageTableConnectionString
                let entity = JobStatusEntity(
                                jobId,
                                agentName,
                                message,
                                state |> Microsoft.FSharpLu.Json.Compact.serialize,
                                utcEventTime,
                                resultsUrl,
                                ETag = etag)

                let insertOp = TableOperation.InsertOrReplace(entity)
                let! insertResult = table.ExecuteAsync(insertOp).ToAsync
                if insertResult.HttpStatusCode <> int HttpStatusCode.NoContent then
                    return false
                else
                    return true
            }

        let getEvent (r: JobStatusEntity) : RaftEvent.RaftJobEvent<JobStatus> =
            RaftEvent.deserializeEvent r.JobStatus

        let getState (r: JobStatusEntity) : Raft.JobEvents.JobState =
            match Option.ofObj r.JobState with
            | None ->
                (getEvent r).Message.State
            | Some s ->
                Microsoft.FSharpLu.Json.Compact.deserialize s

    let status (logger:ILogger) (_, agentConfig: AgentConfig, communicationClients: CommunicationClients) (message: string) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            logInfo "[STATUS] Got status message: %s" message

            let eventType = RaftEvent.getEventType message
            if eventType = BugFound.EventType then
                ()
            else if eventType = JobStatus.EventType then
                let decodedMessage: RaftEvent.RaftJobEvent<JobStatus> = RaftEvent.deserializeEvent message
                let jobId, agentName = decodedMessage.Message.JobId, decodedMessage.Message.AgentName

                match! JobStatus.getRow agentConfig (jobId, agentName) with
                | Result.Error() -> logInfo "[STATUS] Failed to retrieve job status table row for %s:%s" jobId agentName
                | Result.Ok(r) ->
                    let currentStatusWithHigherPrecedence, utcEventTime, resultsUrl, etag =
                        match r with
                        | Some row ->
                            let resultsUrl = Option.defaultValue row.ResultsUrl decodedMessage.Message.ResultsUrl
                            let state = JobStatus.getState row
                            if state = decodedMessage.Message.State && decodedMessage.Message.UtcEventTime > row.UtcEventTime then
                                None, decodedMessage.Message.UtcEventTime, resultsUrl, row.ETag
                            else if (state ??> decodedMessage.Message.State) then
                                // if current row job state is one of the next states down the line, then ignore current message altogether.
                                // Since current message is "late"
                                Some (JobStatus.getEvent row), row.UtcEventTime, resultsUrl, row.ETag
                            else
                                None, decodedMessage.Message.UtcEventTime, resultsUrl, row.ETag
                        | None ->
                            let resultsUrl = Option.defaultValue null decodedMessage.Message.ResultsUrl
                            None, decodedMessage.Message.UtcEventTime, resultsUrl, null

                    match currentStatusWithHigherPrecedence with
                    | Some currentRowMessage ->
                        logInfo "Dropping new status message since current status has higher precedence : %A and new message state is : %A [current status: %A new message status: %s ]" 
                                    currentRowMessage.Message.State decodedMessage.Message.State currentRowMessage message
                    | None ->
                        let! updated = JobStatus.setRow agentConfig (jobId, agentName) (message, decodedMessage.Message.State, utcEventTime, resultsUrl) etag
                        if not updated then
                            //Table record got updated by someone else, figure out what to do next.
                            match decodedMessage.Message.State with
                            | JobState.Running ->
                                logInfo "Current message state is Running. The table-record was already updated. Dropping Current message: %s" message
                                // if we are updating running progress -> and record got updated by someone else. 
                                // Then just discard current update.
                                ()
                            | _ ->
                                // Any other 
                                // Try to send message again.
                                logInfo "Current message failed to update the table record. Going to try again: %s" message
                                let message = ServiceBus.Message(RaftEvent.serializeToBytes decodedMessage)
                                do! communicationClients.JobEventsSender.SendAsync(message).ToAsync
                        else
                            let name, units =
                                if decodedMessage.Message.AgentName = decodedMessage.Message.JobId then
                                    "Job", "job"
                                else
                                    (sprintf "Task: %s" (if String.IsNullOrWhiteSpace decodedMessage.Message.Tool then "NotSet" else decodedMessage.Message.Tool)), "task"

                            let collectToolMetrics() =
                                match decodedMessage.Message.Metrics with
                                | None -> ()
                                | Some m ->
                                    Central.Telemetry.TrackMetric(
                                        TelemetryValues.BugsFound(
                                            m.TotalBugBucketsCount,
                                            name,
                                            decodedMessage.Message.UtcEventTime), "Bugs")

                                    for (KeyValue(statusCode, count)) in m.ResponseCodeCounts do
                                        Central.Telemetry.TrackMetric(
                                            TelemetryValues.StatusCount(
                                                statusCode, 
                                                count, 
                                                name, 
                                                decodedMessage.Message.UtcEventTime), "HttpRequests"
                                        )

                            match decodedMessage.Message.State with
                            | JobState.Created ->
                                Central.Telemetry.TrackMetric(TelemetryValues.Created(name, decodedMessage.Message.UtcEventTime), units)
                            | JobState.Completed ->
                                Central.Telemetry.TrackMetric(TelemetryValues.Completed(name, decodedMessage.Message.UtcEventTime), units)
                                collectToolMetrics()
                            | JobState.Error ->
                                Central.Telemetry.TrackMetric(TelemetryValues.Error(name, decodedMessage.Message.UtcEventTime), units)
                                collectToolMetrics()
                            | JobState.TimedOut ->
                                Central.Telemetry.TrackMetric(TelemetryValues.TimedOut(name, decodedMessage.Message.UtcEventTime), units)
                                collectToolMetrics()
                            | JobState.ManuallyStopped ->
                                Central.Telemetry.TrackMetric(TelemetryValues.Deleted(name, decodedMessage.Message.UtcEventTime), units)
                                collectToolMetrics()
                            | JobState.Creating | JobState.Running | JobState.Completing -> ()

                else
                    logInfo "Unhandled message type %s in message %s" eventType message
            return ()
        } |> Async.StartAsTask


    let doDelete (azure: IAzure) (agentConfig: AgentConfig, _) (containerGroupName: string)=
        async {
            try
                do! azure.ContainerGroups.DeleteByResourceGroupAsync(agentConfig.ResourceGroup, containerGroupName).ToAsync
                return Result.Ok ()
            with ex ->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                return Result.Error("Failed to delete Container Instance", ex.Message)
        }

    // This function runs in response to a message in the webhook queue.
    // It takes the message and posts it to the event grid.
    // The message it receives is the "raw" message. The message does not have the webhook envelope wrapped around it. 
    let webhookMessage (logger:ILogger) (_, agentConfig: AgentConfig, communicationClients: CommunicationClients) (message: string) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let logError format = Printf.kprintf logger.LogError format
            logInfo "[WEBHOOK] message: %s" message

            let inline processMessage (jobId: string) (decodedMessage : RaftEvent.RaftJobEvent< ^T >) =
                async {
                    let! table = getWebHookTable agentConfig.StorageTableConnectionString
                    let query = TableQuery<JobWebhookEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, jobId.ToString()))
                    let! segment = table.ExecuteQuerySegmentedAsync(query, null) |> Async.AwaitTask
            
                    if segment.Results.Count = 1 then
                        let webhookName = segment.Results.[0].RowKey
                        let envelope = 
                            {
                                Topic = webhookName
                                Id = Guid.NewGuid().ToString()
                                EventType = decodedMessage.EventType.ToString()
                                Subject = decodedMessage.EventType.ToString()
                                EventTime = DateTime.UtcNow.ToString("O")
                                Data = decodedMessage.Message
                                DataVersion = "1.0"
                            }

                        // Post the message to event grid.
                        let body = Microsoft.FSharpLu.Json.Compact.serialize [envelope]
                        logInfo "[WEBHOOK] body: %s" body

                        use content = 
                            let c = new System.Net.Http.StringContent(body)
                            c.Headers.Add("aeg-sas-key", agentConfig.EventGridKey)
                            c.Headers.ContentType <- Http.Headers.MediaTypeHeaderValue.Parse("application/json")
                            c

                        let! response = communicationClients.WebhookSender.PostAsync(agentConfig.EventGridEndpoint, content) |> Async.AwaitTask

                        if int response.StatusCode < 200 || int response.StatusCode > 299 then
                            logError "[WEBHOOK] Send failure: %O" response
                        else
                            logInfo "[WEBHOOK] Send response: %O" response
                    else
                        ()
                }

            let getMetadata jobId =
                async {
                    let! jobTable = getJobTable agentConfig.StorageTableConnectionString
                    let getOperation = TableOperation.Retrieve<JobEntity>(jobId, jobId)
                    let! retrieveResult = jobTable.ExecuteAsync(getOperation).ToAsync

                    if retrieveResult.HttpStatusCode = int HttpStatusCode.OK then
                        let jobEntity = retrieveResult.Result :?> JobEntity
                        let webhookDefinition = Microsoft.FSharpLu.Json.Compact.deserialize<Raft.Job.Webhook option>(jobEntity.Webhook)
                        match webhookDefinition with
                        | Some webhook ->
                            match webhook.Metadata with
                            | None ->
                                return None
                            | Some m when m.IsEmpty ->
                                 return None
                            | Some m ->
                                return Some m
                        | None ->
                            return None
                    else
                        logError "[WEBHOOK] Getting job (to find pipeline data) failed with status code %d" retrieveResult.HttpStatusCode
                        return None
                }

            let getResultsUrl jobId =
                async {
                    match! JobStatus.getRow agentConfig (jobId, jobId) with
                    | Result.Error() -> return None
                    | Result.Ok(r) ->
                        match r with
                        | None -> return None
                        | Some row -> return (if String.IsNullOrWhiteSpace row.ResultsUrl then None else Some row.ResultsUrl)
                }

            let eventType = RaftEvent.getEventType message
            if eventType = JobStatus.EventType then
                let jobStatus : RaftEvent.RaftJobEvent<JobStatus> = RaftEvent.deserializeEvent message
                let! metadata = getMetadata jobStatus.Message.JobId
                let! resultsUrl = getResultsUrl jobStatus.Message.JobId
                let updatedJobStatus = { jobStatus with
                                           Message = { jobStatus.Message with
                                                         Metadata =  metadata
                                                         ResultsUrl = resultsUrl
                                                       }
                                       }
                do! processMessage updatedJobStatus.Message.JobId updatedJobStatus
            else if eventType = BugFound.EventType then
                let bugFound : RaftEvent.RaftJobEvent<BugFound> = RaftEvent.deserializeEvent message
                let! metadata = getMetadata bugFound.Message.JobId
                let! resultsUrl = getResultsUrl bugFound.Message.JobId
                let updatedBugFound = { bugFound with
                                           Message = { bugFound.Message with
                                                         Metadata =  metadata
                                                         ResultsUrl = resultsUrl
                                                        }
                                       }

                do! processMessage updatedBugFound.Message.JobId updatedBugFound
            else
                logError "[WEBHOOK] Unhandled webhook event type : %s in message :%s" eventType message

            return ()
        } |> Async.StartAsTask

    type Metric =
        {
            Average : float
            TimeStamp: DateTime
        }

    type MetricEvent =
        {
            ContainerGroupName: string
            MetricName : string
            MetricUnits: string
            MetricData : Metric
        }

    let metrics (logger: ILogger) (azure: IAzure) (containerGroup:IContainerGroup) (startTime: DateTime, endTime: DateTime) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let logError format = Printf.kprintf logger.LogError format
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()
            let! metrics =
                async {
                    try
                        let! metrics = azure.MetricDefinitions.ListByResourceAsync(containerGroup.Id).ToAsync
                        let! metricsList =
                            [
                                for m in metrics ->
                                    async {
                                        let! res = m.DefineQuery().StartingFrom(startTime).EndsBefore(endTime).ExecuteAsync().ToAsync
                                        return! [
                                            for r in res.Metrics ->
                                                async {
                                                    let metricEvents: MetricEvent list =
                                                        [
                                                            for t in r.Timeseries do
                                                                for d in t.Data do
                                                                    if d.Average.HasValue then
                                                                        yield
                                                                            {
                                                                                ContainerGroupName = containerGroup.Name
                                                                                MetricName = r.Name.Value
                                                                                MetricUnits = sprintf "%A" r.Unit
                                                                                MetricData = 
                                                                                    {
                                                                                        Average = d.Average.Value
                                                                                        TimeStamp = d.TimeStamp
                                                                                    }
                                                                            }
                                                        ]
                                                    let metricsList =
                                                        metricEvents 
                                                        |> List.map(fun event -> 
                                                            match event.MetricName.ToLowerInvariant() with
                                                            | "networkbytestransmittedpersecond" ->
                                                                Some(TelemetryValues.AverageNetworkBytesTransmittedPerSecond(event.MetricData.Average, event.MetricData.TimeStamp), event.MetricUnits)
                                                            | "networkbytesreceivedpersecond" ->
                                                                Some(TelemetryValues.AverageNetworkBytesReceivedPerSecond(event.MetricData.Average, event.MetricData.TimeStamp), event.MetricUnits)
                                                            | "memoryusage" ->
                                                                Some(TelemetryValues.ContainerGroupAverageRAMUsageMB(event.MetricData.Average, event.MetricData.TimeStamp), event.MetricUnits)
                                                            | "cpuusage" ->
                                                                Some(TelemetryValues.ContainerGroupAverageCPUUsage(event.MetricData.Average, event.MetricData.TimeStamp), event.MetricUnits)
                                                            | _ -> 
                                                                logError  "[Metrics] Unexpected metric name - %s" event.MetricName
                                                                None
                                                        ) 
                                                        |> List.filter Option.isSome
                                                        |> List.map (fun v -> v.Value)

                                                    return metricsList
                                                }
                                        ] |> Async.Sequential
                                    }
                            ] |> Async.Sequential
                        return Some metricsList 
                    with
                    | ex -> 
                        Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                        logError "[Metrics] Failed to get metric %s due to %A" containerGroup.Name ex
                        return None
                }
            stopWatch.Stop()
            logInfo "[Metrics] Time took to run on-timer metrics collection for container  group %s: %f seconds" containerGroup.Name stopWatch.Elapsed.TotalSeconds
            return metrics
        }

    module ContainerInstancesStates =
        let [<Literal>] Terminated = "Terminated"
        let [<Literal>] Error = "Error"
        let [<Literal>] Succeeded = "Succeeded"
        let [<Literal>] Running = "Running"

    /// Job is finished if every container is terminated
    let isJobRunFinished (g: IContainerGroup) =
        if Tags.isIdling g.Tags then
            false
        else
            isJobProvisioningFailed g
            ||
            (g.Containers.Count = 0 ||
                (g.Containers.Count > 0 &&
                    g.Containers
                    |> Seq.forall (fun (KeyValue(_, v)) ->
                        not <| isNull v.InstanceView &&
                            v.InstanceView.CurrentState.State = ContainerInstancesStates.Terminated ||
                            v.Name.StartsWith(TestTarget, StringComparison.InvariantCultureIgnoreCase)
                    )
                )
            )

    // We want to allow all internal processes to complete before we 
    // force kill a container. 
    let durationSlack = TimeSpan.FromMinutes 10.0
    let isJobExpired (logger: ILogger) (g: IContainerGroup) =
        let logInfo format = Printf.kprintf logger.LogInformation format
        if not <| g.Tags.ContainsKey Tags.Duration then
            //if duration tag is not set, then let job run until it terminates
            false
        else
            match Tags.getStartTime g.Tags, Tags.getDuration g.Tags with
            | Some startTime, Some duration ->
                let now = DateTime.UtcNow
                let expirationTime = startTime + duration + durationSlack
                if now > expirationTime then
                    logInfo "Running job expired: %s. %s: %A, %s: %A. UtcNow: %A, ExpirationTimeUtc: %A" g.Name Tags.StartTimeUtc startTime Tags.Duration duration now expirationTime
                    true
                else
                    false
            // If tags do not have correct values, then assume job expired
            | None, None -> 
                logInfo "Running job assumed to be expired, since tags tracking start time and duration are not set: %s" g.Name
                true
            | Some _, None ->
                logInfo "Running job assumed to be expired since %s tag is not set: %s" Tags.Duration g.Name
                true
            | None, Some _ ->
                logInfo "Running job assumed to be expired since %s tag is not set: %s" Tags.StartTimeUtc g.Name
                true

    /// Get a sequence of containers that terminated due to error
    let getContainersExitedWithError (g: IContainerGroup) =
        g.Containers |> Seq.filter(fun (KeyValue(_, v)) ->
            if not <| isNull v.InstanceView then
                let currentState = v.InstanceView.CurrentState
                currentState.DetailStatus = ContainerInstancesStates.Error
            else false)
        |> Seq.map (fun (KeyValue(_, v)) -> v)


    /// Calculate union of all container life times within container group
    let calculateContainerGroupLifeSpan (g: IContainerGroup) =
        (None, g.Containers)
        ||> Seq.fold ( fun lifeSpan (KeyValue(_, v)) ->
            if isNull v.InstanceView then
                lifeSpan
            else
                let instance = v.InstanceView.CurrentState
                match lifeSpan with
                | None ->
                    let startTime =
                        if instance.StartTime.HasValue then instance.StartTime.Value else DateTime.UtcNow
                    let endTime =
                        if instance.FinishTime.HasValue then instance.FinishTime.Value else DateTime.UtcNow
                    Some (startTime, endTime)
                | Some(s, e) ->
                    let startTime =
                        if instance.StartTime.HasValue then min instance.StartTime.Value s else s
                    let endTime =
                        if instance.FinishTime.HasValue then max instance.FinishTime.Value e else e
                    Some(startTime, endTime)
            )

    let executePostRun (agentConfig: AgentConfig, communicationClients: CommunicationClients) (logger: ILogger) (cg: IContainerGroup) =
        async {
            match! JobStatus.getRow (agentConfig) (cg.Name, cg.Name) with
            | Result.Ok(Some r) ->
                let logInfo format = Printf.kprintf logger.LogInformation format
                let logError format = Printf.kprintf logger.LogError format

                match Tags.getPostRunExpectedRunDuration cg.Tags with
                | None ->
                    return true
                | Some d ->
                    match JobStatus.getState r with
                    | JobState.Completing when r.Timestamp + d < System.DateTimeOffset.UtcNow ->
                        return true
                    | JobState.Completing ->
                        return false
                    | s when JobState.Completing ??> s ->
                        do! postStatus communicationClients.JobEventsSender cg.Name JobState.Completing None None
                        let testTargets =
                            cg.Containers
                            |> Seq.map(fun (KeyValue(_, c)) -> c)
                            |> Seq.filter(fun c -> c.Name.StartsWith(TestTarget, StringComparison.InvariantCultureIgnoreCase) )
                    
                        for tt in testTargets do
                            if tt.InstanceView.CurrentState.State = ContainerInstancesStates.Running then
                                let shell = tt.EnvironmentVariables |> Seq.tryFind(fun ev -> ev.Name = "RAFT_CONTAINER_SHELL")
                                let command = tt.EnvironmentVariables |> Seq.tryFind (fun ev -> ev.Name = "RAFT_POST_RUN_COMMAND")

                                match shell, command with
                                | Some sh, Some c ->
                                    let! postRunOut = runWebsocketCmd logger (cg, tt.Name) (sh.Value, c.Value)
                                    logInfo "PostRunOut: %s" postRunOut
                                | None, _ -> logError "Shell must be set in order to execute post-run command for %s" tt.Name
                                | Some _, None -> logInfo "PostRun command is not set. Skipping for %s" tt.Name
                            else
                                logInfo "Cannot execute post run command since container instance %s terminated" tt.Name

                        return false
                    | _ ->
                        return true
            | Result.Error() ->
                return false
            | Result.Ok(None) ->
                return true
        }

    /// Garbage collect all finished jobs.
    /// Job is finished if all containers are stopped or job is running longer than it's set duration.
    /// - Collect and log container group metrics
    ///
    let gc (logger: ILogger) (azure: IAzure, agentConfig: AgentConfig, communicationClients: CommunicationClients) =
        async {
            let logInfo format = Printf.kprintf logger.LogInformation format
            let logError format = Printf.kprintf logger.LogError format

            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let rec processContainerGroups (containerGroups: Core.IPagedCollection<IContainerGroup>) (numberOfSuccessfullDeletions: int, numberOfFailedDeletion: int) =
                async {
                    if (isNull containerGroups) then
                        return (numberOfSuccessfullDeletions, numberOfFailedDeletion)
                    else
                        let successFullDeletionsCount, failedDeletionsCount = ref 0, ref 0
                        for g in containerGroups do
                            try
                                let jobManuallyStopped = isJobManuallyStopped g
                                let jobRunFinished = isJobRunFinished g

                                let! isExpired =
                                    async {
                                        if jobRunFinished || jobManuallyStopped then
                                            // if job is GC ready, then it is already stopped. And therefore cannot be expired
                                            return false
                                        else if isJobExpired logger g then
                                            //since job is expired, need to stop it and garbage collect it
                                            logInfo "[GC] Stopping running job since it expired: %s" g.Name
                                            do! stopJob g
                                            return true
                                        else
                                            return false
                                    }

                                let! postRunFinished =
                                    async {
                                        if jobRunFinished || isExpired then
                                            let! pr = executePostRun (agentConfig, communicationClients) logger g
                                            if pr then
                                                do! stopJob g
                                            return pr
                                        else
                                            return false
                                    }

                                if jobRunFinished && not isExpired then
                                    logInfo "[GC] All containers [Count: %d] terminated for container group: %s" g.Containers.Count g.Name

                                if ((jobRunFinished || isExpired ) && postRunFinished) || jobManuallyStopped then
                                    let containerGroupFailedToProvision = isJobProvisioningFailed g
                                    let instancesExitedWithError = getContainersExitedWithError g

                                    let state, details =
                                        if containerGroupFailedToProvision then
                                            JobState.Error, 
                                                (Map.empty, g.Events |> List.ofSeq)
                                                ||> List.fold( fun details v ->
                                                    details
                                                        .Add("Name", v.Name)
                                                        .Add("Message", v.Message)
                                                        .Add("Type", v.Type)
                                                )
                                        else if isExpired then
                                            JobState.TimedOut, Map.empty
                                        else if jobManuallyStopped then
                                            JobState.ManuallyStopped, Map.empty
                                        else if Seq.isEmpty instancesExitedWithError then
                                            JobState.Completed, Map.empty
                                        else
                                            //There is at least one container that terminated with an error
                                            JobState.Error, 
                                                (Map.empty, instancesExitedWithError |> List.ofSeq)
                                                ||> List.fold (fun details v ->
                                                        details.Add(
                                                            (sprintf "[%s] Exit Code" v.Name), sprintf "%A" v.InstanceView.CurrentState.ExitCode
                                                        ).Add(
                                                            (sprintf "[%s] State" v.Name), sprintf "%A" v.InstanceView.CurrentState.State
                                                        ).Add(
                                                            (sprintf "[%s] Detailed Status" v.Name), (sprintf "%A" v.InstanceView.CurrentState.DetailStatus)
                                                        )
                                                    )

                                    let! detailsWithUsage =
                                        async {
                                            match calculateContainerGroupLifeSpan g with
                                            | Some (startTime, endTime) ->
                                                logInfo "[GC] Collecting metrics for %s from %A to %A" g.Name startTime endTime
                                                match! metrics logger azure g (startTime - TimeSpan.FromMinutes(5.0), endTime + TimeSpan.FromMinutes(5.0)) with
                                                | None -> return details
                                                | Some ms -> 
                                                    ms
                                                    |> Array.iter(fun metricsEvents ->
                                                        metricsEvents
                                                        |> Array.iter(fun events -> 
                                                            events |> List.iter(fun event -> Central.Telemetry.TrackMetric event)
                                                        )
                                                    )

                                                    let cpu, bytesReceived, bytesSent =
                                                        (([], [], []), ms)
                                                        ||> Array.fold(fun (cpu, bytesReceived, bytesSent) metricsEvents ->
                                                            ((cpu, bytesReceived, bytesSent), metricsEvents)
                                                            ||> Array.fold(fun (cpu, bytesReceived, bytesSent) events -> 
                                                                ((cpu, bytesReceived, bytesSent), events) 
                                                                ||> List.fold(fun (cpu, bytesReceived, bytesSent) (event, _) ->
                                                                    match event with
                                                                    | ContainerGroupAverageCPUUsage(v, _) ->
                                                                        (v::cpu, bytesReceived, bytesSent)
                                                                    | AverageNetworkBytesTransmittedPerSecond(v, _) ->
                                                                        (cpu, bytesReceived, v::bytesSent)
                                                                    | AverageNetworkBytesReceivedPerSecond(v, _) ->
                                                                        (cpu, v::bytesReceived, bytesSent)
                                                                    | _ ->
                                                                        (cpu, bytesReceived, bytesSent)
                                                                )
                                                            )
                                                        )
                                                    return
                                                        details.Add(
                                                            "CpuAverage", sprintf "%f" (if List.isEmpty cpu then 0.0 else List.average cpu)
                                                        ).Add(
                                                            "NetworkTotalBytesReceived", sprintf "%d" (if List.isEmpty bytesReceived then 0 else int (List.sum bytesReceived))
                                                        ).Add(
                                                            "NetworkTotalBytesSent", sprintf"%d" (if List.isEmpty bytesSent then 0 else int (List.sum bytesSent))
                                                        )
                                            | None ->
                                                logInfo "[Metrics] ignoring metrics collection since could not calculate container lifespan for container group: %s" g.Name
                                                return details
                                        }

                                    do! postStatus communicationClients.JobEventsSender g.Name state None (Some detailsWithUsage)

                                    for v in instancesExitedWithError do
                                        let! failedContainerLogs = g.GetLogContentAsync(v.Name).ToAsync
                                        
                                        logInfo "[%s][%s][State:%A][DetailStatus: %A][ExitCode: %A] failed logs: %s\nEvents: %A" 
                                                    g.Name v.Name 
                                                    v.InstanceView.CurrentState.State
                                                    v.InstanceView.CurrentState.DetailStatus 
                                                    v.InstanceView.CurrentState.ExitCode
                                                    failedContainerLogs
                                                    (v.InstanceView.Events |> Seq.map (fun e -> e.Name, e.Message))

                                    logInfo "[GC] Deleting container group: %s" g.Name
                                    let! result = doDelete azure (agentConfig, communicationClients) g.Name
                                    
                                    match result with
                                    | Result.Ok() ->
                                        incr successFullDeletionsCount
                                    | Result.Error errors ->
                                        logError "[GC] for container group %s failed to delete: %A" g.Name errors
                                        incr failedDeletionsCount
                            with
                            | ex ->
                                logError "[GC] for container group: %s due to %A" g.Name ex
                                Central.Telemetry.TrackError (TelemetryValues.Exception ex)

                        let! nextGroup = containerGroups.GetNextPageAsync().ToAsync
                        return! processContainerGroups nextGroup (numberOfSuccessfullDeletions + !successFullDeletionsCount, numberOfFailedDeletion + !failedDeletionsCount)
                }
            let! containerGroups = azure.ContainerGroups.ListByResourceGroupAsync(agentConfig.ResourceGroup).ToAsync
            
            let! numberOfSuccessfullfDeletions, numberOfFailedDeletions = processContainerGroups containerGroups (0, 0)
            stopWatch.Stop()
            if numberOfSuccessfullfDeletions <> 0 || numberOfFailedDeletions <> 0 then
                Central.Telemetry.TrackMetric(GCRun(stopWatch.Elapsed.TotalSeconds, numberOfSuccessfullfDeletions, numberOfFailedDeletions), "seconds")
                logInfo "[GC] Time took to run GC: %f seconds. Container Groups deleted [Successfully: %d; Failed: %d]" stopWatch.Elapsed.TotalSeconds numberOfSuccessfullfDeletions numberOfFailedDeletions
            return ()
        } |> Async.StartAsTask
