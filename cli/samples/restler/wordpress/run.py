# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os
import json

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig, RaftJobError, RaftDefinitions

def run(cli, config, subs):
    # Create compilation job configuration
    job_config = RaftJobConfig(file_path=config, substitutions=subs)
    print(f'Running {config}')
    # submit a new job with the Compile config and get new job ID
    job = cli.new_job(job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(job['jobId'])
    return job['jobId']

if __name__ == "__main__":
    try:
        # instantiate RAFT CLI
        cli = RaftCLI()
        compile_job_id = None

        subs = {
        }
        for arg in sys.argv[1:]:
            if arg == 'compile':
                compile_job_id = run(cli, os.path.join(cur_dir, 'compile.json'), subs)
                subs['{compile.jobId}'] = compile_job_id

            if arg == 'test':
                run(cli, os.path.join(cur_dir, "test.json"), subs), 

            if arg == 'test-fuzz-lean':
                run(cli, os.path.join(cur_dir, "test-fuzz-lean.json"), subs), 

    except RaftJobError as ex:
        print(f'ERROR: {ex.message}')
        sys.exit(1)