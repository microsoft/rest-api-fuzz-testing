// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Learn more about F# at http://fsharp.org

open System
open Microsoft.FSharpLu

type System.Threading.Tasks.Task with
    member x.ToAsync = Async.AwaitTask(x)

type System.Threading.Tasks.Task<'T> with
    member x.ToAsync = Async.AwaitTask(x)

module CommandLineArguments =
    type RunAnalyzerOn =
        | RestlerNetworkLogPath of string
        | RestlerResultsFolderPath of string

    type ResultAnalyzerArgs =
        {
            RunAnalyzerOn: RunAnalyzerOn option
            BugBucketsPath : string option
        }

    let [<Literal>] ResultsFolderPath = "--restler-results-folder"
    let [<Literal>] NetworkLog = "--network-log-file-path"
    let [<Literal>] BugBuckets = "--bug-buckets"

    let usage =
        [
            sprintf "RaftResultAnalyzer [%s | %s] %s" ResultsFolderPath NetworkLog BugBuckets
            sprintf "%s <Path to the logs folder produced by RESTler>" ResultsFolderPath
            sprintf "%s <Path to the network log produced by RESTler>" NetworkLog
            sprintf "%s <Path to bug-buckets.txt>" BugBuckets
        ] 
        |> (fun args -> String.Join("\n", args))

    let parseCommandLine (argv: string array) =
        if Array.isEmpty argv then
            failwithf "Expected command line arguments %s" usage
        else
            let rec parse (config: ResultAnalyzerArgs) (args: string list) =
                match args with
                | [] -> config
                | ResultsFolderPath :: path :: rest ->
                    parse {config with RunAnalyzerOn = Some (RunAnalyzerOn.RestlerResultsFolderPath path) } rest
                | NetworkLog :: path :: rest ->
                    parse {config with RunAnalyzerOn = Some (RunAnalyzerOn.RestlerNetworkLogPath path) } rest
                | BugBuckets :: path :: rest ->
                    parse {config with BugBucketsPath = Some (path)} rest
                | arg :: _ -> failwithf "Unhandled command line parameter: %s" arg
            parse {RunAnalyzerOn = None; BugBucketsPath = None} (argv |> List.ofArray)


let [<Literal>] RaftAnalyzerFilePrefix = "raft-analyzer"

type AnalyzerNetworkLogProgress =
    {
        NetworkLogName : string
        FileProcessedOffset: int

        ResponseCodeCounts: Map<int, int>
    }

    static member Empty (logFileName : string) =
        {
            NetworkLogName = logFileName
            FileProcessedOffset = 0
            ResponseCodeCounts = Map.empty
        }


type SubArrayResult =
    | SourceTooShort
    | SubArrayFullMatch of int
    | SubArrayPartialMatch of int
    | NoMatch

let inline subArrayMatch (srcAr: 'a array, srcStartPos: int, srcEndPos: int) (subAr: 'a array) =
    if srcStartPos + subAr.Length > srcEndPos then
        SourceTooShort
    else
        let mutable index = 0

        while index < subAr.Length && srcAr.[srcStartPos + index] = subAr.[index] do
            index <- index + 1

        if index = 0 then
            NoMatch
        elif index = subAr.Length then
            SubArrayFullMatch index
        else
            SubArrayPartialMatch index


