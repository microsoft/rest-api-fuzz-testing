# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig, RaftJobError

def run(cli, compile, test, fuzz, replay):
    # Create compilation job configuration
    compile_job_config = RaftJobConfig(file_path=compile)
    print('Compile')
    # submit a new job with the Compile config and get new job ID
    compile_job = cli.new_job(compile_job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(compile_job['jobId'])

    subs = {}
    # use compile job as input for fuzz job
    subs['{compile.jobId}'] = compile_job['jobId']

    test_job_config = RaftJobConfig(file_path=test, substitutions=subs)
    print('Test')
    # create new fuzz job configuration
    test_job = cli.new_job(test_job_config)
    # wait for job ID from fuzz_job to finish the run
    cli.poll(test_job['jobId'])

    subs['{outputFolder}'] = 'fuzz'
    # create a new job config with Fuzz configuration JSON
    fuzz_job_config = RaftJobConfig(file_path=fuzz, substitutions=subs)
    print('Fuzz')
    # create new fuzz job configuration
    fuzz_job = cli.new_job(fuzz_job_config)

    # wait for job ID from fuzz_job to finish the run
    cli.poll(fuzz_job['jobId'])

    status = cli.job_status(fuzz_job['jobId'])
    experiment = None
    for message in status:
        if (message['agentName'] != message['jobId']):
            if message.get('details') and message['details'].get('numberOfBugsFound'):
                if (int(message['details']['numberOfBugsFound']) > 0):
                    experiment = message['details']['experiment']
                else:
                    print("Did not find any bugs")

    subs['{experiment}'] = experiment
    subs['{jobId}'] = fuzz_job['jobId']

    replay_job_config = RaftJobConfig(file_path=replay, substitutions=subs)
    replay_job = cli.new_job(replay_job_config)
    cli.poll(replay_job['jobId'])


if __name__ == "__main__":
    if '--local' in sys.argv:
        from raft_local import RaftLocalCLI
        cli = RaftLocalCLI(network='host')
    else:
        cli = RaftCLI()
    try:
        run(cli, os.path.join(cur_dir, "restler.compile.json"),
            os.path.join(cur_dir, "restler.test.json"), 
            os.path.join(cur_dir, "restler.fuzz.json"),
            os.path.join(cur_dir, "restler.replay-all-fuzz-bugs.json"))
    except RaftJobError as ex:
        print(f'ERROR: {ex.message}')