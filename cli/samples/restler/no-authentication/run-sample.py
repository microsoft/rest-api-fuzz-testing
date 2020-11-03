# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(compile, fuzz, host):
    # instantiate RAFT CLI
    cli = RaftCLI()

    # will replace {sample.host} with the value of host variable
    # see sample.restler.compile.json and sample.restler.fuzz.json
    subs = {
        '{sample.host}' : host
    }
    # Create compilation job configuration
    compile_job_config = RaftJobConfig(file_path=compile, substitutions=subs)
    # add webhook metadata that will be included in every triggered webhook by Compile job
    compile_job_config.add_metadata({"branch":"wizbangFeature"})
    print('Compile')

    # submit a new job with the Compile config and get new job ID
    compile_job = cli.new_job(compile_job_config)

    # wait for a job with ID from compile_job to finish the run
    cli.poll(compile_job['jobId'])

    # use compile job as input for fuzz job
    subs['{compile.jobId}'] = compile_job['jobId']

    # create a new job config with Fuzz configuration JSON
    fuzz_job_config = RaftJobConfig(file_path=fuzz, substitutions=subs)
    print('Fuzz')
    # add webhook metadata that will included in every triggered webhook by Fuzz job
    fuzz_job_config.add_metadata({"branch":"wizbangFeature"})
    # create new fuzz job configuration
    fuzz_job = cli.new_job(fuzz_job_config)

    # wait for job ID from fuzz_job to finish the run
    cli.poll(fuzz_job['jobId'])


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print('Please provide host under test as an argument that will be used to \
substitute {sample.host} in compile.json and fuzz.json config files')
    else:
        host = sys.argv[1]

    run(os.path.join(cur_dir, "sample.restler.compile.json"),
        os.path.join(cur_dir, "sample.restler.fuzz.json"), 
        host)