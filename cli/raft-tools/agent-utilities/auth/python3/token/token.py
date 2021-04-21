import os
import sys

def token_from_env_variable(env_variable_name):
    auth_params = os.environ.get(env_variable_name)
    if auth_params:
        #print("Token retrieved")
        return auth_params
    else:
        raise Exception(f"Token environment variable is not set {env_variable_name}")

if __name__ == "__main__":
    token = token_from_env_variable(sys.argv[1])
    print(token)