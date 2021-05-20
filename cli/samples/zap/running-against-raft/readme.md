#### zap/running-against-raft

In this sample, the job description JSON will cause RAFT to execute ZAP against the
RAFT service itself. This assumes that you've already deployed the RAFT service. 

Note that when you
deployed the service, a secret, with correct values, called **RaftServicePrincipal** was
created for you.

The raft-zap.py script gives an example of how you can substitute your deployment name using the CLI
so that the job definition can stay generic. 

From the CLI directory run the sample with the command:
```
python run.py
```
or, using the CLI use (assuming a deployment name of `demo`):
```json
python raft.py job create --file samples/zap/running-against-raft/zap.json --substitute "{\"{defa
ults.deploymentName}\" : \"demo\"}"


You can also run this in your local docker service by running, but you have to have RAFT Azure service deployed, since it is used as a service under test by the sample
```
python run.py --local
```