# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(run_zap):
    cli = RaftCLI()
    run_zap_config = RaftJobConfig(file_path=run_zap,
                        substitutions={'{defaults.deploymentName}' : cli.definitions.deployment})
    zap_job = cli.new_job(run_zap_config)
    print(zap_job)
    cli.poll(zap_job['jobId'])

if __name__ == "__main__":
    run(os.path.join(cur_dir, "raft.zap.json"))