#### zap/running-against-raft

In this sample, the job description JSON will cause RAFT to execute `schemathesis` against the
RAFT service itself. This assumes that you've already deployed the RAFT service. 

Note that when you deployed the service, a secret, with correct values, called **RaftServicePrincipal** was
created for you.

The **run.py** script gives an example of how you can substitute your deployment name using the CLI
so that the job definition can stay generic. 

From the CLI directory to run this sample in you Azure RAFT deployment execute the sample with the command:
```
python run.py
```

To run this sample in your local docker service. You have to have RAFT Azure service deployed, since this is used as a service under test in this sample.
```
python run.py --local
```

or, using the CLI use (assuming a deployment name of `demo`):
```json
python raft.py job create --file samples/zap/running-against-raft/zap.json --substitute "{\"{defaults.deploymentName}\" : \"demo\"}"
```