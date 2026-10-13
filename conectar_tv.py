
import base64
import json
import os
import ssl
import websocket

from dotenv import load_dotenv

load_dotenv()

IP_TV = os.getenv("IP")
TOKEN = os.getenv("TOKEN_TV")

if not IP_TV:
    raise ValueError("A variável IP não foi definida no .env")

nome = base64.b64encode(b"MeuPC-Python").decode()

url = (
    f"wss://{IP_TV}:8002/api/v2/channels/"
    f"samsung.remote.control?name={nome}"
)

if TOKEN:
    url += f"&token={TOKEN}"

ws = websocket.create_connection(
    url,
    timeout=10,
    sslopt={"cert_reqs": ssl.CERT_NONE}
)

try:
    while True:
        mensagem = json.loads(ws.recv())
        print("Evento:", mensagem)

        if mensagem.get("event") == "ms.channel.connect":
            print("Controle remoto conectado.")
            break

    comando = {
        "method": "ms.remote.control",
        "params": {
            "Cmd": "Click",
            "DataOfCmd": "KEY_VOLUP",
            "Option": "false",
            "TypeOfRemote": "SendRemoteKey"
        }
    }

    ws.send(json.dumps(comando))
    print("Comando enviado.")

finally:
    ws.close()
