// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

open System

open Microsoft.Identity.Client
open Newtonsoft
open Microsoft.FSharpLu

type AuthType =
    | MSAL
    | Token

type SecretVariable = string

type Config =
    {
        AuthType : AuthType option
        Secret : SecretVariable option
        PrependLine : string option
    }

    static member Empty =
        {
            AuthType = None
            Secret = None
            PrependLine = None
        }


let [<Literal>] MSAL = "msal"
let [<Literal>] Token = "token"

let [<Literal>] Secret = "--secret"
let [<Literal>] PrependLine = "--prepend-line"

let usage =
    [
        sprintf "[%s | %s ]" MSAL Token
        sprintf "[%s]" Secret
        sprintf "[%s]" PrependLine
    ] |> (fun args -> String.Join("\n", args))


let parseCommandLine (argv: string array) =
    if Array.length argv = 0 then
        failwithf "Expected command line arguments %s" usage

    let rec parse (currentConfig: Config) (args: string list) =
        match args with
        | [] -> currentConfig
        | Token :: rest ->
            parse {currentConfig with AuthType = Some AuthType.Token} rest
        | MSAL :: rest ->
            parse {currentConfig with AuthType = Some AuthType.MSAL} rest
        | Secret :: secret :: rest ->
            parse {currentConfig with Secret = Some secret} rest
        | PrependLine :: prependLine :: rest ->
            parse {currentConfig with PrependLine = Some prependLine} rest
        | arg :: _ -> 
            failwithf "Unhandled command line parameter: %s\nusage: %s" arg usage

    parse Config.Empty (Array.toList argv)

type AppRegistration =
    {
        tenant : string
        client : string
        secret : string
        scopes : string array option
        authorityUri :string option
    }


[<EntryPoint>]
let main argv =
    async {
        let config = parseCommandLine argv

        let loadSecretEnv(secretEnvVar) =
            match System.Environment.GetEnvironmentVariable(sprintf "RAFT_%s" secretEnvVar) with
            | s when String.IsNullOrEmpty s ->
                match System.Environment.GetEnvironmentVariable(secretEnvVar) with
                | ss when String.IsNullOrEmpty ss -> failwithf "Token secret is not set in the Environment"
                | ss -> ss
            | s -> s

        match config.AuthType with
        | Some AuthType.Token ->
            let token =
                match config.Secret with
                | Some secretEnvVar -> loadSecretEnv secretEnvVar
                | None -> failwithf "Secret is not specified. Usage: %s" usage

            match config.PrependLine with
            | Some h -> printfn "%s" h
            | None -> ()
            printfn "Authorization: %s" token

        | Some AuthType.MSAL ->
            match config.Secret with
            | Some envVar ->
                let auth : AppRegistration = envVar |> loadSecretEnv |> Json.Compact.deserialize
                let scopes = 
                    match auth.scopes with
                    | None -> [|sprintf "%s/.default" auth.client|]
                    | Some s -> s

                let cred =
                    let authBuilder = ConfidentialClientApplicationBuilder.Create(auth.client).WithClientSecret(auth.secret)
                    match auth.authorityUri with
                    | None ->
                        authBuilder.WithAuthority(AzureCloudInstance.AzurePublic, auth.tenant, true).Build()
                    | Some uri ->
                        authBuilder.WithAuthority(uri, auth.tenant, true).Build()

                let! r = cred.AcquireTokenForClient(scopes).ExecuteAsync() |> Async.AwaitTask

                match config.PrependLine with
                | Some h -> printfn "%s" h
                | None -> ()
                printfn "Authorization: %s" (r.CreateAuthorizationHeader())

            | None -> failwithf "Secret is not specified. Usage: %s" usage

        | None -> failwithf "Auth type is not set. Usage: %s" usage
    } |> Async.RunSynchronously
    0 // return an integer exit code
