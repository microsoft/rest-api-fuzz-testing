// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
module RESTlerAgent

open System
open Newtonsoft
open Microsoft.FSharpLu
open RESTlerAgentConfigTypes
open System.Collections.Generic

let inline (++) (path1: string) (path2 : string) = IO.Path.Join(path1, path2)

type System.Threading.Tasks.Task with
    member x.ToAsync = Async.AwaitTask(x)

type System.Threading.Tasks.Task<'T> with
    member x.ToAsync = Async.AwaitTask(x)

let globalRunStartTime = DateTime.UtcNow


type RaftAgentUtilities(agentUtilitiesUrl : System.Uri) =
    let httpClient = new System.Net.Http.HttpClient(BaseAddress = agentUtilitiesUrl)

    member _.WaitForUtilitiesToBeReady() =
        let rec wait() =
            async {
                try
                    let! r = httpClient.GetAsync("/readiness/ready").ToAsync
                    if r.IsSuccessStatusCode then
                        return ()
                    else
                        do! Async.Sleep(1000)
                        return! wait()
                with
                | ex ->
                    printfn "Trying again since connection failed due to : %A" ex.Message
                    do! Async.Sleep(10000)
                    return! wait()
            }
        wait()

    member _.SendJobStatus (jobStatus : Raft.JobEvents.JobStatus) =
        async {
            use s = new System.Net.Http.StringContent(Json.Compact.Strict.serialize jobStatus)
            let! _ = httpClient.PostAsync("/messaging/event/jobStatus", s).ToAsync
            return ()
        }

    member _.SendBugFound(bugFound: Raft.JobEvents.BugFound) =
        async {
            use s = new System.Net.Http.StringContent(Json.Compact.Strict.serialize bugFound)
            let! _ = httpClient.PostAsync("/messaging/event/bugFound", s).ToAsync
            return ()
        }

    member _.Flush() =
        async {
            use empty = new System.Net.Http.StringContent("")
            let! _ = httpClient.PostAsync("/messaging/flush", empty).ToAsync
            return ()
        }

    member _.Trace(message: string, severity: string, tags: IDictionary<string, string>) =
        async {
            use trace =
                let json =
                    {|Message = message; Severity=severity; Tags=tags|}
                    |> Json.Compact.Strict.serialize
                new System.Net.Http.StringContent(json)

            let! _ = httpClient.PostAsync("/messaging/trace", trace).ToAsync
            return ()
        }

    interface IDisposable with
        member _.Dispose() =
            httpClient.Dispose()


