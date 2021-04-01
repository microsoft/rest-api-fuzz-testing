// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Microsoft.FSharpLu
open Newtonsoft

type RESTlerBugFilePosition =
    | Intro
    | Request

type RestlerRequestDetails =
    {
        producerTimingDelay : int option
        maxAsyncWaitTime : int option

        previousResponse : string option
    }

    static member Empty =
        {
            producerTimingDelay = None
            maxAsyncWaitTime = None

            previousResponse = None
        }



type RestlerRequest =
    {
        httpMethod : string
        query :string
        headers : Map<string, string>
        body: string option

        restlerRequestDetails : RestlerRequestDetails
    }


type RESTlerBug =
    {
        bugName : string option
        bugHash: string option

        requests : RestlerRequest list
    }

    static member Empty =
        {
            bugName = None
            bugHash = None
            requests = []
        }

module Constants =
    let [<Literal>] IntroSeparator = "################################################################################"
    let [<Literal>] Hash = "Hash:"
    let [<Literal>] Request = "->"
    let [<Literal>] PreviousResponse = "PREVIOUS RESPONSE:"
    let [<Literal>] Host = "Host:"
    let [<Literal>] Accept = "Accept:"
    let [<Literal>] ContentType = "Content-Type:"

    module RequestConfig =
        let [<Literal>] ProducerTimerDelay = "! producer_timing_delay"
        let [<Literal>] MaxAsyncWaitTime = "! max_async_wait_time"




let parseRequest (x: string) =
    let r = x.Substring(Constants.Request.Length).Split("\\r\\n") |> List.ofArray
    let method, query =
        match ((List.head r).Trim().Split(' ')) |> List.ofArray with
        | m :: q :: _ -> m.Trim(), q.Trim()
        | _ -> failwithf "Unhandled RESTler log format (expected HTTP method followed by query): %s" (r.[0])

    //from r.[1] to end, but if "" line and not last one -> then next one is body
    let rec collectHeaders (headers: Map<string, string>) (rs: string list) =
        let parseHeader (h: string) =
            match h.Split(": ") with
            | [|k; v|] -> Some(k.Trim(), v.Trim())
            | _ -> None

        match rs with
        | "" :: body :: "" :: [] | "" :: body :: [] -> headers, Some body
        | r :: "" :: [] -> 
            match parseHeader r with
            | Some(k, v) ->
                (Map.add k v headers), None
            | None -> headers, None
        | r :: rs ->
            match parseHeader r with
            | Some(k, v) -> collectHeaders (Map.add k v headers) rs
            | None -> collectHeaders headers rs
        | [] -> headers, None

    let headers, body = collectHeaders Map.empty (List.tail r)
    let body =
        match body with
        | Some s ->
            if  String.IsNullOrWhiteSpace s then
                None
            else
                let b = Json.Compact.deserialize(s.Replace("\\n", ""))
                Some(b.ToString())
        | None -> None

    {|
        HttpMethod = method
        Query = query
        Headers= headers
        Body = body
    |}



