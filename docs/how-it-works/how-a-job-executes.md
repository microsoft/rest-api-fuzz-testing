# How a job executes

Jobs are submitted to the front-end service via a job definition written in json. Every job has one or more tasks. The `taskConfiguration` 
portion of the job definition is information that is specific to the task that will run.

This data is passed to the task by being written into a file called `task-config.json` and placed into the file share that
is the working directory for the task. The task can then deserialize and validate the data as needed.

Under the CLI that is downloaded, you will find under the raft-utils/tools folder a folder for each installed tool. By default there are
tools for RESTler and ZAP. In the tool directory you will find a file called `config.json`. This file contains the configuration
data for how to run the tool.

Here is the example for the ZAP tool.

```
{
	"container" : "owasp/zap2docker-stable",
	"containerConfig" : {
		"CPUs" : 2,
		"MemorySizeInGB" : 1.0	
	},

	"run" : {
		"command" : "bash", 
		"arguments" : ["-c", 
		    "cd $RAFT_RUN_DIRECTORY; ln -s $RAFT_WORK_DIRECTORY /zap/wrk; python3 run.py install; python3 run.py" ]
	},
	"idle" : {
		"command" : "bash",
		"arguments" : ["-c", "echo DebugMode; while true; do sleep 100000; done;"]
	}
}
```

The "container" specifies the name of the docker container. The "containerConfig" allows you to specify the amount of compute resources you need for your tool.
The "run" structure allows you to specify the command to run and it's arguments on the container. The "idle" structure is what is run when
the tool is marked as "isDebug". 

The available commands and arguments are limited by what is available in the container you select.

The schema for the tool configuration is

```
    type ToolCommand = 
        {
            command : string
            arguments: string array
        }


    type ToolConfig =
        {
            container : string
            containerConfig : ContainerConfig option
            run : ToolCommand
            idle : ToolCommand
        }
```

## Environment Variables
A number of pre-defined environment variables are available in the container for your use. 

* RAFT_JOB_ID
* RAFT_TASK_INDEX
* RAFT_CONTAINER_GROUP_NAME
* RAFT_CONTAINER_NAME
* RAFT_APP_INSIGHTS_KEY
* RAFT_WORK_DIRECTORY
* RAFT_RUN_DIRECTORY
* RAFT_SB_OUT_SAS</br>
  This can be used to write event our to the service bus so your tool can generate job status and webhook data.

Paths specified in the environment variables are Linux specific paths. 

