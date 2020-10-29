// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module Raft.RESTlerDriver

open System
open Microsoft.FSharpLu

module RESTler =
    let version = "6.1.0"

module private RESTlerInternal =
    let inline (++) (path1: string) (path2 : string) = IO.Path.Join(path1, path2)

    module Runtime =
        let [<Literal>] DotNet = "dotnet"

        let [<Literal>] Python = "python3"


    module Paths =
        let compiler (restlerRootDirectory: string) = 
            restlerRootDirectory ++ "compiler" ++ "Restler.CompilerExe.dll"

        let restler (restlerRootDirectory) =
            restlerRootDirectory ++ "engine" ++ "restler.pyc"

        let resultAnalyzer = 
            "/raft" ++ "result-analyzer" ++ "RaftResultAnalyzer.dll"


    (*
    let SupportedCheckers =
        [
            "leakagerule"
            "resourcehierarchy"
            "useafterfree"
            "namespacerule"
            "invaliddynamicobject"
            "payloadbody"
            "*"
        ]
    *)


    type ProcessResult =
        {
            ExitCode : int option
            ProcessId : int
        }

    let startProcessAsync command arguments workingDir (stdOutFilePath: string option) (stdErrFilePath: string option) =
        async {
            use instance =
                new Diagnostics.Process(
                    StartInfo =
                        Diagnostics.ProcessStartInfo
                            (
                                FileName = command,
                                WorkingDirectory = workingDir,
                                Arguments = arguments,
                                CreateNoWindow = false,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            ),
                    EnableRaisingEvents = true
                )

            use instanceTerminated = new System.Threading.AutoResetEvent(false)
            use noMoreOutput = new System.Threading.AutoResetEvent(false)
            use noMoreError = new System.Threading.AutoResetEvent(false)

            // Note: it's important to register this event __before__ calling instance.Start()
            // to avoid a deadlock if the process terminates too quickly...
            instance.Exited.Add
                (fun _ ->
                    if not instanceTerminated.SafeWaitHandle.IsClosed && not instanceTerminated.SafeWaitHandle.IsInvalid then
                        instanceTerminated.Set() |> ignore
                )


            // Standard output must be read prior to waiting on the instance to exit.
            // Otherwise, a deadlock is created when the child process has filled its output
            // buffer and waits for the parent to consume it, and the parent waits for the
            // child process to exit first.
            // Reference: https://stackoverflow.com/questions/139593/processstartinfo-hanging-on-waitforexit-why?lq=1
            use stdOutFile =
                match stdOutFilePath with
                | Some path -> System.IO.File.CreateText(path) :> IO.TextWriter
                | None -> IO.TextWriter.Null
            use stdErrFile =
                match stdErrFilePath with
                | Some path -> System.IO.File.CreateText(path) :> IO.TextWriter
                | None -> IO.TextWriter.Null
        
            let appendHandler
                    (endOfStreamEvent:System.Threading.AutoResetEvent)
                    (aggregator:IO.TextWriter)
                    (dataReceived:Diagnostics.DataReceivedEventArgs) =
                if isNull dataReceived.Data then
                    if not endOfStreamEvent.SafeWaitHandle.IsClosed && not endOfStreamEvent.SafeWaitHandle.IsInvalid then
                        endOfStreamEvent.Set() |> ignore
                else
                    aggregator.WriteLine(dataReceived.Data) |> ignore

            instance.OutputDataReceived.Add(appendHandler noMoreOutput stdOutFile)
            instance.ErrorDataReceived.Add(appendHandler noMoreError stdErrFile)

            printfn "Running process with arguments : %s and argument list : %A" instance.StartInfo.Arguments instance.StartInfo.ArgumentList

            if instance.Start() then
                instance.BeginOutputReadLine()
                instance.BeginErrorReadLine()

                let! _ = Async.Parallel [ Async.AwaitWaitHandle instanceTerminated;  Async.AwaitWaitHandle noMoreOutput; Async.AwaitWaitHandle noMoreError ]

                let exitCode =
                    try
                        Some instance.ExitCode
                    with :? System.InvalidOperationException ->
                        printfn "Ether process has not exited or process handle is not valid: '%O %O'" command arguments
                        None

                try
                    do! stdOutFile.FlushAsync() |> Async.AwaitTask
                with ex -> 
                    printfn "Failed to flush stdoout due to %A" ex

                try
                    do! stdErrFile.FlushAsync() |> Async.AwaitTask
                with ex ->
                    printfn "Failed to flush stderr due to %A" ex

                stdOutFile.Close()
                stdErrFile.Close()

                return
                    {
                        ProcessId = instance.Id
                        ExitCode = exitCode
                    }
            else
                return failwithf "Could not start command: '%s' with parameters '%s'" command arguments
        }

    let getRunExperimentFolder (fuzzingWorkingDirectory) (runStartTime: DateTime) =
        let restlerResults = IO.DirectoryInfo(fuzzingWorkingDirectory ++ "RestlerResults")
        if restlerResults.Exists then
            let experiments = restlerResults.EnumerateDirectories("experiment*")
            if Seq.isEmpty experiments then
                None
            else
                let startedExperiments =
                    experiments 
                    |> Seq.filter ( fun e -> e.CreationTimeUtc >= runStartTime)
                    |> Seq.sortBy ( fun e -> e.CreationTimeUtc )

                if (Seq.length startedExperiments > 1) then
                    printfn "There are : %d [%A] that have been create past %A. Using one closest to start time of this run." 
                                (Seq.length startedExperiments) startedExperiments runStartTime
                startedExperiments |> Seq.tryHead
        else
            None
 
    /// Runs the results analyzer.  Note: the results analyzer searches for all
    /// logs in the specified root directory
    let runResultsAnalyzer fuzzingWorkingDirectory runStartTime = 
        async {
            let resultsAnalyzer = Paths.resultAnalyzer
            if not (IO.File.Exists resultsAnalyzer) then
                failwith "Could not find path to RESTler results analyzer."

            match getRunExperimentFolder fuzzingWorkingDirectory runStartTime with
            | Some experimentFolder ->
                let! summaryPath =
                    async {
                        let restlerExperimentLogs = experimentFolder.FullName ++ "logs"
                        let bug_buckets = experimentFolder.FullName ++ "bug_buckets" ++ "bug_buckets.txt"
                        if IO.Directory.Exists restlerExperimentLogs then
                            let resultsAnalyzerParameters =
                                [
                                    sprintf "--restler-results-folder \"%s\"" restlerExperimentLogs
                                    sprintf "--bug-buckets \"%s\"" bug_buckets
                                ]
                            let resultsAnalyzerCmdLine = resultsAnalyzerParameters |> String.concat " "
                            let! result =
                                startProcessAsync
                                    Runtime.DotNet
                                    (sprintf "\"%s\" %s" resultsAnalyzer resultsAnalyzerCmdLine)
                                    fuzzingWorkingDirectory
                                    (Some (fuzzingWorkingDirectory ++ "stdout-resultsAnalyzer.txt"))
                                    (Some (fuzzingWorkingDirectory ++ "stderr-resultsAnalyzer.txt"))

                            match result.ExitCode with
                            | Some exitCode -> 
                                if exitCode <> 0 then
                                    printfn "Results analyzer for logs in %s failed." fuzzingWorkingDirectory
                            | None ->
                                printfn "Result Analyzer did not produce exit code"

                            let summaryPath = restlerExperimentLogs ++ "raft-analyzer-summary.json"

                            if IO.File.Exists summaryPath then
                                return Some summaryPath
                            else
                                eprintfn "RESTLER Summary file was not found: %s" summaryPath
                                return None
                        else
                            return None
                    }
                return Some summaryPath
            | None -> 
                return None
        }

    let runRestlerEngine restlerRootPath workingDirectory (engineArguments: string seq) = 
        async {
            let restlerPyPath = Paths.restler restlerRootPath

            if not (IO.File.Exists restlerPyPath) then
                failwith "Could not find path to RESTler engine."

            let restlerParameterCmdLine = engineArguments |> String.concat " "

            let engineStdErr = workingDirectory ++ "stderr-RESTlerEngine.txt"
            let engineStdOut = workingDirectory ++ "stdout-RESTlerEngine.txt"

            printfn "Running restler with command line : %s" restlerParameterCmdLine

            let! result =
                startProcessAsync
                    Runtime.Python
                    (sprintf "-B \"%s\" %s" restlerPyPath restlerParameterCmdLine)
                    workingDirectory
                    (Some engineStdOut)
                    (Some engineStdErr)

            match result.ExitCode with
            | Some exitCode ->
                if exitCode = -1 then
                    failwithf "RESTler caught an unhandled exception. See %s for more information." engineStdOut
                else if exitCode <> 0 then
                    failwithf "RESTler engine failed. See logs in %s directory for more information." workingDirectory
            | None ->
                printfn "RESTler engine did not produce exit code"

            let engineError = IO.FileInfo(engineStdErr)
            if engineError.Length > 0L then
                failwithf "RESTler engined failed. See RESTler error log %s for more information." engineStdErr
        }

    let compile restlerRootDirectory (workingDirectory: string) (config:Raft.RESTlerTypes.Compiler.Config) = 
        async {
            let compilerPath = Paths.compiler restlerRootDirectory
            if not (IO.File.Exists compilerPath) then
                failwith "Could not find path to compiler.  Please re-install RESTler or contact support."

            let compilerConfigPath = workingDirectory ++ "config.json"
            let compilerConfig =
                { config with
                    grammarOutputDirectoryPath = Some workingDirectory
                    customDictionaryFilePath = config.customDictionaryFilePath }
            Json.Compact.serializeToFile compilerConfigPath compilerConfig

            // Run compiler
            let! result =
                startProcessAsync
                    Runtime.DotNet
                    (sprintf "\"%s\" \"%s\"" compilerPath compilerConfigPath)
                    workingDirectory
                    (Some (workingDirectory ++ "stdout-RESTlerCompiler.txt"))
                    (Some (workingDirectory ++ "stderr-RESTlerCompiler.txt"))

            match result.ExitCode with
            | Some exitCode ->
                if exitCode <> 0 then
                    failwithf "Compiler failed. See logs in %s directory for more information. " workingDirectory
            | None ->
                failwithf "Compiler did not produce and exit code"
        }


    /// Gets the RESTler engine parameters common to any fuzzing mode
    let getCommonParameters workingDirectory (fuzzingMode: string option) (parameters:Raft.RESTlerTypes.Engine.EngineParameters) =
        let settings = Raft.RESTlerTypes.Engine.Settings.FromEngineParameters fuzzingMode parameters
        let settingsFilePath = workingDirectory ++ "settings.json"
        Json.Compact.serializeToFile settingsFilePath settings
        [
            sprintf "--settings %s" settingsFilePath
            sprintf "--restler_grammar \"%s\"" parameters.grammarFilePath
            sprintf "--custom_mutations \"%s\"" parameters.mutationsFilePath

            // Checkers
            (if parameters.checkerOptions.Length > 0 then
                parameters.checkerOptions
                |> List.map (fun (x, y) -> sprintf "%s %s" x y)
                |> String.concat " "
            else "")
            sprintf "--set_version 'restler/%s'" RESTler.version
        ]

    let validateAuthentication workingDirectory (tokenOptions : RESTlerTypes.Engine.RefreshableTokenOptions) =
        async {
            printfn "Validating authentication configuration"
            let cmd, args = "/bin/sh", sprintf "-c \"%s\"" tokenOptions.refreshCommand

            let! r = startProcessAsync cmd args "." None (Some(workingDirectory ++ "stderr-auth.txt"))
            match r.ExitCode with
            | Some 0 -> return ()
            | None | Some _ ->
                return failwithf "Failed to validate Authentication configuration. Please see stdout-auth.txt and stderr-auth.txt for errors."
        }

    let test testType restlerRootDirectory workingDirectory (parameters: Raft.RESTlerTypes.Engine.EngineParameters) = 
        async {
            do!
                match parameters.refreshableTokenOptions with
                | None -> async.Return()
                | Some t -> validateAuthentication workingDirectory t

            let testParameters = getCommonParameters workingDirectory (Some testType) parameters
            do! runRestlerEngine restlerRootDirectory workingDirectory testParameters
        }


    let fuzz fuzzType restlerRootDirectory workingDirectory (parameters: Raft.RESTlerTypes.Engine.EngineParameters) = 
        async {
            do!
                match parameters.refreshableTokenOptions with
                | None -> async.Return()
                | Some t -> validateAuthentication workingDirectory t

            let fuzzingParameters = getCommonParameters workingDirectory (Some fuzzType) parameters
            do! runRestlerEngine restlerRootDirectory workingDirectory fuzzingParameters
        }

    let replay restlerRootDirectory workingDirectory replayLogFilePath (parameters: Raft.RESTlerTypes.Engine.EngineParameters) = 
        async {
            do!
                match parameters.refreshableTokenOptions with
                | None -> async.Return()
                | Some t -> validateAuthentication workingDirectory t

            let replayParameters =
                (getCommonParameters workingDirectory None parameters)
                @
                [
                    sprintf "--replay_log %s" replayLogFilePath
                ]

            do! runRestlerEngine restlerRootDirectory workingDirectory replayParameters
        }

