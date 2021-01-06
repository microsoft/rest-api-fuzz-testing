// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace DataStructuresConversionsTests

open System.Collections.Generic
open FsCheck
open FsCheck.Xunit

module DtoTypes =
    open Raft.Controllers.DTOs


    let authenticationMethod =
        Gen.oneof [
            gen {
                let! s = Arb.generate<NonNull<string>>
                return ({MSAL = s.Get; CommandLine = null; TxtToken = null}: AuthenticationMethod)
            }
        
            gen {
                let! s = Arb.generate<NonNull<string>>
                return ({MSAL = null; CommandLine = s.Get; TxtToken = null}: AuthenticationMethod)
            }

            gen {
                let! s = Arb.generate<NonNull<string>>
                return ({MSAL = null; CommandLine = null; TxtToken = s.Get}: AuthenticationMethod)
            }
        ]

    let fileShareMount =
        gen {
            let! fs = Arb.generate<NonWhiteSpaceString>
            let! m = Arb.generate<NonWhiteSpaceString>
            return ({FileShareName = fs.Get; MountPath = m.Get}:FileShareMount)
        }

    let metadata =
        gen {
            let! m = Arb.generate<Map<string, string>>
            let d: Dictionary<string, string> = 
                m 
                |> Microsoft.FSharpLu.Json.Compact.serialize 
                |> Microsoft.FSharpLu.Json.Compact.deserialize
            return d
        }

    let uri =
        gen {
            let! port = Arb.generate<FsCheck.PositiveInt>
            let! host = Arb.generate<FsCheck.HostName>
            
            let url = sprintf "https://%s:%d" (host.ToString()) port.Get
            return System.Uri(url)
        }

    let jObj =
        gen {
            return null
        }

    type DtoGen =
        static member ToolConfiguration() =
            {new Arbitrary<Newtonsoft.Json.Linq.JObject>() with
                override _.Generator = jObj
            }

        static member AuthMethod() =
            {new Arbitrary<AuthenticationMethod>() with
                override _.Generator = authenticationMethod
            }

        static member FileShareMount() =
            { new Arbitrary<FileShareMount>() with
                override _.Generator = fileShareMount
            }

        static member Metadata() =
            { new Arbitrary<Dictionary<string, string>>() with
                override _.Generator = metadata
            }

        static member Uri() =
            {   new Arbitrary<System.Uri>() with
                    override _.Generator = uri
            }


[<Properties( Arbitrary=[| typeof<DtoTypes.DtoGen> |] )>]
module Tests =

    let inline convert (j : ^T) =
        j |> Microsoft.FSharpLu.Json.Compact.serialize |> Microsoft.FSharpLu.Json.Compact.deserialize

    [<Property>]
    let ``Convert between internal and public datastructures``(dtoJobDefinition: Raft.Controllers.DTOs.JobDefinition)=
        let internalJobDefinition : Raft.Job.JobDefinition = convert dtoJobDefinition
        let dtoJobDefinition2 : Raft.Controllers.DTOs.JobDefinition = convert internalJobDefinition
        let internalJobDefinition2 : Raft.Job.JobDefinition = convert dtoJobDefinition2

        let s1 = dtoJobDefinition |> Microsoft.FSharpLu.Json.Compact.serialize
        let s2 = dtoJobDefinition2 |> Microsoft.FSharpLu.Json.Compact.serialize

        let i1 = internalJobDefinition |> Microsoft.FSharpLu.Json.Compact.serialize
        let i2 = internalJobDefinition2 |> Microsoft.FSharpLu.Json.Compact.serialize

        i1 = i2 && s1 = s2


    [<Property>]
    let ``Convert between internal and public job request``(dtoJobRequest: Raft.Controllers.DTOs.CreateJobRequest) =
        
        let internalJobRequest : Raft.Job.CreateJobRequest = convert dtoJobRequest
        let dtoJobRequest2 : Raft.Controllers.DTOs.CreateJobRequest = convert internalJobRequest
        let internalJobRequest2 : Raft.Job.CreateJobRequest = convert dtoJobRequest2

        let i1 = internalJobRequest |> Microsoft.FSharpLu.Json.Compact.serialize
        let i2 = internalJobRequest2 |> Microsoft.FSharpLu.Json.Compact.serialize

        let d1 = dtoJobRequest |> Microsoft.FSharpLu.Json.Compact.serialize
        let d2 = dtoJobRequest |> Microsoft.FSharpLu.Json.Compact.serialize

        i1 = i2 && d1 = d2
