## REST API fuzz testing service and RESTler

The RAFT team owns and maintains the RESTler agent that runs on RAFT platform.
In order to provide a consistent platform for supporting multiple test tools there are some differences between how RESTler is configured when running using RAFT versus when running RESTler directly.

RAFT supports all of the same configuration values that RESTler supports.
RESTler uses a mix of camel case and underscore parameters that it accepts as part of it's run configurations. On the other hand RAFT only uses camel case parameters. For example RESTler uses parameter `restler_custom_payload` where the same parameter is called `restlerCustomPayload` in RAFT job definition. 

RESTler documentation:
https://github.com/microsoft/restler-fuzzer/tree/main/docs/user-guide

When using RESTler documentation for configuring RESTler tasks you can use RAFT swagger definition for paramater name conversion.

*https://<my-deployment>-raft-apiservice.azurewebsites.net/swagger/index.html*


RESTler requires that IP and port number are specified in order to run a test. However, RAFT will do a DNS lookup on the host parameter, specified in the job definition file, on your behalf and fill in the IP parameter for you. RAFT also defaults the port number to 443 when using SSL and 80 when not using a secure connection. If for some reason you find that you still need to specify the IP and port number then manually provided values in TargetEndpoint configuration will override any lookup or default values.

## RESTler mode of opearation

First RESTler compiles Swagger specifications into RESTler grammar. The output of compile step can be consumed by any of the following steps: Compile, Test, Fuzz.
To enable passing of data from one step to the next RAFT allows any file share in the storage account to be mounted by any task.
This way RAFT jobs can be executed in a "pipeline" manner by passing the output of the Compile job as input Test, Fuzz or Compile jobs.

Compile job produces a job ID and a file share named the same as the job ID. This makes it possible for you to mount the output of one job as input to another.
The diagram below demonstrates how to pass output from the Compile step as input to the Test step of a RESTler task.

![RESTler ](images/restler-configs-flow.png)