let parseRESTlerBugFound(bugFound: string list) =

    let rec parse (xs: string list) (bugDefinition : RESTlerBug) (pos: RESTlerBugFilePosition) =
        match pos, xs with
        | Intro, (Constants.IntroSeparator::[]) -> None

        | Intro, (Constants.IntroSeparator::y::xs) when not (String.IsNullOrWhiteSpace y) ->
            parse xs {bugDefinition with bugName = Some y} Intro

        | Intro, x::xs when x.Trim().StartsWith(Constants.Hash) ->
            parse xs {bugDefinition with bugHash = Some (x.Trim().Substring(Constants.Hash.Length)) } Intro

        | Intro, (Constants.IntroSeparator::y::xs) when String.IsNullOrWhiteSpace y ->
            parse xs bugDefinition Request

        // Skip rest of bug definition intro
        | Intro, x::xs ->
            if not (String.IsNullOrWhiteSpace x) then
                printfn "Ignoring: %s" x
            parse xs bugDefinition Intro

        | Request, (x :: xs) when String.IsNullOrEmpty x -> parse xs bugDefinition Request

        | Request, (x :: xs) when x.StartsWith Constants.Request ->
            let r = parseRequest x 

            let rec restlerRequestDetails (xs: string list) (requestDetails: RestlerRequestDetails) =
                match xs with
                | y :: ys when y.StartsWith(Constants.RequestConfig.MaxAsyncWaitTime) ->
                    let t = y.Substring(Constants.RequestConfig.MaxAsyncWaitTime.Length)
                    match Int32.TryParse t with
                    | true, n -> restlerRequestDetails ys {requestDetails with maxAsyncWaitTime = Some n}
                    | false, _ -> failwithf "Expected max async wait time to be an integer: %s" y

                | y :: ys when y.StartsWith(Constants.RequestConfig.ProducerTimerDelay) ->
                    let t = y.Substring(Constants.RequestConfig.ProducerTimerDelay.Length)
                    match Int32.TryParse t with
                    | true, n -> restlerRequestDetails ys {requestDetails with producerTimingDelay = Some n}
                    | false, _ -> failwithf "Expected producer timing delay to be an integer: %s" y

                | y :: ys when y.StartsWith(Constants.PreviousResponse) ->
                    {requestDetails with previousResponse = Some (y.Substring(Constants.PreviousResponse.Length))}, ys

                | s :: _ -> failwithf "Unhandled RESTler request details : %s" s
                | ss -> failwithf "Unhandled case when processing RESTler request details : %A" ss


            let requestDetails, rest = restlerRequestDetails xs RestlerRequestDetails.Empty

            let request: RestlerRequest =
                {
                    httpMethod = r.HttpMethod
                    query = r.Query
                    headers = r.Headers
                    body = r.Body
                    restlerRequestDetails = requestDetails
                }

            parse rest { bugDefinition with requests = bugDefinition.requests @ [request] } Request

        | _ -> Some bugDefinition

    parse (bugFound |> Seq.toList) RESTlerBug.Empty Intro




