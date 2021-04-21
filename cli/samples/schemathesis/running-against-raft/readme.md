#### zap/running-against-raft

In this sample, the job description JSON will cause RAFT to execute `schemathesis` against the
RAFT service itself. This assumes that you've already deployed the RAFT service. 

Note that when you deployed the service, a secret, with correct values, called **RaftServicePrincipal** was
created for you.

The **run.py** script gives an example of how you can substitute your deployment name using the CLI
so that the job definition can stay generic. 

From the CLI directory run the sample with the command:
```python
python run.py
```
or, using the CLI use (assuming a deployment name of `demo`):
```json
python raft.py job create --file samples/zap/running-against-raft/zap.json --substitute "{\"{defaults.deploymentName}\" : \"demo\"}"
```