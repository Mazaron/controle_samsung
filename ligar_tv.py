import os

from dotenv import load_dotenv
from wakeonlan import wake

load_dotenv()
MAC_TV = os.getenv("MAC_TV", "").strip()
IP_BROADCAST = os.getenv("IP_BROADCAST", "255.255.255.255").strip()

if not MAC_TV:
    raise ValueError("Configure MAC_TV no arquivo .env para usar Wake-on-LAN.")

wake(MAC_TV, host=IP_BROADCAST)

print(f"Pacote Wake-on-LAN enviado para {IP_BROADCAST}.")
