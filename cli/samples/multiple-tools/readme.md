#### multiple-tools

This sample runs all tools supported by `raft`. It runs some of the tools against your Azure RAFT deployment.

You can run the sample in your local docker service by executing `python run.py --local`, but you have to have RAFT Azure deployment available, since it is going to be used as a service under test.

You can run the sample in your Azure RAFT deployment by executing `python run.py`.

Python script extracts your raft endpoint definition from `defaults.json`, patches `compile.yaml` job configuration file with the `raft` service endpoint and executes the compile job. It also runs `dredd` on `https://petstore.swagger.io` endpoint. Since any deployment of the `raft` service requires authentication when calling it's APIs - `fuzz.json` job configuration file passes `MSAL` as authentication method to `ZAP` and `RESTler` tasks. Check secrets of the keyvault deployed as part of the `raft` service for definition of the service principal that is used by `ZAP` and `RESTler` to authenticate against raft.

When the job reaches the `Created` state it will print a links to Azure storage file share that will contain output produced by `comple.yaml` job and `fuzz.json` job. 