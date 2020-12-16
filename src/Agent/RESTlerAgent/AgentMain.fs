﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
module RESTlerAgent

open System
open Microsoft.Azure
open Raft.Message
open Newtonsoft
open Microsoft.FSharpLu
open Microsoft.ApplicationInsights
open RESTlerAgentConfigTypes

let inline (++) (path1: string) (path2 : string) = IO.Path.Join(path1, path2)

type System.Threading.Tasks.Task with
    member x.ToAsync = Async.AwaitTask(x)

type System.Threading.Tasks.Task<'T> with
    member x.ToAsync = Async.AwaitTask(x)


let globalRunStartTime = DateTime.UtcNow


type ServiceBus.Core.MessageSender with
    member inline x.SendRaftJobEvent (jobId: string) (message : ^T when ^T : (static member EventType : string)) =
        async {
            let raftJobEvent = Raft.Message.RaftEvent.createJobEvent message
            let message = ServiceBus.Message(RaftEvent.serializeToBytes raftJobEvent, SessionId = jobId.ToString())
            do! x.SendAsync(message).ToAsync
        }

module Arguments =

    type AgentArguments =
        {
            AgentName: string option
            JobId: string option
            TaskConfigPath : string option

            JobEventTopicSAS: string option
            RestlerPath: string option
            WorkDirectory: string option
            AppInsightsInstrumentationKey: string option

            SiteHash : string option
            TelemetryOptOut : bool
        }

        static member Empty =
            {
                AgentName = None
                JobId = None
                TaskConfigPath = None
                JobEventTopicSAS = None
                RestlerPath = None
                WorkDirectory = None
                AppInsightsInstrumentationKey = None

                SiteHash = System.Environment.GetEnvironmentVariable("RAFT_SITE_HASH") |> Option.ofObj
                TelemetryOptOut = 
                    match System.Environment.GetEnvironmentVariable("RESTLER_TELEMETRY_OPTOUT") |> Option.ofObj with
                    | None -> false
                    | Some v when v.ToLowerInvariant() = "true" || v.ToLowerInvariant() = "1" -> true
                    | Some _ -> false
            }

    let [<Literal>] AgentName = "--agent-name"
    let [<Literal>] JobId = "--job-id"
    let [<Literal>] TaskConfigFilePath = "--task-config-path"
    let [<Literal>] JobEventTopicSas = "--output-sas"
    let [<Literal>] AppInsights = "--app-insights-instrumentation-key"
    let [<Literal>] RESTlerPath = "--restler-path"
    let [<Literal>] WorkDirectory = "--work-directory"

    let usage =
        [
            sprintf "%s <Agent name to use for this task when reporting status>" AgentName
            sprintf "%s <String identifying the job>" JobId
            sprintf "%s <Path to the task config file>" TaskConfigFilePath
            sprintf "%s <SAS URL to output status Service Bus topic>" JobEventTopicSas
            sprintf "%s <AppInsights instrumentation key>" AppInsights
            sprintf "%s <Path to RESTler tools>" RESTlerPath
            sprintf "%s <Path to working directory>" WorkDirectory
        ] |> (fun args -> String.Join("\n", args))

    let parseCommandLine (argv: string array) =
        if Array.isEmpty argv then
            failwithf "Expected command line arguments %s" usage

        let rec parse (currentConfig: AgentArguments) (args: string list) =
            match args with
            | [] -> currentConfig

            | AgentName :: agentName :: rest ->
                printfn "Agent Name : %s" agentName
                parse { currentConfig with AgentName = Some agentName} rest

            | JobId :: jobId :: rest ->
                printfn "JobId: %A" jobId
                parse { currentConfig with JobId = Some jobId } rest

            | TaskConfigFilePath :: path :: rest ->
                printfn "Path to the input job-config: %s" path
                parse { currentConfig with TaskConfigPath = Some (path) } rest

            | JobEventTopicSas :: sas :: rest -> 
                printfn "Sas to job-queue: %s" sas
                parse { currentConfig with JobEventTopicSAS = Some (sas) } rest

            | RESTlerPath :: path :: rest ->
                printfn "Path to RESTler : %s" path
                parse { currentConfig with RestlerPath = Some path } rest

            | AppInsights :: key :: rest ->
                printfn "App Insights key: %s" key
                parse { currentConfig with AppInsightsInstrumentationKey = Some ( key )} rest

            | WorkDirectory :: path :: rest ->
                printfn "Path to RAFT Agent work directory: %s" path
                parse { currentConfig with WorkDirectory = Some path } rest

            | arg :: _ -> failwithf "Unhnandled command line parameter: %s" arg

        parse AgentArguments.Empty (argv |> Array.toList)


