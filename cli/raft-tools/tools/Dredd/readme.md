## Dredd configuration

- **config.json**
    
Required by RAFT in order to deploy Dredd tool

- **package.json**, **index.js**

Implementation of a Dredd driver that integrates with RAFT. The implementation uses utilities from `/cli/raft-tools/libs/node-js` for authentication implementation, logging and posting job status updates to RAFT.