module Arguments =

    type AgentArguments =
        {
            AgentName: string option
            JobId: string option
            TaskConfigPath : string option

            AgentUtilitiesUrl: System.Uri option
            RestlerPath: string option
            WorkDirectory: string option

            SiteHash : string option
            TelemetryOptOut : bool
        }

        static member Empty =
            {
                AgentName = None
                JobId = None
                TaskConfigPath = None
                AgentUtilitiesUrl = None
                RestlerPath = None
                WorkDirectory = None

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
    let [<Literal>] AgentUtilsUrl = "--agent-utilities-url"
    let [<Literal>] RESTlerPath = "--restler-path"
    let [<Literal>] WorkDirectory = "--work-directory"

    let usage =
        [
            sprintf "%s <Agent name to use for this task when reporting status>" AgentName
            sprintf "%s <String identifying the job>" JobId
            sprintf "%s <Path to the task config file>" TaskConfigFilePath
            sprintf "%s <URL to agent utilities service container>" AgentUtilsUrl
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

            | AgentUtilsUrl :: url :: rest ->
                printfn "Agent Utilities URL:: %s" url
                parse { currentConfig with AgentUtilitiesUrl = Some (System.Uri(url)) } rest

            | RESTlerPath :: path :: rest ->
                printfn "Path to RESTler : %s" path
                parse { currentConfig with RestlerPath = Some path } rest

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



let installCertificatesIfNeeded (task: Raft.Job.RaftTask) =
    async {
        match task.TargetConfiguration with
        | Some tt ->
            match tt.Certificates with
            | Some certs ->
                for c in certs do
                    do! Raft.RESTlerDriver.prepCertificates c
                do! Raft.RESTlerDriver.updateCaCertificates()
            | None -> ()
        | None -> ()
    
    }

// Need this for TEST and FUZZ tasks
let createRESTlerEngineParameters
    (workDirectory: string)
    (grammarFilePath: string) (mutationsFilePath: string)
    (task: Raft.Job.RaftTask) (checkerOptions:(string*string) list)
    (runConfiguration: RunConfiguration) (authenicationUrl: System.Uri) : Raft.RESTlerTypes.Engine.EngineParameters =
    
    let host, ip, port =
        match task.TargetConfiguration with
        | None -> None, None, None
        | Some targetConfig ->
            match targetConfig.Endpoint with
            | None -> None, None, None
            | Some endpoint ->
                match endpoint.HostNameType with
                | System.UriHostNameType.Dns ->
                    Some(endpoint.Host), None, Some(endpoint.Port)
                | _ -> None, Some(endpoint.Host), Some(endpoint.Port)

    {
        /// File path to the REST-ler (python) grammar.
        GrammarFilePath = grammarFilePath

        /// File path to the custom fuzzing dictionary.
        MutationsFilePath = mutationsFilePath

        /// The string to use in overriding the Host for each request
        Host =  host

        /// The IP of the endpoint being fuzzed
        TargetIp = ip

        /// The port of the endpoint being fuzzed
        TargetPort = port

        /// The maximum fuzzing time in hours
        MaxDurationHours = (Option.orElse runConfiguration.Duration task.Duration) |> Option.map(fun d -> d.TotalHours)

        /// The authentication options, when tokens are required
        RefreshableTokenOptions =
            match task.AuthenticationMethod with
            | Some c ->
                if c.IsEmpty then
                    None
                else
                    let authConfig : Raft.RESTlerTypes.Engine.RefreshableTokenOptions =
                        {
                            RefreshInterval = Option.defaultValue (int <| TimeSpan.FromHours(1.0).TotalSeconds) runConfiguration.AuthenticationTokenRefreshIntervalSeconds
                            RefreshExec = "python3"
                            RefreshArgs =
                                let url =
                                    let auth = Seq.head c
                                    let authType, authSecretName = auth.Key, auth.Value
                                    System.Uri(authenicationUrl, sprintf "auth/%s/%s" authType authSecretName)
                                sprintf """-c "import requests; import json; r=requests.get('%s'); assert r.ok, r.text; print(\"{u'user1':{}}\nAuthorization: \" + json.loads(r.text)['token'])"  """ url.AbsoluteUri
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
    
        IncludeOptionalParameters = compileConfig.IncludeOptionalParameters
    
        UseQueryExamples = compileConfig.UseQueryExamples
    
        UseBodyExamples = compileConfig.UseBodyExamples
    
        /// When set to 'true', discovers examples and outputs them to a directory next to the grammar.
        /// If an existing directory exists, does not over-write it.
        DiscoverExamples = compileConfig.DiscoverExamples
    
        /// The directory where the compiler should look for examples.
        /// If 'discoverExamples' is true, this directory will contain the
        /// example files that have been discovered.
        /// If 'discoverExamples' is false, every time an example is used in the
        /// Swagger file, RESTler will first look for it in this directory.
        ExamplesDirectory = match compileConfig.ExamplesDirectory with None -> workDirectory ++ "Examples" | Some d -> d

        ExampleConfigFilePath = compileConfig.ExampleConfigFilePath
    
        /// Perform data fuzzing
        DataFuzzing = compileConfig.DataFuzzing
    
        // When true, only fuzz the GET requests
        ReadOnlyFuzz = compileConfig.ReadOnlyFuzz
    
        ResolveQueryDependencies = compileConfig.ResolveQueryDependencies
    
        ResolveBodyDependencies = compileConfig.ResolveBodyDependencies
    
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

    let blns = "/raft-tools/libs/blns/blns.json"
    let strings =
        if IO.File.Exists blns then
            let allStrings = Json.Default.deserializeFile<string array> blns
            let indices = FsCheck.Gen.eval 100 stdGen (FsCheck.Gen.arrayOfLength 10 (FsCheck.Gen.choose(0, allStrings.Length - 1)))
            indices |> Array.map (fun i -> allStrings.[i])
        else
            makeValues<FsCheck.NonEmptyString>(stdGen) |> Array.map (fun s -> s.Get)

    {
        restler_fuzzable_string = strings
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

    let agentUtilitiesUrl =
        match agentConfiguration.AgentUtilitiesUrl with
        | None -> failwith "Agent Utilities URL is not set"
        | Some url -> url

    use agentUtilities = new RaftAgentUtilities(agentUtilitiesUrl)

    async {
        let siteHash =
            match agentConfiguration.SiteHash with
            | None -> "NotSet"
            | Some h -> h


        let telemetryTag = 
            match System.Environment.GetEnvironmentVariable("RAFT_LOCAL") |> Option.ofObj with
            | None -> "RAFT"
            | Some t -> sprintf "RAFT-LOCAL(%s)" t
        use telemetryClient = new Restler.Telemetry.TelemetryClient(siteHash, (if agentConfiguration.TelemetryOptOut then "" else Restler.Telemetry.InstrumentationKey), telemetryTag)

        if not <| IO.Directory.Exists workDirectory then
            do! agentUtilities.Trace((sprintf "Work directory does not exist: %s" workDirectory), "Error", dict ["jobId", jobId; "agentName", agentName])
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
        
        let restlerPayloads : RESTlerPayload array =
            match Json.Compact.tryDeserialize (task.ToolConfiguration.ToString()) with
            | Choice1Of2 p -> [|p|]
            | Choice2Of2(_)->
                let payloads: RESTlerPayloads = Json.Compact.deserialize (task.ToolConfiguration.ToString())
                payloads.tasks


        do! agentUtilities.WaitForUtilitiesToBeReady()

        do! agentUtilities.SendJobStatus
                {
                    AgentName = agentName
                    Metadata = None
                    Tool = "RESTler"
                    JobId = jobId
                    State = Raft.JobEvents.Created

                    Metrics = None
                    UtcEventTime = System.DateTime.UtcNow
                    Details = None
                    ResultsUrl = None
                }

        printfn "Got job configuration message: %A" restlerPayloads

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
                                                    let fileName = 
                                                        let fn = Array.last url.Segments
                                                        if List.contains (IO.FileInfo(fn).Extension.ToLower()) [".json"; ".yml"; ".yaml"] then
                                                            fn
                                                        else
                                                            fn + ".json"

                                                    printfn "Downloading API specifications from %s" apiSpecificationLocation
                                                    let! apiSpecification = downloadFile workDirectory fileName apiSpecificationLocation
                                                    printfn "Downloaded apiSpecification spec to :%s" apiSpecification
                                                    return apiSpecification
                                                | false, _ ->
                                                    return failwithf "Invalid api specification location : %s" apiSpecificationLocation

                                        }

                                    if IO.FileInfo(apiSpecification).Extension.ToLower() = ".json" then
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

        let onBugFound (bugDetails : Map<string, string>) =
            async {
                let bugDetails = 
                    bugDetails.Add("jobId", jobId).Add("outputFolder", task.OutputFolder)

                printfn "OnBugFound %A" bugDetails
                do! agentUtilities.SendBugFound
                                        {
                                            Tool = "RESTler"
                                            JobId = jobId
                                            AgentName = agentName
                                            Metadata = None
                                            BugDetails = Some bugDetails
                                            ResultsUrl = None
                                        }
            }
        

        let combinedMetrics = ref Raft.JobEvents.RunSummary.Empty
        let taskIndex = ref 0
        for restlerPayload in restlerPayloads do
            // execution id is used for RESTler specific metrics only
            // Each loop iteration represents a RESTler run. Thus requires
            // new execution ID
            let executionId = Guid.NewGuid()
            let agentName = sprintf "%s:%d" agentName (!taskIndex)

            let report state (experiment : string option, summary:Raft.JobEvents.RunSummary option) =
                async {
                    printfn "Reporting summary [%A]: %A" state summary
                    let! bugsList = Raft.RESTlerDriver.getListOfBugs workDirectory globalRunStartTime
                    let bugsListLen = match bugsList with None -> 0 | Some xs -> Seq.length xs

                    let details =
                        match experiment with
                        | Some e -> Map.empty.Add("Experiment", e)
                        | None -> Map.empty

                    do! agentUtilities.SendJobStatus
                                                {
                                                    AgentName = agentName
                                                    Metadata = None
                                                    Tool = "RESTler"
                                                    JobId = jobId
                                                    State = state

                                                    Metrics = summary
                                                    UtcEventTime = System.DateTime.UtcNow
                                                    Details = Some( details.Add("numberOfBugsFound", sprintf "%d" bugsListLen))
                                                    ResultsUrl = None
                                                }
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
                    do! installCertificatesIfNeeded task
                    let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task checkerOptions jobConfiguration agentUtilitiesUrl
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
                    do! installCertificatesIfNeeded task
                    let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task checkerOptions jobConfiguration agentUtilitiesUrl
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
                            let taskConfigurationPath =
                                match jobConfiguration.InputFolderPath with
                                | None -> workDirectory ++ IO.FileInfo(taskConfigurationPath).Name
                                | Some p -> p ++ ".." ++ ".." ++ ".." ++ IO.FileInfo(taskConfigurationPath).Name

                            let prevTask: Raft.Job.RaftTask = Json.Compact.deserializeFile taskConfigurationPath
                            {
                                task with
                                    TargetConfiguration = prevTask.TargetConfiguration
                            }
                        | _ -> task

                    do! installCertificatesIfNeeded task
                    let engineParameters = createRESTlerEngineParameters workDirectory grammarPy dictJson task [] jobConfiguration agentUtilitiesUrl
                    printfn "Starting RESTler replay task"
                    return! Raft.RESTlerDriver.replay restlerPath workDirectory replayLogFile engineParameters
                }

        
            telemetryClient.RestlerStarted(Raft.RESTlerDriver.RESTler.version, sprintf "%A" restlerPayload.Task, executionId, [])
            let restlerTask =
                match restlerPayload.Task with
                | Some t -> t
                | None -> failwithf "RESTler task is not set. "


            // Start RESTler process with following flags
            // compile, test, fuzz 
            // --telemetryRootDirPath
            let! state, details, exitCode, (experiment, summary) =
                async {
                    match restlerTask with
                    | TaskType.Compile ->
                        let compileConfiguration = 
                            match restlerPayload.CompileConfiguration with
                            | Some compileConfiguration -> compileConfiguration
                            | None -> CompileConfiguration.Empty

                        let restlerCompatibleMutations = 
                            createRESTlerCompilerMutations compileConfiguration.MutationsSeed compileConfiguration.CustomDictionary

                        printfn "Saving custom mutations to : %s" customDictionaryPath
                        restlerCompatibleMutations |> Json.Compact.serializeToFile customDictionaryPath

                        match compileConfiguration.InputFolderPath with
                        | Some path -> copyDir path workDirectory (set [taskConfigurationPath])
                        | None -> ()

                        match compileConfiguration.InputJsonGrammarPath with
                        | None -> do! compileSwaggerGrammar compileConfiguration
                        | Some p -> do! compileJsonGrammar (workDirectory ++ p) compileConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary

                    | TaskType.Test ->
                        printfn "Running directed-smoke-test"
                        let jobConfiguration =
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some jobConfiguration -> jobConfiguration
                        match jobConfiguration.InputFolderPath with
                        | Some p -> copyDir p workDirectory (set [taskConfigurationPath])
                        | None -> ()
                        do! report Raft.JobEvents.Running (None, None)
                        do! test "directed-smoke-test" [] jobConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary
        
                    | TaskType.TestFuzzLean ->
                        printfn "Running directed-smoke-test"
                        let jobConfiguration =
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some jobConfiguration -> jobConfiguration
                        match jobConfiguration.InputFolderPath with
                        | Some p -> copyDir p workDirectory (set [taskConfigurationPath])
                        | None -> ()
                        do! report Raft.JobEvents.Running (None, None)
                        let fuzzLeanCheckers = 
                            [
                                ("--enable_checkers", "*")
                                ("--disable_checkers", "namespacerule")
                            ]
                        do! test "directed-smoke-test" fuzzLeanCheckers jobConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary

                    | TaskType.Fuzz ->
                        printfn "Running bfs fuzz"
                        let jobConfiguration =
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some jobConfiguration -> jobConfiguration

                        match jobConfiguration.InputFolderPath with
                        | Some p -> copyDir p workDirectory (set [taskConfigurationPath])
                        | None -> ()
                        do! report Raft.JobEvents.Running (None, None)
                        let allCheckers = ["--enable_checkers", "*"]
                        do! fuzz "bfs" allCheckers jobConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary

                    | TaskType.FuzzBfsCheap ->
                        printfn "Running bfs-cheap fuzz"
                        let jobConfiguration = 
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some jobConfiguration -> jobConfiguration

                        match jobConfiguration.InputFolderPath with
                        | Some p -> copyDir p workDirectory (set [taskConfigurationPath])
                        | None -> ()
                        do! report Raft.JobEvents.Running (None, None)
                        let allCheckers = ["--enable_checkers", "*"]
                        do! fuzz "bfs-cheap" allCheckers jobConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary

                    | TaskType.FuzzRandomWalk ->
                        printfn "Running random-walk fuzz"
                        let jobConfiguration =
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some jobConfiguration -> jobConfiguration
                        match jobConfiguration.InputFolderPath with
                        | Some p ->  copyDir p workDirectory (set [taskConfigurationPath])
                        | None -> ()
                        do! report Raft.JobEvents.Running (None, None)
                        let allCheckers = ["--enable_checkers", "*"]
                        do! fuzz "random-walk" allCheckers jobConfiguration
                        let! summary = Raft.RESTlerDriver.processRunSummary workDirectory globalRunStartTime
                        return Raft.JobEvents.TaskCompleted, None, 0, summary

                    | TaskType.Replay ->
                        let replayRunConfiguration =
                            match restlerPayload.RunConfiguration with
                            | None -> RunConfiguration.Empty
                            | Some replayRunConfiguration -> replayRunConfiguration

                        let replaySourcePath = 
                            match replayRunConfiguration.InputFolderPath with
                            | Some p -> p
                            | None -> failwithf "Do not know where to replay bugs from"
                        do! report Raft.JobEvents.Running (None, None)

                        let replayAndReport (bugs: IO.FileInfo seq) =
                            async {
                                let! existingBugBuckets = Raft.RESTlerDriver.getListOfBugsFromBugBuckets replaySourcePath

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
                                    do! agentUtilities.SendJobStatus
                                                {
                                                    AgentName = agentName
                                                    Metadata = None
                                                    Tool = "RESTler"
                                                    JobId = jobId
                                                    State = Raft.JobEvents.JobState.Running
                                                    Metrics = None
                                                    UtcEventTime = System.DateTime.UtcNow
                                                    Details = Some (Map.ofSeq replaySummaryDetails)
                                                    ResultsUrl = None
                                                }

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
                        return Raft.JobEvents.TaskCompleted, (details |> Option.map Map.ofSeq), 0, (None, None)
                }

            let! bugsList = Raft.RESTlerDriver.getListOfBugs workDirectory globalRunStartTime
            let bugsListLen = match bugsList with None -> 0 | Some xs -> Seq.length xs

            let testingSummary = Raft.RESTlerDriver.loadTestRunSummary workDirectory globalRunStartTime

            let details =
                let d = (Option.defaultValue Map.empty details).Add("TaskType", sprintf "%A" restlerTask)
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

            match summary with
            | Some s -> combinedMetrics := Raft.JobEvents.RunSummary.Combine(!combinedMetrics, s)
            | None -> ()

            do! agentUtilities.SendJobStatus
                        {
                            AgentName = agentName
                            Metadata = None
                            Tool = "RESTler"
                            JobId = jobId
                            State = state
                            Metrics = summary
                            UtcEventTime = System.DateTime.UtcNow
                            Details = details
                            ResultsUrl = None
                        }


            // Sending RESTler metrics event as "completed", since from RESTler point of view RESTler run is finished.
            // From RAFT point of view job is till running, so do not send RAFT Completed event yet
            let restlerTelemetry = Restler.Telemetry.getDataFromTestingSummary testingSummary
            telemetryClient.RestlerFinished(
                Raft.RESTlerDriver.RESTler.version,
                sprintf "%A" restlerPayload.Task,
                executionId,
                sprintf "%A" Raft.JobEvents.JobState.Completed,
                restlerTelemetry.specCoverageCounts,
                restlerTelemetry.bugBucketCounts)

            incr taskIndex

        do! agentUtilities.SendJobStatus
                    {
                        AgentName = agentName
                        Metadata = None
                        Tool = "RESTler"
                        JobId = jobId
                        State = Raft.JobEvents.JobState.Completed
                        Metrics = Some(!combinedMetrics)
                        UtcEventTime = System.DateTime.UtcNow
                        Details = None
                        ResultsUrl = None
                    }

        return 0
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
                        eprintfn "Raft.Agent failed due to: %A" ex
                        do! agentUtilities.Trace(ex.Message, "Error", dict ["JobId", sprintf "%A" jobId; "AgentName", agentName])

                        do! agentUtilities.SendJobStatus
                                {
                                    AgentName = agentName
                                    Metadata = None
                                    Tool = "RESTler"
                                    JobId = jobId
                                    State = Raft.JobEvents.Error
                                    Metrics = None
                                    UtcEventTime = System.DateTime.UtcNow
                                    Details = Some (Map.empty.Add("Error", ex.Message))
                                    ResultsUrl = None
                                }

                        do! System.Console.Error.FlushAsync().ToAsync
                        do! System.Console.Out.FlushAsync().ToAsync
                        return 2
                }
            do! agentUtilities.Flush()
            do! Console.Out.FlushAsync() |> Async.AwaitTask
            do! Console.Error.FlushAsync() |> Async.AwaitTask
            return res
        }
    |> Async.RunSynchronously