let downloadFile workDirectory filename (fileUrl: string) =
    async {
        use httpClient = new Net.Http.HttpClient(BaseAddress = (Uri(fileUrl)))
        let! response = httpClient.GetAsync("").ToAsync
        if not response.IsSuccessStatusCode then
            return failwithf "Get %s request failed due to '%A' (status code: %A)" fileUrl response.ReasonPhrase response.StatusCode
        else
            use! inputStream = response.Content.ReadAsStreamAsync().ToAsync

            let filePath = workDirectory ++ filename
            use fileStream = IO.File.Create(filePath)
            do! inputStream.CopyToAsync(fileStream).ToAsync
            do! fileStream.FlushAsync().ToAsync
            fileStream.Close()

            match Option.ofNullable response.Content.Headers.ContentLength with
            | None ->
                printfn "Content does not have length set. Ignoring length validation check"
            | Some expectedLength when expectedLength > 0L ->
                let currentLength = IO.FileInfo(filePath).Length
                if currentLength <> expectedLength then
                    failwithf "Expected length of %s file (%d) does not match what got downloaded (%d)" filePath expectedLength currentLength
            | Some expectedLength ->
                printfn "Content lengths is set to :%d, so skipping validation since it is not greater than 0" expectedLength

            return filePath
    }

let validateJsonFile (filePath : string) =
    try
        ignore <| Microsoft.FSharpLu.Json.Compact.deserializeFile filePath
        Result.Ok()
    with
    | ex -> Result.Error (sprintf "Failed to validate %s due to %s. %A" filePath ex.Message ex)

// Need this for TEST and FUZZ tasks
let createRESTlerEngineParameters
    (workDirectory: string)
    (grammarFilePath: string) (mutationsFilePath: string)
    (task: Raft.Job.RaftTask) (checkerOptions:(string*string) list)
    (runConfiguration: RunConfiguration) : Raft.RESTlerTypes.Engine.EngineParameters =
    {
        /// File path to the REST-ler (python) grammar.
        GrammarFilePath = grammarFilePath

        /// File path to the custom fuzzing dictionary.
        MutationsFilePath = mutationsFilePath

        /// The string to use in overriding the Host for each request
        Host =  match task.TargetConfiguration with Some tt -> tt.Host | None -> None

        /// The IP of the endpoint being fuzzed
        TargetIp = match task.TargetConfiguration with Some tt -> tt.IP | None -> None

        /// The port of the endpoint being fuzzed
        TargetPort = match task.TargetConfiguration with Some tt -> tt.Port | None -> None

        /// The maximum fuzzing time in hours
        MaxDurationHours = task.Duration |> Option.map(fun d -> d.TotalHours)

        /// The authentication options, when tokens are required
        RefreshableTokenOptions =
            match task.AuthenticationMethod with
            | Some c ->
                let authConfig : Raft.RESTlerTypes.Engine.RefreshableTokenOptions =
                    {
                        RefreshInterval = Option.defaultValue (int <| TimeSpan.FromHours(1.0).TotalSeconds) runConfiguration.AuthenticationTokenRefreshIntervalSeconds
                        RefreshCommand =
                            match c with
                            | Raft.Job.Authentication.TokenRefresh.CommandLine cmd -> cmd

                            | Raft.Job.Authentication.TokenRefresh.MSAL secret ->
                                (sprintf "dotnet /raft-tools/auth/dotnet-core-3.1/AzureAuth.dll msal --secret \"%s\" --prepend-line \"{u'user1':{}}\"" secret)

                            | Raft.Job.Authentication.TokenRefresh.TxtToken secret ->
                                (sprintf "dotnet /raft-tools/auth/dotnet-core-3.1/AzureAuth.dll token --secret \"%s\" --prepend-line \"{u'user1':{}}\"" secret)
                    }
                printfn "Refreshable token configuration : %A" authConfig
                Some authConfig
            | None -> None

        /// The delay in seconds after invoking an API that creates a new resource
        ProducerTimingDelay = Option.defaultValue 10 runConfiguration.ProducerTimingDelay

        /// The checker options
        /// ["enable or disable", "list of specified checkers"]
        CheckerOptions = checkerOptions
 
        /// Specifies to use SSL when connecting to the server
        UseSsl = match runConfiguration.UseSsl with None -> true | Some useSsl -> useSsl

        /// Path regex for filtering tested endpoints
        PathRegex = runConfiguration.PathRegex

        /// Maximum request execution time before it is considered to be timed out
        MaxRequestExecutionTime = runConfiguration.MaxRequestExecutionTime

        Checkers =
            runConfiguration.Checkers
            |> Option.map(fun checkers ->
                checkers
                |> Map.map(fun _ v -> {Mode = v.Mode})
            )

        IgnoreDependencies = runConfiguration.IgnoreDependencies
 
        IgnoreFeedback = runConfiguration.IgnoreFeedback

        IncludeUserAgent = runConfiguration.IncludeUserAgent
         
        MaxAsyncResourceCreationTime = runConfiguration.MaxAsyncResourceCreationTime
        MaxCombinations = runConfiguration.MaxCombinations
        MaxSequenceLength = runConfiguration.MaxSequenceLength
        WaitForAsyncResourceCreation = runConfiguration.WaitForAsyncResourceCreation

        PerResourceSettings =
            runConfiguration.PerResourceSettings
            |>  Option.map (fun settings ->
                settings
                |> Map.toSeq
                |> Seq.mapi(fun i (k,v) ->
                    let p = workDirectory ++ (sprintf "customDictionary-%d.json" i)
                    v |> Json.Compact.serializeToFile p
                    k,
                        ({
                            ProducerTimingDelay = v.ProducerTimingDelay
                            CreateOnce = v.CreateOnce
                            CustomDictionary = p
                        }: Raft.RESTlerTypes.Engine.EnginePerResourceSetting)
                ) |> Map.ofSeq
            )
    }

