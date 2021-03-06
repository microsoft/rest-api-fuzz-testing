﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Learn more about F# at http://fsharp.org
namespace Raft.Main

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.IO
open System.Reflection
open Raft.Telemetry
open Raft
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore
open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.Extensibility
open Raft.Message
open Microsoft.Azure
open System
open Microsoft.Azure.ServiceBus.Core
open Microsoft.AspNetCore.Mvc
open Microsoft.OpenApi.Models
open Microsoft.AspNetCore.Http
open Swashbuckle.AspNetCore.SwaggerGen
open Microsoft.Identity.Web
open System.IdentityModel.Tokens.Jwt
open Microsoft.AspNetCore.Authentication.JwtBearer

module main = 

    let loadSchemaDocuments (settings : IConfiguration) = 
        async {
            let utilsFileShare = settings.["RAFT_UTILS_FILESHARE"]
            let utilsSas = settings.["RAFT_UTILS_SAS"]

            let directoryClient = Azure.Storage.Files.Shares.ShareDirectoryClient(utilsSas, utilsFileShare, "tools")

            let asyncEnum = directoryClient.GetFilesAndDirectoriesAsync().GetAsyncEnumerator()

            let rec loadAllConfigs(allConfigs) =
                async {
                    let! next = asyncEnum.MoveNextAsync().AsTask() |> Async.AwaitTask
                    if next then
                        if asyncEnum.Current.IsDirectory then
                            try
                                let fileClient = directoryClient.GetSubdirectoryClient(asyncEnum.Current.Name).GetFileClient("schema.json")
                                let! fileExists = fileClient.ExistsAsync() |> Async.AwaitTask
                                if fileExists.Value then
                                    let! fileStream = fileClient.DownloadAsync() |> Async.AwaitTask
                                    use r = new System.IO.StreamReader(fileStream.Value.Content)
                                    let! file = r.ReadToEndAsync() |> Async.AwaitTask
                                    return! loadAllConfigs (Map.add asyncEnum.Current.Name (Some file) allConfigs)
                                else
                                    return! loadAllConfigs (Map.add asyncEnum.Current.Name None allConfigs)
                            with ex ->
                                Central.Telemetry.TrackError(TelemetryValues.Exception(ex))
                                return! loadAllConfigs (Map.add asyncEnum.Current.Name None allConfigs)
                        else
                            return! loadAllConfigs allConfigs
                    else
                        return allConfigs
                }
            return! loadAllConfigs Map.empty
        }

    let getSchemaDocuments (schemaEntities: Map<string, string option>) =
        async {
            return 
                schemaEntities
                |> Map.map (fun tool schema -> 
                    match schema with
                    | None -> 
                        let noSchemaToolDoc = OpenApiDocument(Components = OpenApiComponents())
                        let noSchemaToolSchema = OpenApiSchema(Title = tool)
                        noSchemaToolDoc.Components.Schemas.Add(tool, noSchemaToolSchema)
                        noSchemaToolDoc
                    | Some s ->
                        let reader = Microsoft.OpenApi.Readers.OpenApiStringReader()
                        let schemaDoc, openApiDiagnostic = reader.Read(s)
                        if openApiDiagnostic.Errors.Count <> 0 then
                            raise <| Exception("Errors in the schema document")
                        schemaDoc
                )
        }

    type RaftSwaggerDocumentFilter() =
        interface IDocumentFilter with
            member this.Apply(swaggerDoc : OpenApiDocument, context : DocumentFilterContext): unit =
                let schemaDocuments = getSchemaDocuments (Utilities.toolsSchemas) |> Async.RunSynchronously
                schemaDocuments
                |> Map.iter(fun toolName schemaDocument ->
                            // Add all the schema references to the global schema repository if it does not already exist. 
                            for pair in schemaDocument.Components.Schemas do
                                if not (context.SchemaRepository.Schemas.ContainsKey(pair.Key)) then
                                    context.SchemaRepository.Schemas.Add(pair.Key, pair.Value)
                )

    type RaftSwaggerSchemaFilter() =
        interface ISchemaFilter with
            member this.Apply(schema: OpenApiSchema, context: SchemaFilterContext): unit =
                if not (isNull context.MemberInfo) &&
                   context.MemberInfo.Name = "ToolConfiguration" &&
                   context.MemberInfo.DeclaringType.Name = "RaftTask" then

                   let schemaDocuments = getSchemaDocuments (Utilities.toolsSchemas) |> Async.RunSynchronously
                   schemaDocuments
                   |> Map.iter(fun toolName schemaDocument ->
                                for pair in schemaDocument.Components.Schemas do
                                    // We will find the schema entry that matches the name of the tool
                                    // This entry then is #ref referenced to the RaftTask->TaskConfiguration "anyOf" schema
                                    if pair.Key = toolName then
                                        schema.AnyOf.Add(pair.Value)
                   )

    let configureCors (builder : CorsPolicyBuilder) =
        builder.WithOrigins("http://localhost:5001")
               .AllowAnyMethod()
               .AllowAnyHeader()
               |> ignore

    let configureApp (app : IApplicationBuilder) =
        app.UseAuthentication()
           .UseCors(configureCors)
           .UseExceptionHandler(fun options -> options.Run(RequestDelegate ErrorHandling.handleExceptions))
           .UseAuthentication()
           .UseMvc()
           .UseSwagger()
           .UseSwaggerUI(fun config ->
                config.ConfigObject.Urls <- [
                    Swashbuckle.AspNetCore.SwaggerUI.UrlDescriptor(Name = "RAFT API Json", Url = "/swagger/v2/swagger.json")
                    Swashbuckle.AspNetCore.SwaggerUI.UrlDescriptor(Name = "RAFT API Yaml", Url = "/swagger/v2/swagger.yaml")
                ]
            )
           |> ignore

        if Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") <> "Development" then
            app.UseHttpsRedirection() |> ignore

    let configureServices (services : IServiceCollection) =

        let serviceProvider = services.BuildServiceProvider()
        let settings = serviceProvider.GetService<IConfiguration>()
        let metricsKey = settings.["RAFT_METRICS_APP_INSIGHTS_KEY"]
        let siteHash = settings.["RAFT_SITE_HASH"]
        settings.["RestartTime"] <- DateTimeOffset.UtcNow.ToString()

        printfn "Initializing central telemetry"
        ignore <| Central.Initialize (TelemetryClient(new TelemetryConfiguration(metricsKey), InstrumentationKey = metricsKey)) siteHash

        Raft.Utilities.toolsSchemas <- loadSchemaDocuments settings |> Async.RunSynchronously
        Raft.Utilities.serviceStartTime <- System.DateTimeOffset.UtcNow
        Raft.Utilities.raftStorage <- Raft.Storage.RaftStorage(settings.[Constants.StorageTableConnectionString])
        Raft.Utilities.serviceBusSenders <- Map.empty
                                                  .Add(Raft.Message.ServiceBus.Queue.create,
                                                      ServiceBus.Core.MessageSender(
                                                          settings.[Constants.ServiceBusConnectionStringSetting], 
                                                          Raft.Message.ServiceBus.Queue.create, ServiceBus.RetryPolicy.Default) :> IMessageSender)
                                                  .Add(Raft.Message.ServiceBus.Queue.delete,
                                                      ServiceBus.Core.MessageSender(
                                                          settings.[Constants.ServiceBusConnectionStringSetting], 
                                                          Raft.Message.ServiceBus.Queue.delete, ServiceBus.RetryPolicy.Default) :> IMessageSender)
                                                  .Add(Raft.Message.ServiceBus.Topic.events,
                                                      ServiceBus.Core.MessageSender(
                                                          settings.[Constants.ServiceBusConnectionStringSetting], 
                                                          Raft.Message.ServiceBus.Topic.events, ServiceBus.RetryPolicy.Default) :> IMessageSender)


        services.AddApplicationInsightsTelemetry()
                .AddSwaggerGenNewtonsoftSupport()
                .AddSwaggerGen(fun config -> 
                    config.SwaggerDoc("v2", OpenApiInfo (Title = "RAFT", Version = "v2"))
                    config.UseOneOfForPolymorphism()

                    // Set the comments path for the Swagger JSON and UI.
                    let xmlFile = sprintf "%s.xml" (Assembly.GetExecutingAssembly().GetName().Name)
                    let xmlPath = Path.Join(AppContext.BaseDirectory, xmlFile);
                    config.IncludeXmlComments(xmlPath)
                    config.SchemaFilter<RaftSwaggerSchemaFilter>()
                    config.DocumentFilter<RaftSwaggerDocumentFilter>()
                )
                .AddCors()
                .AddMvc(fun options -> options.EnableEndpointRouting <- false)
                .SetCompatibilityVersion(CompatibilityVersion.Latest)
        |> ignore

        services.AddControllers().AddJsonOptions(fun options ->
                    options.JsonSerializerOptions.Converters.Add(Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase))
                )
                .AddNewtonsoftJson(fun options ->
                                        options.SerializerSettings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())

                                        options.SerializerSettings.Formatting <- Newtonsoft.Json.Formatting.Indented
                                        options.SerializerSettings.NullValueHandling <- Newtonsoft.Json.NullValueHandling.Ignore
                                        options.SerializerSettings.DateTimeZoneHandling <- Newtonsoft.Json.DateTimeZoneHandling.Utc
                                        options.SerializerSettings.ContractResolver <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                                        options.SerializerSettings.TypeNameHandling <- Newtonsoft.Json.TypeNameHandling.Auto
                )
        |> ignore

        // Enable logging of details on console window
        Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII <- true

        // This is required to be instantiated before the OpenIdConnectOptions starts getting configured.
        // By default, the claims mapping will map claim names in the old format to accommodate older SAML applications.
        // 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' instead of 'roles'
        // This flag ensures that the ClaimsIdentity claims collection will be built from the claims in the token
        JwtSecurityTokenHandler.DefaultMapInboundClaims <- false
        
        //https://aka.ms/ms-identity-web
        use config = 
            let mem = Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource()
            let p = Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider(mem) :> Microsoft.Extensions.Configuration.IConfigurationProvider

            let config = new Microsoft.Extensions.Configuration.ConfigurationRoot(ResizeArray [p])
            let section = config.GetSection("AzureAd")
            section.["ClientId"] <- settings.["RAFT_SERVICE_PRINCIPAL_CLIENT_ID"]
            section.["Instance"] <- "https://login.microsoftonline.com/"
            section.["TenantId"] <- settings.["RAFT_SERVICE_PRINCIPAL_TENANT_ID"]
            section.["Audience"] <- settings.["RAFT_SERVICE_PRINCIPAL_CLIENT_ID"]
            //https://github.com/AzureAD/microsoft-identity-web/wiki/web-apis#to-support-acl-based-authorization
            section.["AllowWebApiToBeAuthorizedByACL"] <- "true"
            config

        services.AddMicrosoftIdentityWebApiAuthentication(config) |> ignore

        // Pre-create the tables once when startup up so the running controllers does not need to.
        // This eliminates a bunch of 409's. 
        do Raft.Utilities.raftStorage.CreateTable Raft.StorageEntities.JobWebHookTableName |> Async.RunSynchronously |> ignore
        do Raft.Utilities.raftStorage.CreateTable Raft.StorageEntities.JobStatusTableName |> Async.RunSynchronously |> ignore
        do Raft.Utilities.raftStorage.CreateTable Raft.StorageEntities.JobTableName |> Async.RunSynchronously |> ignore

    let configureLogging (builder : ILoggingBuilder) =
        builder.AddConsole()      // Set up the Console logger
               .AddDebug()        // Set up the Debug logger
               .AddApplicationInsights()
        |> ignore

    let configureAppConfiguration(context: WebHostBuilderContext) (config: IConfigurationBuilder) =  

        config
         .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
         .AddJsonFile("appsettings.json", false, true)
         // When running locally, don't forget to add the appsettings.Development.json file and set the 
         // property to always copy.
         .AddJsonFile(sprintf "appsettings.%s.json" context.HostingEnvironment.EnvironmentName, true)
         .AddEnvironmentVariables() |> ignore

    [<EntryPoint>]
    let main argv =
        
        WebHost
            .CreateDefaultBuilder()
            .Configure(Action<IApplicationBuilder> configureApp)
            .ConfigureAppConfiguration(configureAppConfiguration)
            .ConfigureServices(configureServices)
            .ConfigureLogging(configureLogging)
            .UseUrls(sprintf "http://*:%s" (Environment.GetEnvironmentVariable("PORT")))
            .Build()
            .Run()
    
        0
