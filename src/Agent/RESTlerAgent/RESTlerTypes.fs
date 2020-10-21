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
            refreshInterval : int

            /// The command that, when run, generates a new token in the form required
            /// by the API (e.g. 'Header : <value>')
            refreshCommand : string
        }

    /// The user-facing engine parameters
    type EngineParameters =
        {
            /// File path to the REST-ler (python) grammar.
            grammarFilePath : string

            /// File path to the custom fuzzing dictionary.
            mutationsFilePath : string

            /// The IP of the endpoint being fuzzed
            targetIp : string

            /// The port of the endpoint being fuzzed
            targetPort : int

            /// The maximum fuzzing time in hours
            maxDurationHours : float option

            /// The authentication options, when tokens are required
            refreshableTokenOptions : RefreshableTokenOptions option

            /// The delay in seconds after invoking an API that creates a new resource
            producerTimingDelay : int

            /// Specifies to use SSL when connecting to the server
            useSsl : bool

            /// The string to use in overriding the Host for each request
            host : string option

            /// Path regex for filtering tested endpoints
            pathRegex : string option

            /// The checker options
            /// ["enable or disable", "list of specified checkers"]
            checkerOptions : (string * string) list
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

    type CheckerSettings =
        {
            mode : string
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

            target_ip: string

            target_port : int

            //in hours
            time_budget : float option

            token_refresh_cmd : string option

            //seconds
            token_refresh_interval: int option

            //if set - poll for resource to be created before
            //proceeding
            wait_for_async_resource_creation : bool

            per_resource_settings : IDictionary<string, PerResourceSetting> option

            checkers : IDictionary<string, CheckerSettings> option
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

                target_ip = ""

                target_port = 443

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
                match p.refreshableTokenOptions with
                | None -> None, None
                | Some options ->
                    (Some options.refreshInterval), (Some options.refreshCommand)
            {
                Settings.Default with
                    host = p.host
                    target_port = p.targetPort
                    target_ip = p.targetIp
                    time_budget = p.maxDurationHours
                    path_regex = p.pathRegex
                    global_producer_timing_delay = p.producerTimingDelay
                    no_ssl = not p.useSsl
                    fuzzing_mode = fuzzingMode
                    token_refresh_cmd = tokenRefreshCommand
                    token_refresh_interval = tokenRefreshInterval
            }


/// Data structures consumed by RESTLer compiler, so we can convert parameters coming to the RAFT Agent and serialize them
/// to something that RESTler can work with
module Compiler =
    /// User-specified compiler configuration
    type Config =
        {
            swaggerSpecFilePath : string list option
    
            // If specified, use this as the input and generate the python grammar.
            grammarInputFilePath : string option
    
            // If unspecified, will be set to the working directory
            grammarOutputDirectoryPath : string option
    
            customDictionaryFilePath : string option
    
            // If specified, update the engine settings with hints derived from the grammar.
            engineSettingsFilePath : string option
    
            includeOptionalParameters : bool
    
            useQueryExamples : bool
    
            useBodyExamples : bool
    
            /// When set to 'true', discovers examples and outputs them to a directory next to the grammar.
            /// If an existing directory exists, does not over-write it.
            discoverExamples : bool
    
            /// The directory where the compiler should look for examples.
            /// If 'discoverExamples' is true, this directory will contain the
            /// example files that have been discovered.
            /// If 'discoverExamples' is false, every time an example is used in the
            /// Swagger file, RESTler will first look for it in this directory.
            examplesDirectory : string
    
            /// Perform payload body fuzzing
            dataFuzzing : bool
    
            // When true, only fuzz the GET requests
            readOnlyFuzz : bool
    
            resolveQueryDependencies: bool
    
            resolveBodyDependencies: bool
    
            useRefreshableToken : bool
    
            // When true, allow GET requests to be considered.
            // This option is present for debugging, and should be
            // set to 'false' by default.
            // In limited cases when GET is a valid producer, the user
            // should add an annotation for it.
            allowGetProducers : bool
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
            // TODO restler_multipart_formdata :  Map<string, string list> option
        }
