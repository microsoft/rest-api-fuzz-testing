# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(replay, fuzz_job_id, replay_job_id=None):
    cli = RaftCLI()
    substitutions = {
        '{defaults.deploymentName}': cli.definitions.deployment,
        '{jobRunId}' : fuzz_job_id
    }
    replay_job_config = RaftJobConfig(file_path=replay, substitutions=substitutions)

    print('Replay')
    isIdle = False
    for task in replay_job_config.config['tasks']:
        isIdle = isIdle or task['isIdling']

    if isIdle and replay_job_id:
        cli.update_job(replay_job_id, replay_job_config)
        print(f'Idle Job: {replay_job_id}')
    else:
        # create new fuzz job configuration
        replay_job_id = cli.new_job(replay_job_config)
        if isIdle:
            print(f'New Idle Job: {replay_job_id}')
        else:
            print(f'New Job: {replay_job_id}')

    if not isIdle:
        # wait for job ID from fuzz_job to finish the run
        cli.poll(replay_job_id['jobId'])


if __name__ == "__main__":
    run(replay = "raft.restler.replay.json",
        #job ID that produced bugs and those bugs going to be replayed
        fuzz_job_id = "d29c7a2a-1815-4edb-91c1-56dd4faea0ce",
        replay_job_id=None)
    