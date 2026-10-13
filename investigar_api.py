
import os
import requests
from dotenv import load_dotenv

load_dotenv()

ip = os.getenv("IP")

if not ip:
    raise ValueError("A variável IP não foi definida no .env")

url = f"https://{ip}:8002/api/v2/"

resposta = requests.get(
    url,
    timeout=10,
    verify=False
)

print("Status HTTP:", resposta.status_code)
print("Conteúdo retornado:")
print(resposta.text)