module Postman =
    type Name = Json.JsonPropertyAttribute

    type Info =
        {
            [<Name("_postman_id")>]
            PostmanId: System.Guid
            [<Name("name")>]
            Name: string
            [<Name("schema")>]
            Schema: string
        }

        static member Create(name: string) =
            {
                PostmanId = System.Guid.NewGuid()
                Name = name
                Schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
            }


    type Query =
        {
            [<Name("key")>]
            Key : string
            [<Name("value")>]
            Value : string
        }


    type Url =
        {
            [<Name("raw")>]
            Raw : string
            [<Name("protocol")>]
            Protocol: string
            [<Name("host")>]
            Host : string array
            [<Name("path")>]
            Path : string array
            [<Name("query")>]
            Query : Collections.Specialized.NameValueCollection option
        }



    type BodyLanguage =
        {
            [<Name("language")>]
            Language : string
        }

    type BodyOptions =
        {
            [<Name("raw")>]
            Raw : BodyLanguage
        }

    type Body =
        {
            [<Name("mode")>]
            Mode: string
            [<Name("raw")>]
            Raw: string
            [<Name("options")>]
            Options : BodyOptions
        }

    type Header =
        {
            [<Name("key")>]
            Key : string
            [<Name("value")>]
            Value : string
        }

    type Request =
        {
            [<Name("method")>]
            Method : string
            [<Name("header")>]
            Header : Header array
            [<Name("body")>]
            Body : Body option
            [<Name("url")>]
            Url : Url
        }

    type Response =
        {
            [<Name("code")>]
            Code : int
        }

    type SystemHeaders =
        {
            [<Name("accept")>]
            Accept : bool

            [<Name("content-type")>]
            ContentType : bool
        }

    type ProtocolProfileBehaviour =
        {
            [<Name("disabledSystemHeaders")>]
            DisabledSystemHeaders: SystemHeaders
        }


    type Item =
        {
            [<Name("name")>]
            Name : string
            [<Name("protocolProfileBehavior")>]
            ProtocolProfileBehavior : ProtocolProfileBehaviour
            [<Name("request")>]
            Request : Request
            [<Name("response")>]
            Response : Response array
        }


    type Collection =
        {
            [<Name("info")>]
            Info : Info
            [<Name("item")>]
            Item : Item list
        }

    let convertToPostmanFormat (useSsl: bool) (name: string) (bug : RESTlerBug) =
        let info = Info.Create(name)
        let requests =
            bug.requests
            |> List.map(fun r ->
                let host = r.headers.["Host"]
                let headers = r.headers.Remove("Host")
                {
                    Name = sprintf "%s:%s" r.httpMethod (r.query.Substring(0, (min 24 r.query.Length)))
                    ProtocolProfileBehavior = {DisabledSystemHeaders = {Accept = true; ContentType = true}}
                    Request =
                        {
                            Method = r.httpMethod
                            Header =
                                headers
                                |> Map.toArray
                                |> Array.map (fun (k, v) -> {Key = k; Value = v})

                            Body =
                                r.body |> Option.map(fun b ->
                                    {
                                        Mode = "raw"
                                        Raw = b
                                        Options = {Raw = {Language = "json"}}
                                    }
                                )

                            Url =
                                let protocol = if useSsl then "https" else "http"
                                let uri = System.Uri(sprintf "%s://%s%s" protocol host r.query)
                                let parsedQuery = System.Web.HttpUtility.ParseQueryString(uri.Query)
                                {
                                    Raw = uri.AbsoluteUri
                                    Protocol = protocol
                                    Host = host.Split('.')
                                    Path = uri.AbsolutePath.Split('/') |> Array.filter(fun s -> not <| String.IsNullOrEmpty s)
                                    Query =
                                        if parsedQuery.Count = 0 then
                                            None
                                        else
                                            Some parsedQuery
                                }
                        }
                    Response = [||]
                }
            )

        {
            Info = info
            Item = requests
        }


module CommandLine =
    let [<Literal>] UseSSL = "--use-ssl"
    let [<Literal>] RESTlerBugBucket = "--restler-bug-bucket-path"

    let parse (args: string array) =
        let rec parse (r : {| UseSsl : bool; RESTlerBugBucketPath : string option |}) (xs : string list) =
            match xs with
            | [] -> r
            | UseSSL :: xs ->
                parse {| r with UseSsl = true |} xs
            | RESTlerBugBucket :: p :: xs ->
                parse {| r with RESTlerBugBucketPath = Some p |} xs
            | s -> failwithf "Unhandled command line parameters: %A" s

        let r = parse {| UseSsl = false; RESTlerBugBucketPath = None |} (List.ofArray args)
        match r.RESTlerBugBucketPath with
        | None ->
            failwithf "Expected RESTler bug bucket as an input. Parameters: %s %s [path]" UseSSL RESTlerBugBucket
        | Some p -> {| UseSSL = r.UseSsl; RESTlerBugBucketPath = p |}


[<EntryPoint>]
let main argv =
    let config = CommandLine.parse argv
    let fileContents =
        use stream = System.IO.File.OpenText(config.RESTlerBugBucketPath)
        [
            while not stream.EndOfStream do
                yield stream.ReadLine().Trim()
        ]

    match parseRESTlerBugFound fileContents with
    | None ->
        printfn "Failed to parse RESTler bug information from : %s" config.RESTlerBugBucketPath
        1
    | Some bug ->
        let bugFileName = System.IO.Path.GetFileNameWithoutExtension(config.RESTlerBugBucketPath)
        let p = System.IO.Path.ChangeExtension(config.RESTlerBugBucketPath, ".postman.json")
        let postmanCollection = Postman.convertToPostmanFormat config.UseSSL (sprintf "%s [%s]" bugFileName bug.bugHash.Value) bug
        printfn "Writing Postman collection to: %s" p
        Json.Compact.serializeToFile p postmanCollection
        0 // return an integer exit code