// Call this first to make sure that we have everything we need to even start fuzzing in the first place
let validateRestlerComponentsPresent (restlerRootDirectory: string) =
    let restlerPyPath = RESTlerInternal.Paths.restler restlerRootDirectory
    if not (IO.File.Exists restlerPyPath) then
        Result.Error "Could not find path to RESTler engine."
    else
        let resultsAnalyzer = RESTlerInternal.Paths.resultAnalyzer
        if not (IO.File.Exists resultsAnalyzer) then
            Result.Error "Could not find path to RESTler results analyzer"
        else
            let compilerPath = RESTlerInternal.Paths.compiler restlerRootDirectory
            if not (IO.File.Exists compilerPath) then
                Result.Error "Could not find path to RESTler compiler."
            else
                Result.Ok ()

let compile restlerRootDirectory workingDirectory config =
    RESTlerInternal.compile restlerRootDirectory workingDirectory config


type ReportRunSummary = Raft.JobEvents.RunSummary option -> Async<unit>
let inline (++) (path1: string) (path2 : string) = IO.Path.Join(path1, path2)

let processRunSummary workingDirectory runStartTime =
    async {
        match! RESTlerInternal.runResultsAnalyzer workingDirectory runStartTime with
        | None ->
            return None
        | Some runSummaryPath ->
            let summary =
                match runSummaryPath with
                | None ->
                    Raft.JobEvents.RunSummary.Empty
                | Some path ->
                    match Json.Compact.tryDeserializeFile path with
                    | Choice1Of2 (runSummary: Raft.JobEvents.RunSummary) -> 
                        runSummary
                    | Choice2Of2 err -> 
                        eprintfn "Failed to process run summary: %s" err
                        Raft.JobEvents.RunSummary.Empty
            return Some summary
    }

