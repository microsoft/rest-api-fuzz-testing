# Using the RAFT command line

The Raft command line interface is written in python and is simply an interface to the REST commands in the service.
Anything you can do with the CLI you can do with any tool that can interact with REST interfaces.
PostMan, curl, to mention a few.
The Raft CLI is both a command line interface which parses commands and executes them,
and an SDK which you can import and script the underlying functions yourself.

## CLI Commands

In our CLI syntax, values without two dashes "--" are positional parameters.
When they are separated by "|" as in [ red | blue  | yellow ] select one value.
Parameters with two dashes "--" are always optional

### General Parameters

These parameters apply to all commands.



**--secret \<secretValue\>**</br>
When **--skip-sp-deployment** is used, new secret generation is not executed.
However, the deployment will overwrite configuration settings for the APIService and the Orchestrator.
These services need to know the service principal secret.
Use this parameter to pass the secret to the deployment process.