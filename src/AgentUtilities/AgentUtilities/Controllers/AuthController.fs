// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AgentUtilities.Controllers

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging


module Process =

    type ProcessResult =
        {
            ExitCode : int option
            ProcessId : int
            StdOut : string
            StdErr : string
        }

    let startProcessAsync command arguments workingDir =
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

            let stdOut = System.Text.StringBuilder()
            let stdErr = System.Text.StringBuilder()
    
            let appendHandler
                    (endOfStreamEvent:System.Threading.AutoResetEvent)
                    (aggregator:System.Text.StringBuilder)
                    (dataReceived:Diagnostics.DataReceivedEventArgs) =
                if isNull dataReceived.Data then
                    if not endOfStreamEvent.SafeWaitHandle.IsClosed && not endOfStreamEvent.SafeWaitHandle.IsInvalid then
                        endOfStreamEvent.Set() |> ignore
                else
                    aggregator.Append(dataReceived.Data) |> ignore

            instance.OutputDataReceived.Add(appendHandler noMoreOutput stdOut)
            instance.ErrorDataReceived.Add(appendHandler noMoreError stdErr)

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

                return
                    {
                        ProcessId = instance.Id
                        ExitCode = exitCode
                        StdOut = stdOut.ToString()
                        StdErr = stdErr.ToString()
                    }
            else
                return failwithf "Could not start command: '%s' with parameters '%s'" command arguments
        }

[<ApiController>]
[<Route("[controller]")>]
[<Produces("application/json")>]
type AuthController (logger : ILogger<AuthController>) =
    inherit ControllerBase()

    [<HttpGet("{authenticationMethod}/{secretName}")>]
    member this.Get(authenticationMethod: string, secretName: string) =
        async {
            // load all .sh files from /auth folder; 
            // then match authenticationMethod with the name (also lookup secret env variable) <- and pass that to the utility
            // if there is RAFT_secretName - use that; otherwise fail
            let agentAuthUtilities = "/raft-tools/agent-utilities/auth"

            let loadSecretEnv(secretEnvVar) =
                let evs = System.Environment.GetEnvironmentVariables()
                let ev =
                    evs.Keys
                    |> Seq.cast
                    |> Seq.tryFind(fun k -> 0 = String.Compare(k, secretEnvVar, true))

                match ev with
                | None -> Result.Error (sprintf "Token secret: %s is not set in the Environment" secretEnvVar)
                | Some k -> Result.Ok k

            
            match loadSecretEnv (sprintf "RAFT_%s" secretName) with
            | Result.Ok secretEnv ->
                let authScripts = System.IO.DirectoryInfo(agentAuthUtilities).EnumerateFiles("*.sh")
                match authScripts |> Seq.tryFind(fun s -> s.Name.ToLowerInvariant() = (sprintf "%s.sh" (authenticationMethod.ToLowerInvariant()))) with
                | None -> return this.NotFound(sprintf "%s authentication method not found" authenticationMethod) :> ActionResult
                | Some script ->
                    let! r = Process.startProcessAsync "/bin/sh" (sprintf "%s %s" script.Name secretEnv) agentAuthUtilities
                    match r.ExitCode with
                    | None | Some 0 ->
                        return this.Ok( {| Token = r.StdOut |}) :> ActionResult
                    | _ ->
                        printfn "Returning BadRequest due to : %s" r.StdErr
                        return this.BadRequest( {| Error = r.StdErr |} ) :> ActionResult
            | Result.Error err ->
                printfn "Returning NotFound due to %s" err
                return this.NotFound(err) :> ActionResult
        } |> Async.StartAsTask
