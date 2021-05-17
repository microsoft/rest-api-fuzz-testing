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
    config = raft.task_config()

    raft_utils = raft.RaftUtils('ZAP')
    raft_utils.wait_for_agent_utilities()

    token = raft.auth_token()
    work_directory = os.environ['RAFT_WORK_DIRECTORY']

    i = 0
    test_target_config = config['targetConfiguration']
    endpoint = test_target_config.get('endpoint')

    n_targets = len(test_target_config.get("apiSpecifications"))
    for t in test_target_config.get("apiSpecifications"):
        print(f'Starting zap for target {t}')
        args = [sys.executable, "scan.py", f"{i}", f"{n_targets}", '--target', t]
        if token:
            args.extend(['--token', token])

        if endpoint:
            url = urlparse(endpoint)
            args.extend(['--host', url.netloc])

        subprocess.check_call(args)
        i = i + 1