let resultAnalyzer workingDirectory (token: Threading.CancellationToken) (report: ReportRunSummary) (runStartTime: DateTime, reportInterval: TimeSpan option) =
    let rec analyze() =
        async {
            if token.IsCancellationRequested then
                let! summary = processRunSummary workingDirectory runStartTime
                printfn "Reporting summary one last time before exiting: %A" summary
                do! report summary
                return ()
            else
                match reportInterval with
                | None ->
                    printfn "Interval reporting is not enabled. Will only report results at the end of the run."
                    let! _ = Async.AwaitWaitHandle(token.WaitHandle)
                    return! analyze()
                | Some interval ->
                    let! summary = processRunSummary workingDirectory runStartTime
                    do! report summary
                    let! _ = Async.AwaitWaitHandle(token.WaitHandle, int interval.TotalMilliseconds)
                    return! analyze()
        }
    analyze()

let isBugFile (file: IO.FileInfo) =
    file.Name <> "bug_buckets.txt"

let loadTestRunSummary workingDirectory runStartTime =
    match RESTlerInternal.getRunExperimentFolder workingDirectory runStartTime with
    | Some experimentFolder ->
        let testingSummaryLog = experimentFolder.FullName ++ "logs" ++ "testing_summary.json"
        if IO.File.Exists testingSummaryLog then
            let testingSummary: RESTlerTypes.Logs.TestingSummary = Json.Compact.Strict.deserializeFile testingSummaryLog
            Some testingSummary
        else
            None
    | None -> None



