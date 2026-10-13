import base64
import json
import os
import ssl
import sys

import requests
import websocket
from dotenv import load_dotenv
from urllib3.exceptions import InsecureRequestWarning

load_dotenv()

IP_TV = os.getenv("IP")
TOKEN = os.getenv("TOKEN_TV")

if not IP_TV:
    raise ValueError("A variável IP não foi definida no .env")

requests.packages.urllib3.disable_warnings(category=InsecureRequestWarning)
resposta = requests.get(f"https://{IP_TV}:8002/api/v2/", timeout=5, verify=False)
resposta.raise_for_status()
estado = resposta.json().get("device", {}).get("PowerState")

if estado != "on":
    print(f"A TV não está ligada (estado informado: {estado!r}); nenhum comando enviado.")
    sys.exit(0)

nome = base64.b64encode(b"MeuPC-Python").decode()
url = f"wss://{IP_TV}:8002/api/v2/channels/samsung.remote.control?name={nome}"
if TOKEN:
    url += f"&token={TOKEN}"

ws = websocket.create_connection(
    url,
    timeout=8,
    sslopt={"cert_reqs": ssl.CERT_NONE},
)

try:
    while True:
        evento = json.loads(ws.recv())
        if evento.get("event") == "ms.channel.connect":
            break
        if evento.get("event") == "ms.channel.unauthorized":
            raise RuntimeError("A TV recusou o controle. Confirme a autorização na tela da TV.")

    comando = {
        "method": "ms.remote.control",
        "params": {
            "Cmd": "Click",
            "DataOfCmd": "KEY_POWER",
            "Option": "false",
            "TypeOfRemote": "SendRemoteKey",
        },
    }
    ws.send(json.dumps(comando))
    print("Comando para desligar enviado.")
finally:
    ws.close()
