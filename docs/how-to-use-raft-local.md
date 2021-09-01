# How to run RAFT on your local machine

There are times where you might want to test your service on your development machine before
committing the changes to run in a CI/CD pipeline with your Azure deployment of RAFT. The
`raft_local.py` script is your way to do that. 

Because RAFT uses containers for the testing tools, this script will pull down the tools needed
and run them using docker on your local machine. 

### Requirements

This script has been tested with these versions of these tools.
* Python (3.8) 
* Docker (20.10)

### Limitations

This script only supports the **job create** command. See `raft_local.py --help` for details.

### Getting Started

Run `raft_local.py local init` to auto-create required directories.

These are the directories that will be created and are expected by the script.
```Text
CLI //root folder where you downloaded the CLI
|- raft_local.py // raft_local.py script 
|- local         // folder named local located at the same level as raft_local.py
      |- secrets     // folder named secrets located inside of local folder
      |- storage     // folder names storage located inside of local folder
      |- events_sink // folder used to synchronize test tool events
```


* The `storage` folder will contain all of the data produced by RAFT job runs. 
* The `secrets` folder is a user maintained folder. </br>
  The files in this folder are the names of the secret used in the job definition file.
  These files should not have an extension.</br>
  **Note:** The contents of the secret file must not contain line breaks. This data is passed
  on the docker command line. Line breaks will cause the docker command to fail. 

  For example if my RAFT job configuration requires a text token. 
  I can store the token in file `MyToken` under `CLI/local/secrets/MyToken` and use `MyToken` 
  as the secret name in RAFT job configuration. 

  ```
      "authenticationMethod": {
        "TxtToken": "MyToken"
      }
  ```

### Running raft_local.py

Once you have completed the getting started portion you can use `raft_local.py` to 
run a job on your local machine.

Here is an example job definition that runs RESTler and ZAP on a
deployed instance of [PetStore](https://petstore3.swagger.io) that can be launched with
`raft_local.py`


```text
{
  "testTasks": {
    "targetConfiguration": {
      "apiSpecifications": [ "https://petstore3.swagger.io/api/v3/openapi.json" ],
      "endpoint": "https://petstore3.swagger.io"
    },
    "tasks": [
      {
        "toolName": "RESTler",
        "outputFolder": "restler-logs",
        "toolConfiguration": {
          "tasks": [
            {
              "task": "compile"
            },
            {
              "task": "Fuzz",
              "runConfiguration": {
                "Duration": "00:10:00"
              }
            }
          ]
        }
      },
      {
        "toolName": "ZAP",
        "outputFolder": "zap-logs"
      }
    ]
  }
}
```

Example command line to create a local job:</br>
`python raft_local.py job create --file <jobdefinitionfile>`

### Telemetry
To prevent sending anonymous telemetry when running locally use the `--no-telemetry flag`.