type GrammarType =
    | Swagger of string list
    | Json of string


let createRESTlerCompilerConfiguration (workDirectory: string) (grammar: GrammarType) (customDictionary: string) (compileConfig: CompileConfiguration ) : Raft.RESTlerTypes.Compiler.Config =
    {
        SwaggerSpecFilePath = match grammar with Swagger paths -> Some paths | Json _ -> None
    
        // If specified, use this as the input and generate the python grammar.
        // This is path to JSON grammar. And we might need to accept as the step to FUZZ task (but do not need it for COMPILE task)
        GrammarInputFilePath = match grammar with Json path -> Some path | Swagger _ -> None

        GrammarOutputDirectoryPath = None

        CustomDictionaryFilePath = Some customDictionary
    
        // If specified, update the engine settings with hints derived from the grammar.
        EngineSettingsFilePath = None
    
        IncludeOptionalParameters = true
    
        UseQueryExamples = true
    
        UseBodyExamples = true
    
        /// When set to 'true', discovers examples and outputs them to a directory next to the grammar.
        /// If an existing directory exists, does not over-write it.
        DiscoverExamples = true
    
        /// The directory where the compiler should look for examples.
        /// If 'discoverExamples' is true, this directory will contain the
        /// example files that have been discovered.
        /// If 'discoverExamples' is false, every time an example is used in the
        /// Swagger file, RESTler will first look for it in this directory.
        ExamplesDirectory = workDirectory ++ "Examples"
    
        /// Perform data fuzzing
        DataFuzzing = true
    
        // When true, only fuzz the GET requests
        ReadOnlyFuzz = compileConfig.ReadOnlyFuzz
    
        ResolveQueryDependencies = true
    
        ResolveBodyDependencies = true
    
        UseRefreshableToken = compileConfig.UseRefreshableToken
    
        // When true, allow GET requests to be considered.
        // This option is present for debugging, and should be
        // set to 'false' by default.
        // In limited cases when GET is a valid producer, the user
        // should add an annotation for it.
        AllowGetProducers = compileConfig.AllowGetProducers
    }

