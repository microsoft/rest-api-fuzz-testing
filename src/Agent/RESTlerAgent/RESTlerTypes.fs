// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

/// Copy paste of RESTler types consumed by the compiler and RESTler engine
module Raft.RESTlerTypes

open System.Collections.Generic
module Engine =

    /// Options for refreshing an authentication token in the RESTler engine
    type RefreshableTokenOptions =
        {
            /// The duration after which to refresh the token
            RefreshInterval : int

            /// The command that, when run, generates a new token in the form required
            /// by the API (e.g. 'Header : <value>')
            RefreshExec : string

            RefreshArgs : string
        }


    type EnginePerResourceSetting =
        {
            //seconds
            ProducerTimingDelay : int
            //use 1
            CreateOnce: bool
            //path to custom dictionary
            CustomDictionary : string
        }

    type CheckerSettings =
        {
            Mode : string
        }

    /// The user-facing engine parameters
    type EngineParameters =
        {
            /// File path to the REST-ler (python) grammar.
            GrammarFilePath : string

            /// File path to the custom fuzzing dictionary.
            MutationsFilePath : string

            /// The IP of the endpoint being fuzzed
            TargetIp : string option

            /// The port of the endpoint being fuzzed
            TargetPort : int option

            /// The maximum fuzzing time in hours
            MaxDurationHours : float option

            /// The authentication options, when tokens are required
            RefreshableTokenOptions : RefreshableTokenOptions option

            /// The delay in seconds after invoking an API that creates a new resource
            ProducerTimingDelay : int

            /// Specifies to use SSL when connecting to the server
            UseSsl : bool

            /// Specifies whether to show contents of auth token in RESTler logs
            ShowAuthToken : bool

            /// The string to use in overriding the Host for each request
            Host : string option

            /// Path regex for filtering tested endpoints
            PathRegex : string option

            /// The checker options
            /// ["enable or disable", "list of specified checkers"]
            CheckerOptions : (string * string) list
            Checkers : Map<string, CheckerSettings> option

            MaxRequestExecutionTime : int option

            IgnoreDependencies : bool option
            IgnoreFeedback : bool option
            IncludeUserAgent : bool option
            MaxAsyncResourceCreationTime : int option
            MaxCombinations : int option
            MaxSequenceLength : int option
            WaitForAsyncResourceCreation : bool option
            PerResourceSettings : Map<string, EnginePerResourceSetting> option
        }


    type PerResourceSetting =
        {
            //seconds
            producer_timing_delay: int
            //use 1
            create_once: int
            //path to custom dictionary
            custom_dictionary : string
        }


    type Settings =
        {
            max_combinations : int
            //seconds
            max_request_execution_time : int

            //seconds
            global_producer_timing_delay : int

            //number of object of the same type before deleted by GC
            dyn_object_cache_size : int

            //always 1
            fuzzing_jobs : int

            // available options:
            // None <- used during Replay
            // bfs; bfs-cheap; random-walk; directed-smoke-test
            fuzzing_mode : string option

            // make it 30 seconds
            garbage_collection_interval : int
        
            //false
            ignore_dependencies: bool

            //false
            ignore_feedback : bool

            //true
            include_user_agent : bool

            //Number of seconds to wait for an asynchronous resource to be created before continuing (60 seconds ?)
            max_async_resource_creation_time : int

            //100
            max_sequence_length : int

            //false
            no_ssl : bool
            //true
            no_tokens_in_logs: bool

            path_regex : string option

            //The time, in milliseconds, to throttle each request being sent.
            //This is here for special cases where the server will block requests from connections that arrive too quickly.
            //Using this setting is not recommended
            request_throttle_ms : int option

            //override host defined in swagger for every request
            host: string option

            target_ip: string option

            target_port : int option

            //in hours
            time_budget : float option

            token_refresh_cmd : string option

            //seconds
            token_refresh_interval: int option

            //if set - poll for resource to be created before
            //proceeding
            wait_for_async_resource_creation : bool

            per_resource_settings : Map<string, PerResourceSetting> option

            checkers : Map<string, CheckerSettings> option
        }

        static member Default =
            {
                max_combinations = 20
                max_request_execution_time = 60
        
                //seconds
                global_producer_timing_delay = 3

                //number of object of the same type before deleted by GC
                dyn_object_cache_size = 100

                //always 1
                fuzzing_jobs = 1

                // available options:
                // bfs; bfs-cheap; random-walk; directed-smoke-test
                fuzzing_mode = Some "directed-smoke-test"

                // make it 30 seconds ?
                garbage_collection_interval = 30

                ignore_dependencies = false
                ignore_feedback = false
                include_user_agent = true

                //Number of seconds to wait for an asynchronous resource to be created before continuing (60 seconds ?)
                max_async_resource_creation_time = 60

                //100
                max_sequence_length = 100

                //false
                no_ssl = false
                //true
                no_tokens_in_logs = true

                path_regex = None

                request_throttle_ms = None

                host = None

                target_ip = None

                target_port = None

                //in hours
                time_budget = None

                token_refresh_cmd = None

                //seconds
                token_refresh_interval = None

                //if set - poll for resource to be created before
                //proceeding
                wait_for_async_resource_creation = true

                per_resource_settings = None

                checkers = None
            }

        static member FromEngineParameters (fuzzingMode: string option) (p : EngineParameters) =
            let tokenRefreshInterval, tokenRefreshCommand =
                match p.RefreshableTokenOptions with
                | None -> None, None
                | Some options ->
                    (Some options.RefreshInterval), (Some (sprintf "%s %s" options.RefreshExec options.RefreshArgs))
            {
                Settings.Default with
                    host = p.Host
                    target_port = p.TargetPort
                    target_ip = p.TargetIp
                    time_budget = p.MaxDurationHours
                    path_regex = p.PathRegex
                    global_producer_timing_delay = p.ProducerTimingDelay
                    no_ssl = not p.UseSsl
                    no_tokens_in_logs = not p.ShowAuthToken
                    fuzzing_mode = fuzzingMode
                    token_refresh_cmd = tokenRefreshCommand
                    token_refresh_interval = tokenRefreshInterval
                    max_request_execution_time = Option.defaultValue Settings.Default.max_request_execution_time p.MaxRequestExecutionTime 
                    ignore_dependencies = Option.defaultValue Settings.Default.ignore_dependencies p.IgnoreDependencies
                    ignore_feedback = Option.defaultValue Settings.Default.ignore_feedback p.IgnoreFeedback
                    max_async_resource_creation_time = Option.defaultValue Settings.Default.max_async_resource_creation_time p.MaxAsyncResourceCreationTime
                    max_combinations = Option.defaultValue Settings.Default.max_combinations p.MaxCombinations
                    max_sequence_length = Option.defaultValue Settings.Default.max_sequence_length p.MaxSequenceLength
                    wait_for_async_resource_creation = Option.defaultValue Settings.Default.wait_for_async_resource_creation p.WaitForAsyncResourceCreation
                    per_resource_settings =
                        p.PerResourceSettings
                        |> Option.map(fun settings ->
                            settings |> Map.map (fun k v ->
                                {
                                    producer_timing_delay = v.ProducerTimingDelay
                                    create_once = if v.CreateOnce then 1 else 0
                                    custom_dictionary = v.CustomDictionary
                                } )
                        ) 
                    checkers = p.Checkers
            }


