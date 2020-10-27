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

def run(compile, test, fuzz):
    cli = RaftCLI()
    substitutions = {
        '{defaults.deploymentName}': cli.definitions.deployment
    }
    compile_job_config = RaftJobConfig(file_path=compile, substitutions=substitutions)
    print('Compile')
    # create a new job with the Compile config and get new job ID
    # in compile_job
    compile_job = cli.new_job(compile_job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(compile_job['jobId'])
    substitutions['{compile.jobId}'] = compile_job['jobId']

    print('Test')
    test_job_config = RaftJobConfig(file_path=test, substitutions=substitutions)
    test_job = cli.new_job(test_job_config)
    cli.poll(test_job['jobId'])

    print('Fuzz')
    fuzz_job_config = RaftJobConfig(file_path=fuzz, substitutions=substitutions)
    fuzz_job = cli.new_job(fuzz_job_config)
    cli.poll(fuzz_job['jobId'])



if __name__ == "__main__":
    run(os.path.join(cur_dir, "raft.restler.compile.json"),
        os.path.join(cur_dir, "raft.restler.test.json"),
        os.path.join(cur_dir, "raft.restler.fuzz.json"))