let processBuffer (buffer: char array, bufferUsed: int) (snippet: char array) (state: 'a, onFullMatch: int -> 'a -> 'a) =
    let rec doProcess (currentBufferIndex) (state: 'a) =
        if currentBufferIndex >= bufferUsed then
            state, currentBufferIndex
        else
            if buffer.[currentBufferIndex] = snippet.[0] then
                match subArrayMatch (buffer, currentBufferIndex, bufferUsed) snippet with
                | SubArrayFullMatch i ->
                    doProcess (currentBufferIndex + i) (onFullMatch (currentBufferIndex + i) state)
                | SubArrayPartialMatch i ->
                    if currentBufferIndex + i + 1 >= bufferUsed then
                        // This is the end of the buffer and we got partial match.
                        // Let caller refill the buffer preserving the last section and then re-process the buffer
                        state, currentBufferIndex
                    else
                        doProcess (currentBufferIndex + i) state
                | NoMatch -> 
                    doProcess (currentBufferIndex + 1) state
                | SourceTooShort ->
                    state, currentBufferIndex
            else
                doProcess (currentBufferIndex + 1) state
    doProcess 0 state


let processRESTlerProgress (progressFilePath: string) =
    let dateTimePrefixLen = "0000-00-00 00:00:00.000: ".Length
    let receivedLogSnippet = @"Received: 'HTTP/1.1".ToCharArray()
    async {
        // logic goes as follows: find receivedLogSnippet, right after that the characters
        // represent the response status code up to next space character
        let bufferLen = 8 * 1024 * 1024
        let httpResponseCodeLength = 3
        let progressFile = IO.FileInfo(progressFilePath)
        let progress : AnalyzerNetworkLogProgress = Json.Compact.deserializeFile progressFilePath
        let logFile = IO.FileInfo (IO.Path.Join(progressFile.DirectoryName, progress.NetworkLogName))

        if logFile.Length = int64 progress.FileProcessedOffset then
            return progress
        else
            let fileStream = logFile.OpenText()
            try
                let buffer = Array.zeroCreate bufferLen

                let rec doProcess (progress: AnalyzerNetworkLogProgress) =
                    async {
                        fileStream.BaseStream.Seek(int64 progress.FileProcessedOffset, IO.SeekOrigin.Begin) |> ignore
                        let! bytesRead = fileStream.ReadAsync( buffer, 0, bufferLen).ToAsync

                        let onBufferMatch bufferIndex (responseCodeCounts: Map<int, int>) =
                            let isStartOfFile = (bufferIndex - receivedLogSnippet.Length - dateTimePrefixLen = 0)
                            let hasDelim = not isStartOfFile && (buffer.[bufferIndex - receivedLogSnippet.Length - dateTimePrefixLen - 1] = '\n')
                            let hasExpectedSpace = (buffer.[bufferIndex + httpResponseCodeLength + 1] = ' ')

                            if (isStartOfFile || hasDelim) && hasExpectedSpace then

                                let responseCode = String(buffer, bufferIndex, httpResponseCodeLength + 1)
                                match Int32.TryParse responseCode with
                                | true, n ->
                                    if Map.containsKey n responseCodeCounts then
                                        Map.add n (responseCodeCounts.[n] + 1) responseCodeCounts
                                    else
                                        Map.add n 1 responseCodeCounts
                                | false, _ -> 
                                    responseCodeCounts
                            else
                                responseCodeCounts

                        let updatedCounts, newIndex = processBuffer (buffer, bytesRead) receivedLogSnippet (progress.ResponseCodeCounts, onBufferMatch)

                        let updatedProgress =
                            { progress with
                                ResponseCodeCounts = updatedCounts
                                FileProcessedOffset = progress.FileProcessedOffset + newIndex
                            }

                        Json.Compact.serializeToFile progressFilePath updatedProgress

                        if bytesRead < bufferLen then
                            return updatedProgress
                        else
                            return! doProcess updatedProgress
                    }
                return! doProcess progress

            finally
                fileStream.Close()
    }

[<EntryPoint>]
let main argv =

    let config = CommandLineArguments.parseCommandLine argv

    let networkLogs, logDirectory =
        match config.RunAnalyzerOn with
        | None -> failwithf "Analyzer target is not set. %s" CommandLineArguments.usage
        | Some (CommandLineArguments.RunAnalyzerOn.RestlerNetworkLogPath path) ->
            let fi = IO.FileInfo path
            if fi.Exists then
                [|path|], fi.DirectoryName
            else
                failwithf "Could not find RESTler network log file: %s" path
        | Some (CommandLineArguments.RunAnalyzerOn.RestlerResultsFolderPath folderPath) ->
            if IO.Directory.Exists folderPath then
                IO.Directory.GetFiles(folderPath, "network.*.txt", IO.SearchOption.TopDirectoryOnly), folderPath
            else
                eprintfn "Could not find RESTler logs folder: %s" folderPath
                [||], folderPath

    let updateProgress =
        networkLogs
        |> Array.map (fun logPath ->
            let logFileInfo = IO.FileInfo(logPath)
            let progressFile = IO.FileInfo(IO.Path.Join(logFileInfo.DirectoryName, sprintf "%s.%s.json" RaftAnalyzerFilePrefix logFileInfo.Name))

            if progressFile.Exists then
                Json.Compact.deserializeFile<AnalyzerNetworkLogProgress> progressFile.FullName
                |> ignore
            else
                Json.Compact.serializeToFile progressFile.FullName (AnalyzerNetworkLogProgress.Empty logFileInfo.Name)
            progressFile.FullName
        )
        |> Array.map (fun progressFile ->
            processRESTlerProgress progressFile
        )
        |> Async.Sequential
        |> Async.RunSynchronously
    
    let summary =
        (Raft.JobEvents.RunSummary.Empty, updateProgress)
        ||> Array.fold(fun s progress ->
            let counts, total =
                ((s.ResponseCodeCounts, s.TotalRequestCount), progress.ResponseCodeCounts)
                ||> Map.fold(fun (summaryCounts, total) code count ->
                    if summaryCounts.ContainsKey code then
                        summaryCounts.Add(code, summaryCounts.[code] + count), total + count
                    else
                        summaryCounts.Add(code, count), total + count
                )

            { s with
                ResponseCodeCounts = counts
                TotalRequestCount = total
            }
        )

    let summary =
        let totalBuckets = "Total Buckets: "
        let stopWhenHit = "-------------"
        match config.BugBucketsPath with
        | None ->
            eprintfn "Bug buckets path is not set"
            summary
        | Some b ->
            let bugBuckets = IO.FileInfo b
            if bugBuckets.Exists then
                let textStream = bugBuckets.OpenText()
                try
                    let rec parseBugBuckets () =
                        let line = textStream.ReadLine()
                        if line.StartsWith stopWhenHit then
                            summary
                        else if line.StartsWith(totalBuckets) then
                            match Int32.TryParse (line.Substring(totalBuckets.Length)) with
                            | true, n -> {summary with TotalBugBucketsCount = n}
                            | false, _ -> summary
                        else
                            parseBugBuckets()

                    parseBugBuckets()

                finally
                    textStream.Close()
            else
                summary

    if IO.Directory.Exists logDirectory then
        Json.Compact.serializeToFile (IO.Path.Join(logDirectory, (sprintf  "%s-summary.json" RaftAnalyzerFilePrefix))) summary
    0
