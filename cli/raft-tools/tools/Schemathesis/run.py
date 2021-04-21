import json
import os
import subprocess
import sys
from urllib.parse import urlparse

run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
raft_libs_dir = os.path.join(run_directory, '..', '..', 'libs', 'python3')
sys.path.append(raft_libs_dir)
import raft

if __name__ == "__main__":
    #sys.path.append('/tmp/usr/local/lib/python3.9/site-packages')
    #raft.install_certificates()
    token = raft.auth_token()
    config = raft.task_config()

    raft = raft.RaftUtils('schemathesis')
    raft.wait_for_agent_utilities()

    work_directory = os.environ['RAFT_WORK_DIRECTORY']

    i = 0
    test_target_config = config['targetConfiguration']
    endpoint = test_target_config.get('endpoint')

    n_targets = len(test_target_config.get("apiSpecifications"))
    for t in test_target_config.get("apiSpecifications"):
        print(f'Starting schemathesis for target {t}')
        args = ["schemathesis", "run", "--stateful", "links", "--checks", "all"]
        if token:
            args.extend(["-H", f"Authorization: {token}"])

        if endpoint:
            args.extend(['--base-url', endpoint])

        cassette = f'cassette-{i}.yaml'

        args.extend(["--store-network-log", f'{work_directory}/{cassette}', t])
        print(f'Running with args: {args}')
        raft.report_status_running({"endpoint" : endpoint})
        result = subprocess.run(args)
        if (result.returncode == 1):
            raft.report_bug({"cassette": f'config.outputFolder/cassette'})
        print(f'Finished run on API specification: {t}')
        i = i + 1 

    raft.report_status_completed()
    raft.flush()