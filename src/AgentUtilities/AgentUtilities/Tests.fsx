#r "System.Net.Http"

//let url = "https://localhost:49166"

let url = "https://petstore.swagger.io/v2/swagger.json"
let httpClient = new System.Net.Http.HttpClient(BaseAddress = System.Uri(url))

let post (path: string) (json: string) =
    let content = new System.Net.Http.StringContent(json)
    content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json")
    httpClient.PostAsync(path, content) |> Async.AwaitTask |> Async.RunSynchronously


post "/messaging/flush" ""


post "/messaging/trackTrace" """{"message" : "TestMessage", "severity" : "Information", "tags" : {"tag1":"abc", "tag2" : "def"}}"""

post "/messaging/event/jobStatus" """{"Tool" : "Test", "JobId" : "123", "AgentName" : "test", "State" : "Running", "ResultsUrl" : "http://www.blah.com", "UtcEventTime" : "4/22/2021 7:57:01 PM"}"""


open System
let downloadFile workDirectory filename (fileUrl: string) =
    async {
        use httpClient = new Net.Http.HttpClient(BaseAddress = (Uri(fileUrl)))
        let! response = httpClient.GetAsync("") |> Async.AwaitTask
        if not response.IsSuccessStatusCode then
            return failwithf "Get %s request failed due to '%A' (status code: %A)" fileUrl response.ReasonPhrase response.StatusCode
        else
            use! inputStream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask

            let filePath = workDirectory + "/" + filename
            use fileStream = IO.File.Create(filePath)
            do! inputStream.CopyToAsync(fileStream) |> Async.AwaitTask
            do! fileStream.FlushAsync() |> Async.AwaitTask
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

downloadFile "." "abc.json" url |> Async.RunSynchronously

let response = httpClient.GetAsync("") |> Async.AwaitTask |> Async.RunSynchronously
response.StatusCode


System.Math.Round(4.0 / (float 58), 1, System.MidpointRounding.ToEven);;