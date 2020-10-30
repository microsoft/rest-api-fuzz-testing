import json
import os
import subprocess
import sys

work_directory = os.environ['RAFT_WORK_DIRECTORY']
run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']

def auth_token(init):
    with open(os.path.join(work_directory, "task-config.json"), 'r') as task_config:
        config = json.load(task_config)
        auth_config = config.get("authenticationMethod")
        if auth_config:
            if auth_config.get("txtToken"): 
                token = os.environ.get(f"RAFT_{auth_config['txtToken']}") or os.environ.get(auth_config["txtToken"])
                return token
            elif auth_config.get("commandLine"):
                subprocess.getoutput(auth_config.get("commandLine"))
            elif auth_config.get("msal"):
                msal_dir = os.path.join(run_directory, "..", "..", "auth", "python3", "msal")

                if init:
                    print("Installing MSAL requirements")
                    subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(msal_dir, "requirements.txt")])
                else:
                    print("Retrieving MSAL token")
                    sys.path.append(msal_dir)
                    authentication_environment_variable = auth_config["msal"]
                    import msal_token
                    token = msal_token.token_from_env_variable( authentication_environment_variable )
                    if token:
                        print("Retrieved MSAL token")
                        return token
                    else:
                        print("Failed to retrieve MSAL token")
                        return None
            else:
                print(f'Unhandled authentication configuration {auth_config}')
    return None

if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "install":
        subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(run_directory, "requirements.txt")])
        auth_token(True)
    else:
        token = auth_token(False)
        import scan
        scan.run(token)