/// Data structures consumed by RESTLer compiler, so we can convert parameters coming to the RAFT Agent and serialize them
/// to something that RESTler can work with
module Compiler =
    /// User-specified compiler configuration
    type Config =
        {
            SwaggerSpecFilePath : string list option
    
            // If specified, use this as the input and generate the python grammar.
            GrammarInputFilePath : string option
    
            // If unspecified, will be set to the working directory
            GrammarOutputDirectoryPath : string option
    
            CustomDictionaryFilePath : string option
    
            // If specified, update the engine settings with hints derived from the grammar.
            EngineSettingsFilePath : string option
    
            IncludeOptionalParameters : bool
    
            UseQueryExamples : bool
    
            UseBodyExamples : bool
    
            /// When set to 'true', discovers examples and outputs them to a directory next to the grammar.
            /// If an existing directory exists, does not over-write it.
            DiscoverExamples : bool

            ExampleConfigFilePath : string option
    
            /// The directory where the compiler should look for examples.
            /// If 'discoverExamples' is true, this directory will contain the
            /// example files that have been discovered.
            /// If 'discoverExamples' is false, every time an example is used in the
            /// Swagger file, RESTler will first look for it in this directory.
            ExamplesDirectory : string
    
            /// Perform payload body fuzzing
            DataFuzzing : bool
    
            // When true, only fuzz the GET requests
            ReadOnlyFuzz : bool
    
            ResolveQueryDependencies: bool
    
            ResolveBodyDependencies: bool
    
            UseRefreshableToken : bool
    
            // When true, allow GET requests to be considered.
            // This option is present for debugging, and should be
            // set to 'false' by default.
            // In limited cases when GET is a valid producer, the user
            // should add an annotation for it.
            AllowGetProducers : bool
        }
    
    type MutationsDictionary =
        {
            restler_fuzzable_string : string array
            restler_fuzzable_int : string array
            restler_fuzzable_number : string array
            restler_fuzzable_bool : string array
            restler_fuzzable_datetime : string array
            restler_fuzzable_object : string array
            restler_fuzzable_uuid4 : string array
            restler_custom_payload : IDictionary<string, string array> option
            restler_custom_payload_uuid4_suffix : IDictionary<string, string> option
            restler_custom_payload_header :  IDictionary<string, string array> option
            shadow_values : IDictionary<string, IDictionary<string, string array>> option
        }

module Logs =
    type TestingSummary =
        {
            final_spec_coverage: string
            rendered_requests: string
            rendered_requests_valid_status: string
            num_fully_valid: int
            num_sequence_failures: int
            num_invalid_by_failed_resource_creations: int
            total_object_creations: int
            total_requests_sent : IDictionary<string, int>
            bug_buckets : IDictionary<string, int>
        }

    type BugPath =
        {
            file_path : string
        }
    type BugHashes = Map<string, BugPath>