let inline makeValues< 'T > (stdGen) =
    FsCheck.Gen.eval 100 stdGen (FsCheck.Gen.arrayOfLength 10 FsCheck.Arb.generate< 'T >)

let genMutationsDictionary(rnd: int64) : Raft.RESTlerTypes.Compiler.MutationsDictionary=
    let stdGen = FsCheck.Random.mkStdGen(rnd)

    {
        restler_fuzzable_string = makeValues<FsCheck.NonEmptyString>(stdGen) |> Array.map (fun s -> s.Get)
        restler_fuzzable_int = makeValues<int>(stdGen) |> Array.map(fun n -> sprintf "%d" n)
        restler_fuzzable_number = makeValues<float>(stdGen) |> Array.map(fun n -> sprintf "%f" n)
        restler_fuzzable_bool = [|"true"; "false"|]
        restler_fuzzable_datetime = makeValues<DateTime>(stdGen) |> Array.map (fun dt -> dt.ToString("G"))
        restler_fuzzable_object = [|"{}"|]
        restler_fuzzable_uuid4 = makeValues<Guid>(stdGen) |> Array.map (fun g -> sprintf "%A" g)

        restler_custom_payload = Some (dict [])
        restler_custom_payload_uuid4_suffix = Some (dict [])
        restler_custom_payload_header = None
        shadow_values = None
    }

let predefinedMutationsDictionary : Raft.RESTlerTypes.Compiler.MutationsDictionary =
    {
        restler_fuzzable_string = [|"fuzzstring"|]
        restler_fuzzable_int = [|"0" ; "1"|]
        restler_fuzzable_number = [|"0.1"; "1.2"|]
        restler_fuzzable_bool = [|"true"|]
        restler_fuzzable_datetime = [|"6/25/2019 12:00:00 AM"|]
        restler_fuzzable_object = [|"{}"|]
        restler_fuzzable_uuid4 = [|"903bcc44-30cf-4ea7-968a-d9d0da7c072f"|]
        restler_custom_payload = Some (dict [])
        restler_custom_payload_uuid4_suffix = Some (dict [])
        restler_custom_payload_header = None
        shadow_values = None
    }

let createRESTlerCompilerMutations (fsCheckSeed: int64 option) (userConfigurableMutations: CustomDictionary option) : Raft.RESTlerTypes.Compiler.MutationsDictionary=
    let m = 
        match fsCheckSeed with
        | Some seed ->
            let m = genMutationsDictionary seed
            { m with
                restler_fuzzable_string = Array.append m.restler_fuzzable_string predefinedMutationsDictionary.restler_fuzzable_string}
        | None -> predefinedMutationsDictionary

    match userConfigurableMutations with
    | None -> m

    | Some d ->
        {
            restler_fuzzable_string =
                match d.FuzzableString with
                | None -> m.restler_fuzzable_string
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_string
                | Some xs -> xs

            restler_fuzzable_int =
                match d.FuzzableInt with
                | None -> m.restler_fuzzable_int
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_int
                | Some xs -> xs

            restler_fuzzable_number =
                match d.FuzzableNumber with
                | None -> m.restler_fuzzable_number
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_number
                | Some xs -> xs

            restler_fuzzable_bool =
                match d.FuzzableBool with
                | None -> m.restler_fuzzable_bool
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_bool
                | Some xs -> xs

            restler_fuzzable_datetime =
                match d.FuzzableDatetime with
                | None -> m.restler_fuzzable_datetime
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_datetime
                | Some xs -> xs

            restler_fuzzable_object =
                match d.FuzzableObject with
                | None -> m.restler_fuzzable_object
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_object
                | Some xs -> xs

            restler_fuzzable_uuid4 =
                match d.FuzzableUuid4 with
                | None -> m.restler_fuzzable_uuid4
                | Some [||] -> predefinedMutationsDictionary.restler_fuzzable_uuid4
                | Some xs -> xs
            
            restler_custom_payload = d.CustomPayload
            restler_custom_payload_uuid4_suffix = d.CustomPayloadUuid4Suffix
            restler_custom_payload_header = d.CustomPayloadHeader
            shadow_values = d.ShadowValues
        }

type ConsoleMulitplex (log: string -> unit, consoleTextWriter: IO.TextWriter) =
    inherit IO.TextWriter()

    let sb = Text.StringBuilder()

    override _.Write(s:string)=
        consoleTextWriter.Write(s)
        sb.Append s |> ignore

    override _.WriteLine() =
        consoleTextWriter.WriteLine()
        log(sb.ToString())
        sb.Clear() |> ignore

    override _.Encoding = Console.OutputEncoding

let multiplexConsole (workDirectory: string) (jobId: string) (agentName: string) =
    let workDirectory =
        if IO.Directory.Exists workDirectory then
            workDirectory
        else
            "."

    let stdOutToFile s =
        let p = workDirectory ++ "stdout-agent.txt"
        IO.File.AppendAllLines(p, [s])

    let stdErrToFile s =
        let p = workDirectory ++ "stderr-agent.txt"
        IO.File.AppendAllLines(p, [s])

    Console.SetOut(new ConsoleMulitplex((fun s -> stdOutToFile s), Console.Out))
    Console.SetError(new ConsoleMulitplex((fun s -> stdErrToFile s), Console.Error))


let copyDir srcDir destDir (doNotOverwriteFilePaths: string Set) =
    if srcDir = destDir then
        printfn "Source and destination is the same directory (%s). Skipping copy" srcDir
    else
        let rec doCopyDir src dest =
            let srcDir = IO.DirectoryInfo(src)
            if not srcDir.Exists then
                failwithf "Directory not found: %s" srcDir.FullName

            let destDir = IO.DirectoryInfo(dest)
            if not destDir.Exists then
                destDir.Create()

            for f in srcDir.GetFiles() do
                let destFilePath = destDir.FullName ++ f.Name
                if not (doNotOverwriteFilePaths.Contains destFilePath && IO.File.Exists destFilePath) then
                    f.CopyTo(destFilePath, true) |> ignore

            for d in srcDir.GetDirectories() do
                doCopyDir d.FullName (destDir.FullName ++ d.Name)
        doCopyDir srcDir destDir

[<EntryPoint>]
let main argv =
    printfn "Arguments: %A" argv
    let agentConfiguration = Arguments.parseCommandLine argv
    let appInsights =
        match agentConfiguration.AppInsightsInstrumentationKey with
        | None -> failwith "AppInsights configuration is not set"
        | Some instrumentationKey ->
            Microsoft.ApplicationInsights.TelemetryClient(new Extensibility.TelemetryConfiguration(instrumentationKey), InstrumentationKey = instrumentationKey)

    let jobId =
        match agentConfiguration.JobId with
        | None -> failwith "JobId is not set"
        | Some jobId -> jobId

    let agentName =
        match agentConfiguration.AgentName with
        | None -> failwith "Agent name is not set"
        | Some agentName -> agentName

    let workDirectory =
        match agentConfiguration.WorkDirectory with
        | None -> failwithf "Work directory is not set"
        | Some p -> p

    multiplexConsole workDirectory jobId agentName

    let jobEventSender =
        match agentConfiguration.JobEventTopicSAS with
        | None -> failwith "Job status connection is not set"
        | Some sas ->
            ServiceBus.Core.MessageSender(ServiceBus.ServiceBusConnectionStringBuilder(sas), ServiceBus.RetryPolicy.Default)

    async {
        let siteHash =
            match agentConfiguration.SiteHash with
            | None -> "NotSet"
            | Some h -> h
        use telemetryClient = new Restler.Telemetry.TelemetryClient(siteHash, if agentConfiguration.TelemetryOptOut then "" else Restler.Telemetry.InstrumentationKey)

        if not <| IO.Directory.Exists workDirectory then
            appInsights.TrackTrace((sprintf "Workd directory does not exist: %s" workDirectory),
                DataContracts.SeverityLevel.Error,
                dict ["jobId", jobId; "agentName", agentName])
            failwithf "Failing since work directory does not exist: %s" workDirectory

        let taskConfigurationPath = 
            match agentConfiguration.TaskConfigPath with
            | Some path -> path
            | None -> failwith "Job configuration is not set"
            

        let restlerPath =
            match agentConfiguration.RestlerPath with
            | None -> failwith "RESTler path is not set"
            | Some p -> p

        match Raft.RESTlerDriver.validateRestlerComponentsPresent restlerPath with
        | Result.Ok() -> ()
        | Result.Error err -> failwith err

        let task: Raft.Job.RaftTask = Json.Compact.deserializeFile taskConfigurationPath
        let restlerPayload : RESTlerPayload = Json.Compact.deserialize (task.ToolConfiguration.ToString())

        do! jobEventSender.SendRaftJobEvent jobId
                ({
                    AgentName = agentName
                    Metadata = None
                    Tool = "RESTler"
                    JobId = jobId
                    State = Raft.JobEvents.Created

                    Metrics = None
                    UtcEventTime = System.DateTime.UtcNow
                    Details = None
                    ResultsUrl = None
                }: Raft.JobEvents.JobStatus)

        printfn "Got job configuration message: %A" restlerPayload

        let customDictionaryPath = workDirectory ++ "customDictionary.json"
        let grammarPy = workDirectory ++ "grammar.py"
        let dictJson = workDirectory ++ "dict.json"

        let compileSwaggerGrammar(compilerConfiguration) =
            async {
                match task.TargetConfiguration with
                | None -> return failwith "Cannot perform compilation step, since TargetConfiguration is not set"
                | Some targetConfiguration ->
                    match targetConfiguration.ApiSpecifications with
                    | None -> return failwith "Cannot perform compilation step, since ApiSpecifications are not set in TargetConfiguration, i.e. Swagger Grammar location"
                    | Some apiSpecifications ->
                        let! apiSpecifications =
                            apiSpecifications
                            |> Array.map (fun apiSpecificationLocation ->
                                async {
                                    let! apiSpecification =
                                        async {
                                            if IO.File.Exists apiSpecificationLocation then
                                                return apiSpecificationLocation
                                            else 
                                                match System.Uri.TryCreate(apiSpecificationLocation, UriKind.Absolute) with
                                                | true, url ->
                                                    let! apiSpecification = downloadFile workDirectory (Array.last url.Segments) apiSpecificationLocation
                                                    printfn "Downloaded apiSpecification spec to :%s" apiSpecification
                                                    return apiSpecification
                                                | false, _ ->
                                                    return failwithf "Invalid api specification location : %s" apiSpecificationLocation

                                        }

                                    match validateJsonFile apiSpecification with
                                    | Result.Ok () -> ()
                                    | Result.Error (err) -> failwithf "File %s is not a valid JSON file due to %s" apiSpecification err

                                    return apiSpecification
                                }
                            )
                            |> Async.Sequential

                        let compilerConfig = createRESTlerCompilerConfiguration workDirectory (Swagger (apiSpecifications |> Array.toList)) customDictionaryPath compilerConfiguration
                        do! Raft.RESTlerDriver.compile restlerPath workDirectory compilerConfig
            }

        let compileJsonGrammar (jsonGrammarPath: string) (compilerConfiguration) =
            async {
                match validateJsonFile jsonGrammarPath with
                | Result.Ok() -> ()
                | Result.Error (err) -> 
                    failwithf "File %s is not a valid JSON file due to %s (make sure that you set CompileFolder path parameter in config JSON)" jsonGrammarPath err

                let compilerConfig = createRESTlerCompilerConfiguration workDirectory (Json jsonGrammarPath) customDictionaryPath compilerConfiguration
                do! Raft.RESTlerDriver.compile restlerPath workDirectory compilerConfig
            }

        let report state (experiment : string option, summary:Raft.JobEvents.RunSummary option) =
            async {
                printfn "Reporting summary [%A]: %A" state summary
                let! bugsList = Raft.RESTlerDriver.getListOfBugs workDirectory globalRunStartTime
                let bugsListLen = match bugsList with None -> 0 | Some xs -> Seq.length xs

                let details =
                    match experiment with
                    | Some e -> Map.empty.Add("Experiment", e)
                    | None -> Map.empty

                do! jobEventSender.SendRaftJobEvent jobId
                                                ({
                                                    AgentName = agentName
                                                    Metadata = None
                                                    Tool = "RESTler"
                                                    JobId = jobId
                                                    State = state

                                                    Metrics = summary
                                                    UtcEventTime = System.DateTime.UtcNow
                                                    Details = Some( details.Add("numberOfBugsFound", sprintf "%d" bugsListLen))
                                                    ResultsUrl = None
                                                } : Raft.JobEvents.JobStatus)
            }

        let onBugFound (bugDetails : Map<string, string>) =
            async {
                let bugDetails = 
                    bugDetails.Add("jobId", jobId).Add("outputFolder", task.OutputFolder)

                printfn "OnBugFound %A" bugDetails
                do! jobEventSender.SendRaftJobEvent jobId
                                        ({
                                            Tool = "RESTler"
                                            JobId = jobId
                                            AgentName = agentName
                                            Metadata = None
                                            BugDetails = Some bugDetails
                                            ResultsUrl = None
                                        } : Raft.JobEvents.BugFound)
            }

        let getResultReportingInterval() =
            let resultAnalyzerReportInterval =
                let defaultInterval = TimeSpan.FromSeconds(60.0)
                match restlerPayload.AgentConfiguration with
                | Some agentConfig ->
                    match agentConfig.ResultsAnalyzerReportTimeSpanInterval, task.Duration with
                    | None, _ ->
                        printfn "Result analyzer interval is not set, using default"
                        Some defaultInterval
                    | Some ts, Some duration when ts >= duration ->
                        printfn "Result analyzer interval (%A) is longer than job duration (%A). Using this as not set" ts duration
                        None
                    | Some ts, _ ->
                        printfn "Result analyzer interval is set to %A" ts
                        Some ts
                | None -> Some defaultInterval
            resultAnalyzerReportInterval

        let test (testType: string) checkerOptions (jobConfiguration: RunConfiguration) =
            async {
                let resultAnalyzerReportInterval = getResultReportingInterval()
                let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task checkerOptions jobConfiguration
                printfn "Starting RESTler test task"

                let ignoreBugHashes =
                    match jobConfiguration.IgnoreBugHashes with
                    | None -> Set.empty
                    | Some hs -> Set.ofArray hs

                do! Raft.RESTlerDriver.test testType restlerPath workDirectory engineParameters ignoreBugHashes onBugFound (report Raft.JobEvents.JobState.Running) (globalRunStartTime, resultAnalyzerReportInterval)
            }

        let fuzz (fuzzType:string) checkerOptions (jobConfiguration: RunConfiguration) =
            async {
                let resultAnalyzerReportInterval = getResultReportingInterval()
                let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task checkerOptions jobConfiguration
                printfn "Starting RESTler fuzz task"

                let ignoreBugHashes =
                    match jobConfiguration.IgnoreBugHashes with
                    | None -> Set.empty
                    | Some hs -> Set.ofArray hs

                do! Raft.RESTlerDriver.fuzz fuzzType restlerPath workDirectory engineParameters ignoreBugHashes onBugFound (report Raft.JobEvents.JobState.Running) (globalRunStartTime, resultAnalyzerReportInterval)
            }
            
        let replay replayLogFile (jobConfiguration: RunConfiguration) =
            async {
                //same command line parameters as fuzzing, except for fuzzing parameter pass sprintf "--replay_log %s" replayLogFilePath
                let task =
                    match task.TargetConfiguration with
                    | None ->
                        let taskConfigurationPath = jobConfiguration.InputFolderPath ++ ".." ++ ".." ++ ".." ++ IO.FileInfo(taskConfigurationPath).Name
                        let prevTask: Raft.Job.RaftTask = Json.Compact.deserializeFile taskConfigurationPath
                        {
                            task with
                                TargetConfiguration = prevTask.TargetConfiguration
                        }

                    | _ -> task

                let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task [] jobConfiguration
                printfn "Starting RESTler replay task"
                return! Raft.RESTlerDriver.replay restlerPath workDirectory replayLogFile engineParameters
            }

        let executionId = Guid.NewGuid()
        telemetryClient.RestlerStarted(Raft.RESTlerDriver.RESTler.version, sprintf "%A" restlerPayload.Task, executionId, [])
        // Start RESTler process with following flags
        // compile, test, fuzz 
        // --telemetryRootDirPath
        let! state, details, exitCode, (experiment, summary) =
            async {
                try
                    match restlerPayload.Task with
                    | TaskType.Compile ->
                        let compileConfiguration = 
                                match restlerPayload.CompileConfiguration with
                                | Some compileConfiguration -> compileConfiguration
                                | None ->
                                          // Return a default compilerConfiguration type
                                          { InputJsonGrammarPath = None 
                                            InputFolderPath = None
                                            ReadOnlyFuzz = false
                                            AllowGetProducers = false
                                            UseRefreshableToken = false
                                            MutationsSeed = None
                                            CustomDictionary = None }

                        let restlerCompatibleMutations = 
                            createRESTlerCompilerMutations compileConfiguration.MutationsSeed compileConfiguration.CustomDictionary

                        restlerCompatibleMutations |> Json.Compact.serializeToFile customDictionaryPath

                        match compileConfiguration.InputFolderPath with
                        | Some path -> copyDir path workDirectory (set [taskConfigurationPath])
                        | None -> ()

                        match compileConfiguration.InputJsonGrammarPath with
                        | None -> do! compileSwaggerGrammar compileConfiguration
                        | Some p -> do! compileJsonGrammar (workDirectory ++ p) compileConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.Completed, None, 0, summary

                    | TaskType.Test ->
                        printfn "Running directed-smoke-test"
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set for Test task for job payload: %A" restlerPayload
                        | Some jobConfiguration ->
                            copyDir jobConfiguration.InputFolderPath workDirectory (set [taskConfigurationPath])
                            do! report Raft.JobEvents.Running (None, None)
                            do! test "directed-smoke-test" [] jobConfiguration
                            let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                            return Raft.JobEvents.Completed, None, 0, summary
        
                    | TaskType.TestFuzzLean ->
                        printfn "Running directed-smoke-test"
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set for Test task for job payload: %A" restlerPayload
                        | Some jobConfiguration ->
                            copyDir jobConfiguration.InputFolderPath workDirectory (set [taskConfigurationPath])
                            do! report Raft.JobEvents.Running (None, None)
                            let fuzzLeanCheckers = 
                                [
                                    ("--enable_checkers", "*")
                                    ("--disable_checkers", "namespacerule")
                                ]
                            do! test "directed-smoke-test" fuzzLeanCheckers jobConfiguration
                            let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                            return Raft.JobEvents.Completed, None, 0, summary

                    | TaskType.Fuzz ->
                        printfn "Running bfs fuzz"
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set for Compile task for job payload: %A" restlerPayload
                        | Some jobConfiguration ->
                            copyDir jobConfiguration.InputFolderPath workDirectory (set [taskConfigurationPath])
                            do! report Raft.JobEvents.Running (None, None)
                            let allCheckers = ["--enable_checkers", "*"]
                            do! fuzz "bfs" allCheckers jobConfiguration
                            let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                            return Raft.JobEvents.Completed, None, 0, summary

                    | TaskType.FuzzBfsCheap ->
                        printfn "Running bfs-cheap fuzz"
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set for Test task for job payload: %A" restlerPayload
                        | Some jobConfiguration ->
                            copyDir jobConfiguration.InputFolderPath workDirectory (set [taskConfigurationPath])
                            do! report Raft.JobEvents.Running (None, None)
                            let allCheckers = ["--enable_checkers", "*"]
                            do! fuzz "bfs-cheap" allCheckers jobConfiguration
                            let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                            return Raft.JobEvents.Completed, None, 0, summary

                    | TaskType.FuzzRandomWalk ->
                        printfn "Running random-walk fuzz"
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set for Fuzz task for job payload: %A" restlerPayload
                        | Some jobConfiguration ->
                            copyDir jobConfiguration.InputFolderPath workDirectory (set [taskConfigurationPath])
                            do! report Raft.JobEvents.Running (None, None)
                            let allCheckers = ["--enable_checkers", "*"]
                            do! fuzz "random-walk" allCheckers jobConfiguration
                            let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                            return Raft.JobEvents.Completed, None, 0, summary

                    | TaskType.Replay ->
                        match restlerPayload.RunConfiguration with
                        | None -> return failwithf "Job-run configuration is not set Replay task for job payload: %A" restlerPayload
                        | Some replayRunConfiguration ->
                            let replaySourcePath = replayRunConfiguration.InputFolderPath
                            do! report Raft.JobEvents.Running (None, None)

                            let replayAndReport (bugs: IO.FileInfo seq) =
                                async {
                                    let! existingBugBuckets = Raft.RESTlerDriver.getListOfBugsFromBugBuckets replayRunConfiguration.InputFolderPath

                                    let remapBugHashes (bugHashes : Raft.RESTlerTypes.Logs.BugHashes) =
                                        bugHashes
                                        |> Seq.map(fun (KeyValue(k, v)) -> v.file_path, k)
                                        |> Map.ofSeq

                                    let bugs, fileNameToBugHashMap =
                                        match replayRunConfiguration.IgnoreBugHashes, existingBugBuckets with
                                        | None, None | Some _, None -> bugs, Map.empty
                                         | None, Some existingBugs ->
                                            bugs, (remapBugHashes existingBugs)

                                        | Some ignoreHashes, Some existingBugs ->
                                            let filesToIgnore = 
                                                ignoreHashes 
                                                |> Array.map(fun h ->
                                                    match Map.tryFind h existingBugs with
                                                    | None -> None
                                                    | Some b -> Some b.file_path
                                                )
                                                |> Array.filter Option.isSome
                                                |> Array.map Option.get
                                                |> Set.ofArray

                                            let filteredBugs =
                                                bugs
                                                |> Seq.filter (fun b ->
                                                    not (filesToIgnore.Contains b.Name)
                                                )
                                            filteredBugs, (remapBugHashes existingBugs)

                                    let replaySummaryDetails = ResizeArray<string * string>()

                                    for bug in bugs do
                                        printfn "Running replay on %s" bug.FullName
                                        let! (experiment, replaySummary) = replay bug.FullName replayRunConfiguration
                                        let summary =
                                            match replaySummary with
                                            | None ->
                                                sprintf "%s : No results" bug.Name
                                            | Some s ->
                                                sprintf "%s/%s: %A" (Option.defaultValue "ExperimentNotSet" experiment) bug.Name (s.ResponseCodeCounts |> Map.toList)

                                        let bugKey =
                                            match Map.tryFind bug.Name fileNameToBugHashMap with
                                            | None -> bug.Name
                                            | Some h -> h
                                        replaySummaryDetails.Add(bugKey, summary)
                                        do! jobEventSender.SendRaftJobEvent jobId
                                                    ({
                                                        AgentName = agentName
                                                        Metadata = None
                                                        Tool = "RESTler"
                                                        JobId = jobId
                                                        State = Raft.JobEvents.JobState.Running
                                                        Metrics = None
                                                        UtcEventTime = System.DateTime.UtcNow
                                                        Details = Some (Map.ofSeq replaySummaryDetails)
                                                        ResultsUrl = None
                                                    } : Raft.JobEvents.JobStatus)

                                    return replaySummaryDetails
                                }

                            let replayAll () =
                                async {
                                    let bugBuckets = IO.DirectoryInfo(replaySourcePath)
                                    if bugBuckets.Exists then
                                        let! details = replayAndReport (bugBuckets.EnumerateFiles() |> Seq.filter Raft.RESTlerDriver.isBugFile)
                                        return Some details
                                    else
                                        printfn "ReplayAll bugBuckets folder does not exist %s" bugBuckets.FullName
                                        return None
                                }
                            let! details =
                                match restlerPayload.ReplayConfiguration with
                                | None -> replayAll()
                                | Some replayConfiguration ->
                                    async {
                                        match replayConfiguration.BugBuckets with
                                        | Some paths ->
                                            let details = ResizeArray()
                                            for path in paths do
                                                let fullPath = replaySourcePath ++ path
                                                printfn "Full path: %s" fullPath
                                                let file = IO.FileInfo(fullPath)
                                                if file.Exists then
                                                    printfn "%s is a file, running replay..." fullPath
                                                    let! d = replayAndReport [file]
                                                    details.AddRange d
                                                else
                                                    printfn "Replay bugBuckets file does not exist %s" file.FullName
                                                    failwithf "Cannot run replay since could not find %s" fullPath
                                            return Some details
                                        | None -> return! replayAll()
                                    }
                            return Raft.JobEvents.Completed, (details |> Option.map Map.ofSeq), 0, (None, None)
                with
                | ex ->
                    printfn "%A" ex
                    let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                    return Raft.JobEvents.Error, (Some (Map.empty.Add("Error",  ex.Message))), 1, summary
            }

        let! bugsList = Raft.RESTlerDriver.getListOfBugs workDirectory globalRunStartTime
        let bugsListLen = match bugsList with None -> 0 | Some xs -> Seq.length xs

        let testingSummary = Raft.RESTlerDriver.loadTestRunSummary workDirectory globalRunStartTime

        let details =
            let d = Option.defaultValue Map.empty details
            match experiment with
            | Some e -> Some (d.Add("Experiment", e))
            | None -> Some d

        let details =
            match testingSummary with
            | None -> details
            | Some status ->
                let d = Option.defaultValue Map.empty details
                Some( d
                        .Add("finalSpecCoverage", status.final_spec_coverage)
                        .Add("numberOfBugsFound", sprintf "%d" bugsListLen)
                        //.Add("renderedRequests", status.rendered_requests)
                        //.Add("renderedRequestsValidStatus", status.rendered_requests_valid_status)
                        //.Add("numFullyValid", sprintf "%d" status.num_fully_valid)
                        //.Add("numSequenceFailures", sprintf "%d" status.num_sequence_failures)
                        //.Add("numInvalidByFailedResourceCreations", sprintf "%d" status.num_invalid_by_failed_resource_creations)
                        //.Add("totalObjectCreations", sprintf "%d" status.total_object_creations)
                )

        printfn "Sending final event: %A with summary: %A and details %A" state summary details
        do! jobEventSender.SendRaftJobEvent jobId
                    ({
                        AgentName = agentName
                        Metadata = None
                        Tool = "RESTler"
                        JobId = jobId
                        State = state
                        Metrics = summary
                        UtcEventTime = System.DateTime.UtcNow
                        Details = details
                        ResultsUrl = None
                    } : Raft.JobEvents.JobStatus)

        let restlerTelemetry = Restler.Telemetry.getDataFromTestingSummary testingSummary
        telemetryClient.RestlerFinished(
            Raft.RESTlerDriver.RESTler.version,
            sprintf "%A" restlerPayload.Task,
            executionId,
            sprintf "%A" state,
            restlerTelemetry.specCoverageCounts,
            restlerTelemetry.bugBucketCounts)
        return exitCode
    } 
    |> Async.Catch
    |> fun result -> 
        async {
            let! res =
                async {
                    match! result with
                    | Choice1Of2 r ->
                        return r
                    | Choice2Of2 ex ->
                        eprintf "Raft.Agent failed due to: %A" ex
                        appInsights.TrackException(ex, dict ["JobId", sprintf "%A" jobId; "AgentName", agentName])

                        do! jobEventSender.SendRaftJobEvent jobId
                                ({
                                    AgentName = agentName
                                    Metadata = None
                                    Tool = "RESTler"
                                    JobId = jobId
                                    State = Raft.JobEvents.Error
                                    Metrics = None
                                    UtcEventTime = System.DateTime.UtcNow
                                    Details = Some (Map.empty.Add("Error", ex.Message))
                                    ResultsUrl = None
                                } : Raft.JobEvents.JobStatus)

                        do! System.Console.Error.FlushAsync().ToAsync
                        do! System.Console.Out.FlushAsync().ToAsync
                        return 2
                }
            appInsights.Flush()
            do! jobEventSender.CloseAsync().ToAsync
            do! Console.Out.FlushAsync() |> Async.AwaitTask
            do! Console.Error.FlushAsync() |> Async.AwaitTask
            return res
        }
    |> Async.RunSynchronously
