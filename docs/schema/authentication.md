# Authentication

When defining tasks, you can define the authentication secrets needed for the tool to authenticate
against your service. The secrets are described in the json and live in the key vault. 


> [!NOTE]
> Some secrets are be passed to the container via environment variables (MSAL and TxtToken).
> Because environment variables cannot be named with a dash "-', 
> you must not use a dash in your secret name.

## MSALConfig

Here is an example of using [MSAL](https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-overview) for authentication.

``` 
"tasks": [
    {
      "toolName" : "RESTler",
      "keyVaultSecrets": [ "MyServicePrincipal" ],
      "authenticationMethod": {
        "MSAL": "MyServicePrincipal"
      }
    },
    ...
```

In this example the secret "RaftServicePrincipal" in the key vault must contain the data needed
for **MSAL**. 

The MSAL configuration JSON blob stored in the key vault should be in this form:

```
{
  "client": "<your client guid>", 
  "tenant": "<your tenant guid>", 
  "secret": "<your secret string>"
  "scopes": ["example/.default"]
  "authorityUri" : "<your authority uri>"
}
```
The `client`, `tenant`, and `secret` fields are mandatory.

The optional `scopes` field is an array of strings and has a default value of `["{client}/.default"]` 
where `{client}` is the value of the client field in the structure.

The optional `authorityUri` field is a string and has a default value of 
"https://login.microsoftonline.com/{tenant}" where `{tenant}` is the tenant field in the structure. 

The JSON blob is passed to the container in an environment variable. 

## TxtToken

Here is an example of using **TxtToken** for authentication.
``` 
"tasks": [
    {
      "toolName" : "myTool",
      "keyVaultSecrets": [ "MySecretToken" ],
      "authenticationMethod": {
        "TxtToken": "MySecretToken"
      }
    },
    ...
```

If your authentication takes a static secret use **TxtToken**, the secret in the
key vault is expected to be a string.

The string is passed to the container in an environment variable. 

## CommandLine

Here is an example of using **CommandLine** for authentication.

```
"tasks": [
    {
      "toolName" : "myTool",
      "authenticationMethod": {
        "CommandLine" : "GetMyToken.py"
      }
    },
    ...
```

The command line type allows you to define your own command to create authentication data.
When you provide your own command, you must provide all dependencies needed for that command
to be run on the container. In this example the command is a python script, it would be nessasary
to ensure python was installed on the container for it to run successfully. 

You can put dependencies on the mounted file share along with your command.

The command line is written out in the **task-config.json** file.
See [Referencing the task-config.json file](../how-to-onboard-a-tool.md) section for details on
the **task-config.json** file.

## How to use the secret in your agent

Secrets kept in the key vault. This is a secure place for you to keep secrets and manage access
to them. 

When a container is created to run a task, an environment variable will be created for each
secret used in the task. The name of the environment variable will be **RAFT_[key vault secret name]**.
As noted above do not use dashes in your secret name. 

Information about the secret name and secret type are saved in the **task-config.json** file. The
agent will need to deserialize this file to get at these details.
