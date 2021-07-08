// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module RESTlerAgentConfigTypes
open System.Collections.Generic

type CustomDictionary =
    {
        // <FROM HERE>
        // if None and FSCheck seed is set then random values are used
        // if None and FSCheck seed is not set, then hard-coded RESTler default values are used
        // if [] then hard-coded RESTler default values are used
        FuzzableString : string array option
        FuzzableInt : string array option
        FuzzableNumber : string array option
        FuzzableBool : string array option
        FuzzableDatetime : string array option
        FuzzableObject : string array option
        FuzzableUuid4 : string array option
        //<TO HERE>

        CustomPayload : IDictionary<string, string array> option
        CustomPayloadUuid4Suffix : IDictionary<string, string> option
        CustomPayloadHeader :  IDictionary<string, string array> option
        ShadowValues : IDictionary<string, IDictionary<string, string array>> option
    }

/// User-specified compiler configuration
type CompileConfiguration =
    {
        //relative path to a JSON grammar to use for compilation relative to compile folder path
        //if set then JSON grammar used for compilation instead of Swagger
        InputJsonGrammarPath: string option

        /// Grammar is produced by compile step. The compile step
        /// file share is mounted and set here. Agent will not modify
        /// this share. Agent will make a copy of all needed files to it's work directory.
        InputFolderPath: string option


        // When true, only fuzz the GET requests
        ReadOnlyFuzz : bool

        // When true, allow GET requests to be considered.
        // This option is present for debugging, and should be
        // set to 'false' by default.
        // In limited cases when GET is a valid producer, the user
        // should add an annotation for it.
        AllowGetProducers : bool

        // authentication token refreshed on a timer
        UseRefreshableToken : bool

        // if Some seed then it will be used to populate customDictitonary fields (see CustomDictionary type for logic on how the values are set)
        // if None - then default hard-coded RESTler values are used for populating customDictionary fields
        MutationsSeed : int64 option

        CustomDictionary: CustomDictionary option

        DiscoverExamples : bool
        ExamplesDirectory : string option
        ExampleConfigFilePath : string option

        DataFuzzing : bool
        ResolveQueryDependencies: bool
        ResolveBodyDependencies : bool
        UseBodyExamples : bool
        UseQueryExamples : bool
        IncludeOptionalParameters : bool

        TrackFuzzedParameterNames : bool option
    }

    static member Empty : CompileConfiguration =
        {
            InputJsonGrammarPath = None
            InputFolderPath = None
            ReadOnlyFuzz = false
            AllowGetProducers = false
            UseRefreshableToken = false
            MutationsSeed = None;
            CustomDictionary = None
            DiscoverExamples = true
            ExamplesDirectory = None
            ExampleConfigFilePath = None
            DataFuzzing = true
            ResolveQueryDependencies = true
            ResolveBodyDependencies = true
            UseBodyExamples = true
            UseQueryExamples = true
            IncludeOptionalParameters = true
            TrackFuzzedParameterNames = None
        }


type ReplayConfiguration =
    {
        //list of paths to RESTler folder runs to replay (names of folders are assigned when mounted readonly/readwrite file share mounts)
        // if path is a folder, then all bug buckets replayed in the folder
        // if path is a bug_bucket file - then only that file is replayed.
            
        // if empty - then replay all bugs under jobRunConfiguration.previousStepOutputFolderPath
        BugBuckets : string array option
    }


type CheckerSettings =
    {
        Mode : string
    }

type PerResourceSetting =
    {
        //seconds
        ProducerTimingDelay : int
        //
        CreateOnce: bool
        //path to custom dictionary
        CustomDictionary : CustomDictionary
    }

type RunConfiguration = 
    {
        /// Path to grammar py relative to compile folder path. If not set then default "grammar.py" grammar is assumed
        GrammarPy: string option

        /// For Test of Fuzz tasks: Grammar is produced by compile step. The compile step
        /// file share is mounted and set here. Agent will not modify
        /// this share. Agent will make a copy of all needed files to it's work directory.
        /// For Replay task: path to RESTler Fuzz or Test run that contains bug buckets to replay
        InputFolderPath: string option

        /// The delay in seconds after invoking an API that creates a new resource
        ProducerTimingDelay : int option

        /// Specifies to use SSL when connecting to the server
        UseSsl : bool option

        /// Specifies whether to show contents of auth token in RESTler logs
        ShowAuthToken : bool option

        /// Token Refresh Interval
        AuthenticationTokenRefreshIntervalSeconds: int option

        /// RESTler run duration
        Duration : System.TimeSpan option
        /// Path regex for filtering tested endpoints
        PathRegex : string option

        // In context of Replay - do not replay bugs specified in the list
        // In context of Test or Fuzz - do not post onBugFound events if they are in the list
        IgnoreBugHashes : string array option

        /// Maximum duration of request before RESTler considers it to be timed out
        MaxRequestExecutionTime : int option

        /// If not set, then preconfigured checkers are used
        Checkers : Map<string, CheckerSettings> option

        IgnoreDependencies : bool option

        IgnoreFeedback : bool option

        IncludeUserAgent : bool option

        MaxAsyncResourceCreationTime : int option
        MaxCombinations : int option
        MaxSequenceLength : int option
        WaitForAsyncResourceCreation : bool option
        PerResourceSettings : Map<string, PerResourceSetting> option

    }

    static member Empty =
        {
            GrammarPy = None
            InputFolderPath = None
            ProducerTimingDelay = None
            UseSsl = None
            AuthenticationTokenRefreshIntervalSeconds = None
            PathRegex = None
            IgnoreBugHashes = None
            MaxRequestExecutionTime = None
            Checkers = None
            IgnoreDependencies = None
            IgnoreFeedback = None
            IncludeUserAgent = None
            MaxAsyncResourceCreationTime = None
            MaxCombinations = None
            MaxSequenceLength = None
            WaitForAsyncResourceCreation = None
            PerResourceSettings = None
            Duration = None
            ShowAuthToken = None
        }


type AgentConfiguration =
    {
        // If not set then result analyzer will run only once after RESTler exits
        ResultsAnalyzerReportTimeSpanInterval : System.TimeSpan option
    }

type TaskType =
    | Compile
    | Test
    | TestFuzzLean
    | TestAllCombinations
    | Fuzz
    | FuzzRandomWalk
    | FuzzBfsCheap
    | Replay


type RESTlerPayload =
    {
        Task: TaskType option

        //Compile task type configuration
        CompileConfiguration: CompileConfiguration option
        //Fuzz or Test task type configuration
        RunConfiguration : RunConfiguration option
        //Replay task type configuration
        ReplayConfiguration : ReplayConfiguration option

        //overall agent configuration
        AgentConfiguration : AgentConfiguration option
    }


type RESTlerPayloads =
    {
        tasks: RESTlerPayload array
    }