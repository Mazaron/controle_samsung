# Controle Samsung TV

Aplicativo compacto para controlar a Samsung Smart TV pela rede local. A interface é feita em WPF/C# e o executável atualizado fica em `dist\ControleSamsungTV-Atualizado.exe`. A janela pode ser recolhida na bandeja; o menu do ícone também permite silenciar, desligar e abrir o controle.

## Abrir

Execute `dist\ControleSamsungTV-Atualizado.exe`. O aplicativo procura o `.env` junto ao executável e nas pastas acima dele. Configure o endereço IP e o MAC da TV no `.env` da pasta do projeto:

```dotenv
IP=ENDERECO_IP_DA_TV
TOKEN_TV=TOKEN_GERADO_PELA_TV
MAC_TV=ENDERECO_MAC_DA_TV
IP_BROADCAST=255.255.255.255
```

Na primeira conexão, aceite o controle remoto na tela da TV. O botão **Iniciar com o Windows** registra o aplicativo para o usuário atual.

## Controles

- Ligar por Wake-on-LAN e desligar pelo controle remoto Samsung.
- Volume absoluto por barra deslizante, mudo, início, fonte, guia, canais, navegação, OK, voltar, menu, informação e reprodução. O nível é consultado a cada 2 segundos via UPnP e acompanha mudanças feitas no controle físico.
- Atalhos para Netflix, Prime Video, Globoplay, YouTube e Disney+.
- Acesso pelo ícone na bandeja do Windows.

Os identificadores dos aplicativos podem variar por modelo e região. A API local desta AU7700 informa se alguns aplicativos estão em execução/visíveis, mas não retornou título ou andamento do conteúdo na consulta feita; esses metadados dependem do app e da fonte de reprodução.

## Compilar no Windows

O projeto usa o compilador C# e as bibliotecas WPF do .NET Framework que já acompanham o Windows. No PowerShell, dentro da pasta do projeto:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& $csc /nologo /target:winexe /optimize+ /out:dist\ControleSamsungTV-Atualizado.exe /win32icon:tv.ico `
  /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" `
  /reference:"$wpf\WindowsBase.dll" /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" `
  /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" `
  /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" windows\ControleSamsungTV.cs
```

O Wake-on-LAN usa `MAC_TV` e, opcionalmente, `IP_BROADCAST` do arquivo `.env`.



