#### dredd/petstore3

This sample runs [dredd](https://github.com/apiaryio/dredd) on the `petstore` - the basic Open API sample app. `Dredd` is deployed alongside `petstore` service docker container.

You can run the sample in your RAFT Azure deployment by executing `raft.py job create --file dredd.json --poll 10`.
You can run the sample in your local docker by executing `raft_local.py job create --file dredd.json --network host`.

When the job reaches the `Created` state it will print a link to Azure storage file share that will contain reports produced by `dredd` and logs from `petstore` service.
