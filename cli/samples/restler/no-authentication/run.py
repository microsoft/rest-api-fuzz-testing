# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(cli, compile, fuzz):
    # will replace {sample.host} with the value of host variable
    # see sample.restler.compile.json and sample.restler.fuzz.json
    subs = {
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
    if '--local' in sys.argv:
        from raft_local import RaftLocalCLI
        cli = RaftLocalCLI(network='bridge')
    else:
        cli = RaftCLI()
    run(cli, os.path.join(cur_dir, "compile.json"),
        os.path.join(cur_dir, "fuzz.json"))