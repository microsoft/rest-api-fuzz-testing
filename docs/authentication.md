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
  "audience" : "<applicationId>"
}
```
The `client`, `tenant`, and `secret` fields are mandatory.

The optional `scopes` field is an array of strings and has a default value of `["{client}/.default"]` 
where `{client}` is the value of the client field in the structure. If you provide the `audience` field,
the scope will be set to the default value `["{audience}/.default"]`. This is useful when your
application registration service principal is different from the application you are targeting. 

**If you provide the `scopes` array with your own values, it will be used as you have defined it, no 
defaults will be applied.**

The optional `authorityUri` field is a string and has a default value of 
"https://login.microsoftonline.com/{tenant}" where `{tenant}` is the tenant field in the structure. 

The JSON blob is passed to the container in an environment variable. 

## Token

Here is an example of using **Token** for authentication.
``` 
"tasks": [
    {
      "toolName" : "myTool",
      "keyVaultSecrets": [ "MySecretToken" ],
      "authenticationMethod": {
        "Token": "MySecretToken"
      }
    },
```

If your authentication takes a static secret use **Token**, the secret in the
key vault is expected to be a string.

The string is passed to the container in an environment variable. 


## Agent-Utilities container
Starting with v4 of RAFT - a deicated utilities docker container is deployed with every job run. This container is responsible
for performing authentication with a service under test and processing events from tools.

You do not need to do anything special to update agent utilites when running RAFT local, since agent utilities used directly from your CLI folder. To update agent utiliites when
using RAFT Azure service you need to execute `py raft.py service update` in order to upload and apply the updated tools.

The agent utilities are located under `cli/raft-tools/agent-utilities`. And any authentication related code goes under `cli/raft-tools/agent-utilities/auth`.
When the job run starts the agent-utilities container is deployed and it listens on a port specified in `cli/raft-tools/agent-utilities/config.json`. If you change the port
RAFT Local or RAFT Azure service will apply that when new job is deployed.

In order to get authentication token for a secret tools onboarded to RAFT call GET on following url
```
<agent-utilities-url:port>/auth/<auth-type>/<secret>
```

For example to get MSAL authentication for a secret contained in RaftServicePrincipal
```
<agent-utilities-url:port>/auth/MSAL/RaftServicePrincipal
```

To get a Token stored in secret named MyToken
```
<agent-utilities-url:port>/auth/Token/MyToken
```

### Adding Custom Authentication
Under `cli/raft-tools/agent-utilities/auth` there are two shell scripts `msal.sh` and `token.sh`. The names of the shell scripts without extension is what `<auth-type>` maps when getting authenticaiton token over agent-utilities URL and `authenticationMethod` field key in the job configuration definition.

The first argument that is passed to the shell script is the environment variable name that contains the secret value and it maps to `<secret>` value specified in agent-utilities URL and `authenticationMethod` field value.

To add you own authentication mechanism - you just need to add your own shell script that uses secret defined by environment variable passed as the first parameter to the script, and prints the token to standard output.

For example if I want to add a new authentication method that is called `customAuth`.
1) I need to create `customAuth.sh` script file in `cli/raft-tools/agent-utilities/auth` that receives and environment variable as a parameter, uses environment variable to retrieve auth token (for example `msal.sh` calls a Python script to get a token) and prints the token to standard output.
2) Use new authentication method:
``` 
"tasks": [
    {
      "toolName" : "myTool",
      "keyVaultSecrets": [ "MySecret" ],
      "authenticationMethod": {
        "MyAuth": "MySecret"
      }
    }
```

### The simplest way to test authentication mechanism

Create a RAFT Local job with single test task and add `isIdling : true` property using `host` as network type:

```json
{
  "namePrefix": "sample-compile-",
  "testTasks": {
    "targetConfiguration" : {
      "apiSpecifications": [
        "https://petstore3.swagger.io/api/v3/openapi.json"
      ]
    },
    "tasks": [
      {
        "isIdling" : true,
        "keyVaultSecrets": [ "MySecret" ],
        "authenticationMethod": {
          "MyAuth": "MySecret"
        },
        "toolName": "RESTler",

        "outputFolder": "compile",
        "toolConfiguration": {
          "task": "Compile"
        }
      }
    ]
  }
}
```

```
python raft-local.py job create --file sample-compile.json --network host
```

When job is deployed, RESTler compile task will not be run, but will have an idle shell running. Remote shell to RESTler container and execute:
```
wget http://localhost:8085/auth/MyAuth/MySecret
``` 

This will call `MyAuth.sh` file in agent-utilities container, and this output of that will be saved by wget in MySecret file in the local directory where you executed wget.



## If you need to access secrets in your tools agent

Secrets kept in the key vault. This is a secure place for you to keep secrets and manage access
to them. 

When a container is created to run a task, an environment variable will be created for each
secret used in the task. The name of the environment variable will be **RAFT_[key vault secret name]**.
As noted above do not use dashes in your secret name. 

Information about the secret name and secret type are saved in the **task-config.json** file. The
agent will need to deserialize this file to get at these details.
