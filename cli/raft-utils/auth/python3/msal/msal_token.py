import msal
import os
import json
import sys

def get_token(client_id, tenant_id, secret, scopes, authority_uri):

    if authority_uri:
        authority = f"{authority_uri}/{tenant_id}"
    else:
        authority = f"https://login.microsoftonline.com/{tenant_id}"

    if not scopes:
        scopes = [f"{client_id}/.default"]
    app = msal.ConfidentialClientApplication(client_id, authority=authority, client_credential=secret)
    return app.acquire_token_for_client(scopes)

def token_from_env_variable(env_variable_name):
    auth_params = os.environ.get(f"RAFT_{env_variable_name}") or os.environ.get(env_variable_name)
    if auth_params:
        auth = json.loads(auth_params)
        token = get_token(auth['client'], auth['tenant'], auth['secret'], auth.get('scopes'), auth.get('authorityUri') )
        print("Getting MSAL token")
        return f'{token["token_type"]} {token["access_token"]}'
    else:
        print(f"Authentication parameters are not set in environment variable {env_variable_name}")
        return None

if __name__ == "__main__":
    token = token_from_env_variable(sys.argv[1])
    print(token)