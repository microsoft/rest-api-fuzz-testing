# Authentication

When defining tasks, you can define the authentication needed by the task. 

The authentication data is described in the json and lives in the key vault. 

Here is an example of using MSAL for authentication.

```  "tasks": [
    {
      "toolName" : "RESTler",
      "isIdling" : false,
      "keyVaultSecrets" : ["AuthSecret1", "AuthSecret2"],
      "authTokenConfiguration": {
        "refreshIntervalSeconds": 300,
        "tokenRefreshCommand": {
          "$type" : "MSALConfig",
          "secretName" : "AuthSecret1"
        }
      },
```

In this example the secret "AuthSecret1" must contain the data needed for MSAL.

When the container is created each secret defined in the "keyVaultSecrets" array are retrieved and added as 
environment variables with the name "RAFT_" prepended. So for AuthSecret1 and AuthSecret2 you would find environment variables
"RAFT_AuthSecret1" and "RAFT_AuthSecret2" defined with the contents of their secrets from the key vault. 

These secret values can then be used to perform authentication as needed.

Because environment variables cannot be named with a dash "-', you must not use a dash in your secret name.

Some services need authentication refreshed at certain intervals. Use the **refreshIntervalSeconds** to define the
refresh frequency. 

The following authentication types are supported in the `$type` field. 
## MSALConfig

MSAL configuration stored in the key vault should be json in this form:

```
{
  "client": "<your client guid>", 
  "tenant": "<your tenant guid>", 
  "secret": "<your secret string>"
  "scopes": ["example/.default"]
}
```

The "scopes" value is optional. Use this if you require a specific scope for your service.

## TxtToken

If your authentication takes a static token string use TxtToken. In this case the secret in the
key vault should simply be the token string.

## CommandLine

The command line type allows you to define your own command to create authentication data.

You must provide all dependencies for your command. Put dependencies on a mounted file share as needed.

## Other options

For tools that support other authentication methods, the tool can define what is needed in the
taskConfiguration schema. This schema is completely tool dependent. 

For example in the RESTler configuration you might want to provide a static value in
a header value.

```
"compileConfiguration": {             
                "customDictionary": {                   
                    "customPayloadHeader": {
                        "<CustomAuthHeaderName>": ["<StaticAuthValue>"]
                    }
                }
            }
```