let getListOfBugs workingDirectory (runStartTime: DateTime) =
    async {
        match RESTlerInternal.getRunExperimentFolder workingDirectory runStartTime with
        | None -> return Seq.empty

        | Some experiment ->
            let bugBuckets = IO.DirectoryInfo(experiment.FullName ++ "bug_buckets")
            if bugBuckets.Exists then
                let bugFiles = 
                    bugBuckets.EnumerateFiles("*.txt") 
                    |> Seq.filter isBugFile
                    |> Seq.map (fun f -> f.Name)
                return bugFiles
            else
                return Seq.empty
    }

let bugFoundPollInterval = TimeSpan.FromSeconds (10.0)
type OnBugFound = Map<string, string> -> Async<unit>
let pollForBugFound workingDirectory (token: Threading.CancellationToken) (runStartTime: DateTime) (onBugFound : OnBugFound) =
    let rec poll() =
        async {
            if token.IsCancellationRequested then
                return ()
            else
                let! _ = Async.AwaitWaitHandle(token.WaitHandle, int bugFoundPollInterval.TotalMilliseconds)
                match RESTlerInternal.getRunExperimentFolder workingDirectory runStartTime with
                | None ->
                    return! poll()

                | Some experiment ->
                    let restlerExperimentLogs = experiment.FullName ++ "logs"

                    if IO.Directory.Exists restlerExperimentLogs then
                        let bugsFoundPosted = restlerExperimentLogs ++ "raft-bugsfound.posted.txt"
                        let! postedBugs =
                            async {
                                if IO.File.Exists bugsFoundPosted then
                                    let! bugsPosted = IO.File.ReadAllLinesAsync(bugsFoundPosted) |> Async.AwaitTask
                                    return Set.ofArray bugsPosted
                                else 
                                    return Set.empty
                            }

                        let! bugFiles = getListOfBugs workingDirectory runStartTime
                        let! _ =
                            bugFiles
                            |> Seq.map (fun bugFile ->
                                async {
                                    if not <| postedBugs.Contains bugFile then
                                        printfn "Posting bug found %s" bugFile
                                        do! onBugFound (Map.empty.Add("Experiment", experiment.Name).Add("BugBucket", bugFile))
                                    return ()
                                }
                            ) |> Async.Sequential

                        do! IO.File.WriteAllLinesAsync(bugsFoundPosted, bugFiles) |> Async.AwaitTask

                    return! poll()
            }
    poll()

