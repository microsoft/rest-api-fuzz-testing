#### dredd/raft

This sample runs [dredd](https://github.com/apiaryio/dredd) on the your deployment of the `raft` service.

You can run the sample by executing `python run.py`. The python script extracts your raft endpoint definition from `defaults.json`, patches the `dredd.json` job configuration file with the `raft` service endpoint and executes the job. Since any deployment of the `raft` service requires authentication when calling it's APIs - job configuration file passes `MSAL` as the authentication method. Check the secrets of the keyvault deployed as part of the `raft` service for definition of the service principal that is used by `dredd` to authenticate against raft.

When the job reaches the `Created` state it will print a link to Azure storage file share that will contain reports produced by `dredd`. 
