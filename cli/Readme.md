# RAFT CLI
`raft.py` is Rest API Fuzz Testing service CLI that is used for deployment of the service to Azure, job creation, deletion, etc. See full documentation [RAFT CLI reference](../docs/cli-reference.md)

`raft_local.py` is a script that does not require Azure to run RAFT job configurations. This script requires installation of Python and Docker to run RAFT job configurations.

This script only support **job create** command. See `raft_local.py --help` for details.
`raft_local.py` expects the following folder structure (You can run `raft_local.py local init` to auto-create required directories)

```Text
CLI //root folder where you downloaded the CLI
|- raft_local.py // raft_local.py script 
|- local         // folder named local located at the same level as raft_local.py
      |- secrets // folder named secrets located inside of local folder
      |- storage // folder names storage located inside of local folder
```

`storage` folder will contain all of the data produced by RAFT job runs. 
`secrets` folder is a user maintained folder. It uses files without an extension to store data needed for authentication with services under test. For example if my RAFT job configuration requires a text token. I can store the token in file `MyToken` under `CLI/local/secrets/MyToken` and use `MyToken` string name in RAFT job configuration as `TxtToken` authentication method.