let replay restlerRootDirectory workingDirectory replayLogFile (parameters: Raft.RESTlerTypes.Engine.EngineParameters) =
    async {
        let ts = DateTime.UtcNow
        do! RESTlerInternal.replay restlerRootDirectory workingDirectory replayLogFile parameters
        let! runSummary = processRunSummary workingDirectory ts
        return runSummary
    }

let test (testType: string)
            restlerRootDirectory workingDirectory
            (parameters: Raft.RESTlerTypes.Engine.EngineParameters)
            (onBugFound: OnBugFound)
            (report: ReportRunSummary)(runStartTime: DateTime, reportInterval: TimeSpan option) =
    async {
        use token = new Threading.CancellationTokenSource()
        let! _ = Async.Parallel [
                async {
                    do! RESTlerInternal.test testType restlerRootDirectory workingDirectory parameters
                    token.Cancel()
                }

                resultAnalyzer workingDirectory token.Token report (runStartTime, reportInterval)

                pollForBugFound workingDirectory token.Token runStartTime onBugFound

            ]
        return ()
    }

let fuzz (fuzzType: string)
            restlerRootDirectory
            workingDirectory
            (parameters: Raft.RESTlerTypes.Engine.EngineParameters)
            (onBugFound : OnBugFound)
            (report: ReportRunSummary)
            (runStartTime: DateTime, reportInterval: TimeSpan option) =
    async {
        use token = new Threading.CancellationTokenSource()
        let! _ = Async.Parallel [
                async {
                    do! RESTlerInternal.fuzz fuzzType restlerRootDirectory workingDirectory parameters
                    token.Cancel()
                }

                resultAnalyzer workingDirectory token.Token report (runStartTime, reportInterval)

                pollForBugFound workingDirectory token.Token runStartTime onBugFound
            ]
        return ()
    }

