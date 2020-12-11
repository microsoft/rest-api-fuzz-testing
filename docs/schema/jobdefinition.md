# Job Definition file

Jobs are defined by a definition json. You can view the schema by looking at our
swagger UI. After the service is deployed, point your browser to the APIService to see
the swagger UI. 

For example if you deployment is named "myDeployment' point your browser to
`https://mydeployment-raft-apiservice/swagger` 
This will display a web page with our REST methods and their schema's.

The schema is defined in the code file DTOs.fs.

Most fields in the job definition are optional, only a few are required.

Here is an example job definition to fuzz a service with RESTler.
```
{
  "swagger": { 
	"$type": "URL",
	"value" : "https://my-appservice.azurewebsites.net/swagger/v1/swagger.json" 
  },
  "duration": "00:10:00",
  "host": "my-appservice.azurewebsites.net",
  "rootFileshare" : "myshare-fuzz",
  "webhook" : "myservice-fuzz",
  "tasks": [
    {
      "toolName" : "RESTler",
      "nameSuffix" : "-RESTler-fuzz",
      "taskConfiguration" : {
        "task": "Fuzz",
        "agentConfiguration": {
          "resultsAnalyzerReportTimeSpanInterval": "00:01:00"
        },

        "runConfiguration": {
          "previousStepOutputFolderPath": "/job-compile/0-RESTler-Compile",
          "useSsl": true,
          "producerTimingDelay": 5
        }
      }
    },
    {
      "toolName" : "RESTler",
      "taskConfiguration" : {
        "task": "Fuzz",
        "agentConfiguration": {
          "resultsAnalyzerReportTimeSpanInterval": "00:01:00"
        },

        "runConfiguration": {
          "previousStepOutputFolderPath": "/job-compile/0-RESTler-Compile",
          "useSsl": true,
          "producerTimingDelay": 2
        }
      }
    }
  ]
}
```

## Swagger (required object)

Defines where to find the swagger file. This can be a Uri, or a file path which must reference
a file that is mounted on the container. 

```
  "apiSpecifications": [ 
	  "https://someurl/swagger.json",
    "/folderA/folderB/swagger.json" 
  ]
```

## Tasks (required object)

The task definition defines the tool that will run.  This is an array of definitions. Up to 60 task
definitions can be defined in a single job. 

### ToolName (required string)

Name of the tool. The two tools that are supported by default are `RESTler` and `ZAP`

### Swagger (optional object)

This swagger definition is the same definition as at the job level. This gives you the option
of specifying an alternate swagger file for this tool.

### Host (optional string)

Overwrite the host in every request by ZAP or RESTler instead of using the host value 
in the swagger specification.

### IsIdling (optional bool)

If this boolean value is set, the container will be started using the "idle" command in the
tool configuration file. See the configuration files for tools under the raft_utils/tools folder.

This setting is often used for interactively debugging the tool or examining what is on the container.
Access to the container is available through the Azure [Portal](https://portal.azure.com). 

The container will be created in the resource group which contains the resources for the service.
After clicking on the container you will see where you can connect to it interactively right in
the browser.

### Duration (optional TimeSpan)

Specify the duration for the task to run.

### AuthTokenConfiguration (optional object)

This object is used to define the kind of secret you are using, where it is located, and any
refresh interval if needed.

See details on [authentication](authentication.md)


### KeyVaultSecrets (optional string)

Key vault secrets must start with an alphabetic character and be followed by
a string a alphanumeric characters. Secret names can be up to 127 characters long.

The key vault is used to hold your authentication secrets. Add your secret to the key vault using
any name for the secret you like. 

Your secret should be in the form expected for the type of secret you specify in the
authTokenConfiguration object.


## NamePrefix (optional string)

The “namePrefix” is prepended to the Job ID guid.
This makes finding results in storage easily searchable by the prefix.
By combining the prefix with the timestamp it becomes easy to find job results. 

Some characters are **not allowed** in the namePrefix, these are forward slash (/), 
backslash (\), number sign (#), question mark (?), control characters from U+000 
to U+001F including tab (\t), linefeed (\n), and carriage return (\r), 
and control character from U+007F to U+009F.

`"namePrefix" : "someUniquePrefix"`

## Host (optional string)

Overrides the host value defined in the swagger file.

`"host": "myservice.azurewebsites.net"`

## Webhook (optional string)

The tag used to send webhooks.

`"webhook" : "myhook"`

## Metadata (optional string)

Key/value pairs to include in webhook messages. This can be helpful when wanting to trace
who did a particular fix, or which branch is actually under test. These can be values from your 
CI/CD pipeline. 

`"metadata": {"key" : "value", "author" : "john", "branch" : "mybranch"}`

## Duration (optional string)

This is a timespan value that defines how long a job should run. 
If undefined it will run until completion (or forever if the task never completes). 
For RESTler jobs, the the limit is only useful for Fuzz tasks. 
The timespan format is defined [here](https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings).

This example shows days.hours:minutes:seconds

`"duration": "0.00:10:00"`

## ReadOnlyFileShareMounts (optional string)

Used to mount a file share read-only within the container. 
The system will mount a file share from the storage account into the container for 
tools to access. You can specify the name of the share to mount and the name it should be 
mounted as within the container. In this example the file share name in the storage 
account is “service-biz-swagger”. 

File share names have certain restrictions. See [here](https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-shares--directories--files--and-metadata). 
The mount path is the name under which it will be available within the container. 

```
"readOnlyFileShareMounts": [{
            "FileShareName": "service-biz-swagger",
            "MountPath": "/swagger"
        }
    ]
```

## ReadWriteFileShareMounts (optional string)

Used in the same way as the readOnlyFileShareMounts, but within the container 
it’s mounted as a read-write share. 

```"readWriteFileShareMounts": [{
            "FileShareName": "service-biz-swagger",
            "MountPath": "/swagger"
        }
    ]
```

## RootFileShare (optional string)

When specified the results will be organized by jobId under this file share. 
This is an easy way to collect similar runs together. Also since file shares can be 
mounted onto your local dev machine, it is easy to browse all the log files or to 
use utilities to parse the log files. 
Name requirements are specified [here](https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-shares--directories--files--and-metadata#share-names)

`"rootFileshare" : "mysharename"`