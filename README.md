# Controle Samsung TV

Aplicativo compacto para controlar a Samsung Smart TV pela rede local. A interface é feita em WPF/C# e o executável atualizado fica em `dist\ControleSamsungTV-DetectaApp-v4.exe`. A janela pode ser recolhida na bandeja; o menu do ícone também permite silenciar, desligar e abrir o controle.

## Abrir

Execute `dist\ControleSamsungTV-DetectaApp-v4.exe`. O aplicativo procura o `.env` junto ao executável e nas pastas acima dele. Configure o endereço IP e o MAC da TV no `.env` da pasta do projeto:

```dotenv
IP=ENDERECO_IP_DA_TV
TOKEN_TV=TOKEN_GERADO_PELA_TV
MAC_TV=ENDERECO_MAC_DA_TV
IP_BROADCAST=255.255.255.255
SMARTTHINGS_DEVICE_ID=ID_DO_DISPOSITIVO_SMARTTHINGS
SMARTTHINGS_TOKEN=TOKEN_PESSOAL_SMARTTHINGS
```

Na primeira conexão, aceite o controle remoto na tela da TV. O botão **Iniciar com o Windows** registra o aplicativo para o usuário atual.

## Controles

- Ligar por Wake-on-LAN e desligar pelo controle remoto Samsung.
- Volume absoluto por barra deslizante, mudo, início, fonte, guia, canais, navegação, OK, voltar, menu, informação e reprodução. O nível é consultado a cada 2 segundos via UPnP e acompanha mudanças feitas no controle físico.
- Atalhos com nomes para Netflix, Prime Video e Globoplay na tela principal; YouTube, Disney+, Samsung TV Plus e Twitch também aparecem na lista de aplicativos. Samsung TV Plus é acionado pelo comando de TV.
- Acesso pelo ícone na bandeja do Windows.

Os identificadores dos aplicativos podem variar por modelo e região. A tela consulta a cada 5 segundos o estado dos apps pela API local da TV, prioriza o app visível e tenta reconhecer Samsung TV Plus por seus identificadores de sistema. Com `SMARTTHINGS_DEVICE_ID` e `SMARTTHINGS_TOKEN`, também lê o canal atual pela capacidade `tvChannelName`. O PAT do SmartThings expira em 24 horas; mantenha-o apenas no `.env` local, que não é versionado. Essa capacidade informa canal, não uma prévia em vídeo nem necessariamente o título do programa.

## Compilar no Windows

O projeto usa o compilador C# e as bibliotecas WPF do .NET Framework que já acompanham o Windows. No PowerShell, dentro da pasta do projeto:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& $csc /nologo /target:winexe /optimize+ /out:dist\ControleSamsungTV-DetectaApp-v4.exe /win32icon:tv.ico `
  /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" `
  /reference:"$wpf\WindowsBase.dll" /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" `
  /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" `
  /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" windows\ControleSamsungTV.cs
```

O Wake-on-LAN usa `MAC_TV` e, opcionalmente, `IP_BROADCAST` do arquivo `.env`.



